using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace GControl
{
    static class Program
    {
        public const string Version = "1.1.0";

        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--snap")
            {
                // Capturas de la UI para revisar el diseño sin abrir la ventana.
                Application.EnableVisualStyles();
                using (var f = new MainForm()) f.Snap(args[1]);
                return;
            }
            bool fresh;
            using (var m = new Mutex(true, "GController.single", out fresh))
            {
                if (!fresh) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
        }

        public static Image Resource(string name)
        {
            using (var s = typeof(Program).Assembly.GetManifestResourceStream(name))
                return s == null ? null : new Bitmap(s);
        }
    }

    static class Settings
    {
        const string Key = @"Software\GController";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "GController";

        static int Get(string name, int def)
        {
            using (var k = Registry.CurrentUser.OpenSubKey(Key))
                return k == null ? def : (int)k.GetValue(name, def);
        }

        static void Set(string name, int value)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(Key))
                    k.SetValue(name, value, RegistryValueKind.DWord);
            }
            catch { }
        }

        public static bool StartMinimized { get { return Get("StartMinimized", 0) == 1; } set { Set("StartMinimized", value ? 1 : 0); } }
        public static bool GShift { get { return Get("GShift", 0) == 1; } set { Set("GShift", value ? 1 : 0); } }
        public static int G1Normal { get { return Get("G1Normal", 1); } set { Set("G1Normal", value); } }
        public static int G1Shift { get { return Get("G1Shift", GAction.MicMute); } set { Set("G1Shift", value); } }

        public static bool StartWithWindows
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(RunName) != null;
            }
            set
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) k.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue(RunName, false);
                }
            }
        }

        // Si movieron el .exe, arreglar la ruta de inicio.
        public static void FixRunPath()
        {
            try { if (StartWithWindows) StartWithWindows = true; } catch { }
        }
    }

    class MainForm : Form
    {
        static readonly Color[] Presets =
        {
            Color.FromArgb(0, 160, 255), Color.FromArgb(255, 0, 0), Color.FromArgb(255, 110, 0), Color.FromArgb(255, 220, 0),
            Color.FromArgb(0, 255, 60), Color.FromArgb(170, 0, 255), Color.FromArgb(255, 0, 150), Color.FromArgb(255, 255, 255),
        };
        static readonly int[] DpiPresets = { 400, 800, 1600, 3200, 6400, 12000 };

        readonly G733 headset = new G733();
        readonly G305 mouse = new G305();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly TrayPopup popup = new TrayPopup();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer updTimer = new System.Windows.Forms.Timer();
        readonly Panel top = new Panel(), body = new Panel(), stage = new Panel();
        readonly Panel home = new Panel(), device = new Panel(), settings = new Panel();
        readonly DeviceCard cardMouse = new DeviceCard(), cardHeadset = new DeviceCard();
        readonly Label ghubWarn = new Label();
        readonly Image imgMouse = Program.Resource("g305.png"), imgHeadset = Program.Resource("g733.png");
        readonly Font h1 = Theme.Bold(20f), h2 = Theme.Bold(11f), small = Theme.Ui(9.5f);
        readonly Label updStatus = new Label();
        int mouseLevel = -2, headsetLevel = -2;
        int busy;
        bool quitting, snapping;
        UpdateToast toast;
        Osd osd;

        public MainForm()
        {
            Text = "G Controller";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = Theme.Ui(10f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1040, 700);
            Icon = MakeIcon(32);

            BuildTop();
            body.Dock = DockStyle.Fill;
            body.BackColor = Theme.Back;
            // El contenido mide siempre lo mismo y se centra al maximizar.
            stage.Size = new Size(1040, 616);
            stage.BackColor = Theme.Back;
            body.Controls.Add(stage);
            body.Resize += (s, e) => stage.Location = new Point(Math.Max(0, (body.Width - stage.Width) / 2), Math.Max(0, (body.Height - stage.Height) / 2));
            foreach (var p in new[] { home, device, settings }) { p.Dock = DockStyle.Fill; p.BackColor = Theme.Back; stage.Controls.Add(p); }
            Controls.Add(body);
            Controls.Add(top);
            BuildHome();
            BuildSettings();
            ShowPage(home);

            popup.ItemClicked += i =>
            {
                if (i == 0) { ShowWindow(); ShowPage(home); }
                else if (i == 1) { ShowWindow(); ShowPage(settings); }
                else Quit();
            };
            tray.Text = "G Controller";
            tray.Icon = MakeIcon(SystemInformation.SmallIconSize.Width);
            tray.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) ShowWindow();
                else if (e.Button == MouseButtons.Right) { popup.ShowAt(Cursor.Position); SetForegroundWindow(popup.Handle); Poll(); }
            };
            tray.Visible = true;

            headset.GKey += down => { if (down && IsHandleCreated) BeginInvoke(new Action(RunG1)); };

            Settings.FixRunPath();
            timer.Interval = 30000;
            timer.Tick += (s, e) => Poll();
            timer.Start();

            // Buscar actualización a los 8 s y luego cada 6 h.
            updTimer.Interval = 8000;
            updTimer.Tick += (s, e) => { updTimer.Interval = 6 * 3600 * 1000; CheckUpdate(false); };
            updTimer.Start();
        }

        static Icon MakeIcon(int size)
        {
            using (var bmp = new Bitmap(size, size))
            {
                using (var g = Graphics.FromImage(bmp)) Art.DrawLogo(g, 0, 0, size);
                IntPtr h = bmp.GetHicon();
                var ic = (Icon)Icon.FromHandle(h).Clone();
                DestroyIcon(h);
                return ic;
            }
        }

        // ---------- Botón G1 ----------

        void RunG1()
        {
            var action = GAction.Get(Settings.GShift ? Settings.G1Shift : Settings.G1Normal);
            string text = action.Run();
            if (text == null) return;
            if (osd == null || osd.IsDisposed) osd = new Osd();
            osd.Flash(text);
        }

        // ---------- Barra superior ----------

        void BuildTop()
        {
            top.Dock = DockStyle.Top;
            top.Height = 84;
            top.BackColor = Theme.Back;
            top.Paint += (s, e) =>
            {
                Art.DrawLogo(e.Graphics, 28, 24, 36);
                using (var pen = new Pen(Theme.Line)) e.Graphics.DrawLine(pen, 0, top.Height - 1, top.Width, top.Height - 1);
            };
            top.MouseDown += Drag;
            top.DoubleClick += (s, e) => ToggleMaximize();

            var tab = new Label { Text = "Dispositivos", ForeColor = Theme.Accent, Font = Theme.Bold(11f), AutoSize = true, Cursor = Cursors.Hand, BackColor = Theme.Back };
            tab.Location = new Point(92, 31);
            tab.Click += (s, e) => ShowPage(home);
            top.Controls.Add(tab);

            var gear = new IconButton(IconButton.Kind.Gear) { Size = new Size(40, 40) };
            gear.Click += (s, e) => ShowPage(settings);
            var min = new IconButton(IconButton.Kind.Min) { Size = new Size(44, 32) };
            min.Click += (s, e) => Hide();
            max = new IconButton(IconButton.Kind.Max) { Size = new Size(44, 32) };
            max.Click += (s, e) => ToggleMaximize();
            var close = new IconButton(IconButton.Kind.Close) { Size = new Size(44, 32) };
            close.Click += (s, e) => Hide();
            top.Controls.AddRange(new Control[] { gear, min, max, close });
            top.Resize += (s, e) =>
            {
                close.Location = new Point(top.Width - 45, 1);
                max.Location = new Point(top.Width - 89, 1);
                min.Location = new Point(top.Width - 133, 1);
                gear.Location = new Point(top.Width - 140, 36);
            };
        }

        IconButton max;

        // Maximiza sin tapar la barra de tareas.
        void ToggleMaximize()
        {
            if (WindowState == FormWindowState.Maximized) WindowState = FormWindowState.Normal;
            else
            {
                MaximizedBounds = Screen.FromHandle(Handle).WorkingArea;
                WindowState = FormWindowState.Maximized;
            }
            max.Restore = WindowState == FormWindowState.Maximized;
            max.Invalidate();
        }

        void Drag(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || e.Clicks > 1 || WindowState == FormWindowState.Maximized) return;
            ReleaseCapture();
            SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
        }

        void ShowPage(Panel p)
        {
            foreach (Control c in stage.Controls) c.Visible = c == p;
        }

        // ---------- Inicio: tarjetas ----------

        void BuildHome()
        {
            cardMouse.Title = "G305";
            cardMouse.Picture = imgMouse;
            cardHeadset.Title = "G733";
            cardHeadset.Picture = imgHeadset;
            cardMouse.Click += (s, e) => OpenMouse();
            cardHeadset.Click += (s, e) => OpenHeadset();
            ghubWarn.ForeColor = Color.FromArgb(240, 180, 50);
            ghubWarn.BackColor = Theme.Back;
            ghubWarn.AutoSize = true;
            ghubWarn.Text = "G HUB está abierto: ciérralo para que no cambie tus ajustes ni el botón G1 haga dos cosas.";
            ghubWarn.Visible = false;
            home.Controls.Add(cardMouse);
            home.Controls.Add(cardHeadset);
            home.Controls.Add(ghubWarn);
            home.Resize += (s, e) => LayoutCards();
            LayoutCards();
        }

        void LayoutCards()
        {
            int w = 280, h = 450, gap = 24;
            int x = (home.ClientSize.Width - (w * 2 + gap)) / 2;
            int y = Math.Max(40, (home.ClientSize.Height - h) / 2);
            cardMouse.SetBounds(x, y, w, h);
            cardHeadset.SetBounds(x + w + gap, y, w, h);
            ghubWarn.Location = new Point(Math.Max(16, (home.ClientSize.Width - ghubWarn.Width) / 2), y - 30);
        }

        static bool GHubRunning()
        {
            foreach (var n in new[] { "lghub", "lghub_agent", "lghub_system_tray" })
                if (Process.GetProcessesByName(n).Length > 0) return true;
            return false;
        }

        // ---------- Páginas de aparato ----------

        Panel NewDevicePage(string title, Image pic, Func<int> level)
        {
            device.SuspendLayout();
            foreach (Control c in device.Controls) c.Dispose();
            device.Controls.Clear();

            var back = new FlatButton { Text = "‹  Dispositivos", Width = 150, Location = new Point(36, 22) };
            back.Click += (s, e) => ShowPage(home);
            device.Controls.Add(back);

            var left = new Panel { Location = new Point(36, 74), Size = new Size(440, 540), BackColor = Theme.Back };
            left.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                TextRenderer.DrawText(g, title, h1, new Point(0, 0), Theme.Text);
                int lv = level();
                if (lv >= 0)
                {
                    Art.DrawWireless(g, new RectangleF(2, 52, 18, 13), Theme.Dim);
                    Art.DrawBattery(g, new RectangleF(30, 52, 26, 13), lv, Theme.Dim);
                    TextRenderer.DrawText(g, lv + "% batería", small, new Point(62, 48), Art.LevelColor(lv, Theme.Dim));
                }
                else TextRenderer.DrawText(g, "Apagado o sin conexión", small, new Point(0, 48), Theme.Dim);
                if (pic != null)
                {
                    float sc = Math.Min(420f / pic.Width, 420f / pic.Height);
                    float w = pic.Width * sc, h = pic.Height * sc;
                    g.DrawImage(pic, (420 - w) / 2, 96 + (420 - h) / 2, w, h);
                }
            };
            device.Controls.Add(left);
            return device;
        }

        Label Header(string text, int x, int y)
        {
            var l = new Label { Text = text, Font = h2, ForeColor = Theme.Dim, AutoSize = true, Location = new Point(x, y), BackColor = Theme.Back };
            device.Controls.Add(l);
            return l;
        }

        static void Mark(FlatButton[] buttons, int selected)
        {
            for (int i = 0; i < buttons.Length; i++) { buttons[i].Selected = i == selected; buttons[i].Invalidate(); }
        }

        // Hace una operación del aparato en segundo plano y muestra el resultado.
        void Device(Label status, Func<bool> work, string ok)
        {
            status.Text = "Aplicando…";
            ThreadPool.QueueUserWorkItem(_ =>
            {
                bool done;
                try { done = work(); } catch { done = false; }
                if (!IsHandleCreated) return;
                BeginInvoke(new Action(() => { if (!status.IsDisposed) status.Text = done ? ok : "No responde. ¿Está encendido?"; }));
            });
        }

        void OpenHeadset()
        {
            NewDevicePage("G733", imgHeadset, () => headsetLevel);
            int x = 520, y = 24, w = 480;

            // Asignaciones: G1 con dos modos, como Predeterminado / G-SHIFT.
            Header("ASIGNACIONES · BOTÓN G1", x, y);
            y += 32;
            var editShift = Settings.GShift;
            var modeNormal = new FlatButton { Text = "PREDETERMINADO", Size = new Size(150, 32), Location = new Point(x, y) };
            var modeShift = new FlatButton { Text = "G-SHIFT", Size = new Size(110, 32), Location = new Point(x + 158, y) };
            var shift = new Toggle { Label = "G-Shift activo", Width = 190, Location = new Point(x + w - 190, y) };
            device.Controls.AddRange(new Control[] { modeNormal, modeShift, shift });
            y += 44;

            var actions = new FlatButton[GAction.All.Length];
            int cols = 3, bw = (w - (cols - 1) * 8) / cols;
            Action refresh = () =>
            {
                modeNormal.Selected = !editShift; modeShift.Selected = editShift;
                modeNormal.Invalidate(); modeShift.Invalidate();
                int cur = editShift ? Settings.G1Shift : Settings.G1Normal;
                Mark(actions, Array.FindIndex(GAction.All, a => a.Id == cur));
            };
            for (int i = 0; i < actions.Length; i++)
            {
                var a = GAction.All[i];
                var b = new FlatButton { Text = a.Name, Size = new Size(bw, 32), Location = new Point(x + (i % cols) * (bw + 8), y + (i / cols) * 38) };
                b.Click += (s, e) =>
                {
                    if (editShift) Settings.G1Shift = a.Id; else Settings.G1Normal = a.Id;
                    refresh();
                };
                actions[i] = b;
                device.Controls.Add(b);
            }
            y += ((actions.Length + cols - 1) / cols) * 38 + 2;
            modeNormal.Click += (s, e) => { editShift = false; refresh(); };
            modeShift.Click += (s, e) => { editShift = true; refresh(); };
            shift.On = Settings.GShift;
            shift.Changed += (s, e) => { Settings.GShift = shift.On; editShift = shift.On; refresh(); };
            refresh();
            device.Controls.Add(new Label { Text = "El G1 hace la acción del modo activo. El switch cambia de modo.", ForeColor = Theme.Dim, AutoSize = true, Location = new Point(x, y), BackColor = Theme.Back });
            y += 34;

            // Tono lateral: cuánto te escuchas por los auriculares.
            Header("TONO LATERAL", x, y);
            var sideValue = new Label { Text = "—", ForeColor = Theme.Text, AutoSize = false, Size = new Size(60, 22), Location = new Point(x + w - 60, y), TextAlign = ContentAlignment.MiddleRight, BackColor = Theme.Back };
            device.Controls.Add(sideValue);
            y += 28;
            var side = new StepSlider { Max = 10, Location = new Point(x, y), Width = w };
            device.Controls.Add(side);
            y += 40;

            // Nivel del micrófono (ganancia de entrada de Windows).
            Header("NIVEL DE MICRÓFONO", x, y);
            var micValue = new Label { Text = "—", ForeColor = Theme.Text, AutoSize = false, Size = new Size(60, 22), Location = new Point(x + w - 60, y), TextAlign = ContentAlignment.MiddleRight, BackColor = Theme.Back };
            device.Controls.Add(micValue);
            y += 28;
            var mic = new StepSlider { Max = 20, Location = new Point(x, y), Width = w };
            device.Controls.Add(mic);
            int micNow = Audio.MicLevel();
            if (micNow >= 0) { mic.Value = (micNow + 2) / 5; micValue.Text = micNow + "%"; }
            mic.ValueChanged += (s, e) =>
            {
                int v = mic.Value * 5;
                micValue.Text = Audio.SetMicLevel(v) ? v + "%" : "—";
            };
            y += 40;

            // Iluminación de las dos zonas.
            Header("ILUMINACIÓN", x, y);
            y += 30;
            var effects = new FlatButton[G733.Effects.Length];
            int effect = 0;
            Color color = Presets[0];
            int ebw = (w - 3 * 8) / 4;
            for (int i = 0; i < effects.Length; i++)
            {
                int idx = i;
                var b = new FlatButton { Text = G733.Effects[i].Name, Size = new Size(ebw, 32), Location = new Point(x + i * (ebw + 8), y) };
                b.Click += (s, e) => { effect = idx; Mark(effects, idx); };
                effects[i] = b;
                device.Controls.Add(b);
            }
            Mark(effects, 0);
            y += 42;
            var swatches = new Swatch[Presets.Length + 1];
            for (int i = 0; i <= Presets.Length; i++)
            {
                int idx = i;
                var sw = new Swatch { Color = i < Presets.Length ? Presets[i] : Color.Black, Custom = i == Presets.Length, Selected = i == 0, Location = new Point(x + i * 34, y) };
                sw.Click += (s, e) =>
                {
                    if (idx == Presets.Length)
                        using (var dlg = new ColorDialog { FullOpen = true, Color = color })
                        {
                            if (dlg.ShowDialog(this) != DialogResult.OK) return;
                            swatches[idx].Color = dlg.Color;
                        }
                    color = swatches[idx].Color;
                    foreach (var o in swatches) { o.Selected = o == swatches[idx]; o.Invalidate(); }
                };
                swatches[i] = sw;
                device.Controls.Add(sw);
            }
            y += 42;
            var status = new Label { Text = "", ForeColor = Theme.Dim, AutoSize = false, Size = new Size(w - 160, 34), Location = new Point(x, y), TextAlign = ContentAlignment.MiddleLeft, BackColor = Theme.Back };
            var apply = new FlatButton { Text = "Aplicar luces", Filled = true, Size = new Size(150, 36), Location = new Point(x + w - 150, y) };
            device.Controls.Add(status);
            device.Controls.Add(apply);
            apply.Click += (s, e) =>
            {
                var fx = G733.Effects[effect];
                var c = color;
                Device(status, () => headset.Ensure() && headset.SetLight(fx.Id, c, 4000, 0) && headset.SetLight(fx.Id, c, 4000, 1), "Luces aplicadas.");
            };

            // El tono lateral se aplica al soltar (sin mandar un comando por cada paso).
            var sideTimer = new System.Windows.Forms.Timer { Interval = 300 };
            bool loading = true;
            side.ValueChanged += (s, e) =>
            {
                sideValue.Text = side.Value * 10 + "%";
                if (loading) return;
                sideTimer.Stop();
                sideTimer.Start();
            };
            sideTimer.Tick += (s, e) =>
            {
                sideTimer.Stop();
                int v = side.Value * 10;
                Device(status, () => headset.Ensure() && headset.SetSidetone(v), "Tono lateral: " + v + "%.");
            };
            device.Disposed += (s, e) => sideTimer.Dispose();

            ThreadPool.QueueUserWorkItem(_ =>
            {
                int v = -1;
                try { if (headset.Ensure()) v = headset.Sidetone(); } catch { }
                if (!IsHandleCreated) return;
                BeginInvoke(new Action(() =>
                {
                    if (side.IsDisposed) return;
                    if (v >= 0) side.Value = (v + 5) / 10;
                    sideValue.Text = v >= 0 ? side.Value * 10 + "%" : "—";
                    if (v < 0) status.Text = "No se pudo leer el G733.";
                    loading = false;
                }));
            });

            device.ResumeLayout();
            ShowPage(device);
        }

        void OpenMouse()
        {
            NewDevicePage(mouse.Name, imgMouse, () => mouseLevel);
            int x = 520, y = 24, w = 480;

            Header("SENSIBILIDAD (DPI)", x, y);
            var dpiValue = new Label { Text = "—", Font = Theme.Bold(14f), ForeColor = Theme.Text, AutoSize = false, Size = new Size(120, 28), Location = new Point(x + w - 120, y - 4), TextAlign = ContentAlignment.MiddleRight, BackColor = Theme.Back };
            device.Controls.Add(dpiValue);
            y += 34;
            var dpi = new StepSlider { Max = (12000 - 200) / 50, Location = new Point(x, y), Width = w };
            device.Controls.Add(dpi);
            y += 40;
            var presets = new FlatButton[DpiPresets.Length];
            int pbw = (w - (presets.Length - 1) * 8) / presets.Length;
            for (int i = 0; i < presets.Length; i++)
            {
                int v = DpiPresets[i];
                var b = new FlatButton { Text = v.ToString(), Size = new Size(pbw, 32), Location = new Point(x + i * (pbw + 8), y) };
                b.Click += (s, e) => dpi.Value = (v - 200) / 50;
                presets[i] = b;
                device.Controls.Add(b);
            }
            y += 56;

            Header("TASA DE INFORME", x, y);
            var rateValue = new Label { Text = "—", ForeColor = Theme.Text, AutoSize = true, Location = new Point(x, y + 30), BackColor = Theme.Back };
            device.Controls.Add(rateValue);
            var status = new Label { Text = "Leyendo…", ForeColor = Theme.Dim, AutoSize = false, Size = new Size(w, 34), BackColor = Theme.Back, TextAlign = ContentAlignment.MiddleLeft };
            y += 70;
            status.Location = new Point(x, y);
            device.Controls.Add(status);
            device.Controls.Add(new Label
            {
                Text = "Los botones DPI del ratón siguen funcionando y pueden cambiar este valor.",
                ForeColor = Theme.Dim, AutoSize = true, Location = new Point(x, y + 40), BackColor = Theme.Back,
            });

            var dpiTimer = new System.Windows.Forms.Timer { Interval = 350 };
            bool loading = true;
            dpi.ValueChanged += (s, e) =>
            {
                int v = 200 + dpi.Value * 50;
                dpiValue.Text = v + " DPI";
                Mark(presets, Array.IndexOf(DpiPresets, v));
                if (loading) return;
                dpiTimer.Stop();
                dpiTimer.Start();
            };
            dpiTimer.Tick += (s, e) =>
            {
                dpiTimer.Stop();
                int v = 200 + dpi.Value * 50;
                Device(status, () => mouse.Ensure() && mouse.SetDpi(v), "DPI: " + v + ".");
            };
            device.Disposed += (s, e) => dpiTimer.Dispose();

            ThreadPool.QueueUserWorkItem(_ =>
            {
                int d = -1, r = -1;
                try { if (mouse.Ensure()) { d = mouse.Dpi(); r = mouse.Rate(); } } catch { }
                if (!IsHandleCreated) return;
                BeginInvoke(new Action(() =>
                {
                    if (dpi.IsDisposed) return;
                    if (d > 0) { dpi.Value = (d - 200) / 50; dpiValue.Text = d + " DPI"; Mark(presets, Array.IndexOf(DpiPresets, d)); }
                    rateValue.Text = r > 0 ? r + " Hz (se cambia desde la memoria del ratón, no por software)" : "—";
                    status.Text = d > 0 ? "Config actual leída del ratón." : "No se pudo leer el ratón. Muévelo para despertarlo.";
                    loading = false;
                }));
            });

            device.ResumeLayout();
            ShowPage(device);
        }

        // ---------- Ajustes ----------

        void BuildSettings()
        {
            int x = 60, y = 40, w = 460;
            settings.Controls.Add(new Label { Text = "Ajustes", Font = h1, ForeColor = Theme.Text, AutoSize = true, Location = new Point(x, y), BackColor = Theme.Back });
            y += 64;
            var auto = new Toggle { Label = "Iniciar con Windows", Width = w, Location = new Point(x, y) };
            auto.On = Settings.StartWithWindows;
            auto.Changed += (s, e) => { try { Settings.StartWithWindows = auto.On; } catch { } };
            settings.Controls.Add(auto);
            y += 44;
            var min = new Toggle { Label = "Iniciar minimizado en la bandeja", Width = w, Location = new Point(x, y) };
            min.On = Settings.StartMinimized;
            min.Changed += (s, e) => Settings.StartMinimized = min.On;
            settings.Controls.Add(min);
            y += 60;
            var check = new FlatButton { Text = "Buscar actualizaciones", Size = new Size(200, 34), Location = new Point(x, y) };
            check.Click += (s, e) => { updStatus.Text = "Buscando…"; CheckUpdate(true); };
            settings.Controls.Add(check);
            updStatus.SetBounds(x + 214, y, 300, 34);
            updStatus.ForeColor = Theme.Dim;
            updStatus.BackColor = Theme.Back;
            updStatus.TextAlign = ContentAlignment.MiddleLeft;
            settings.Controls.Add(updStatus);
            y += 60;
            settings.Controls.Add(new Label
            {
                Text = "Versión " + Program.Version + "  ·  Proyecto no oficial, sin relación con Logitech.",
                ForeColor = Theme.Dim, AutoSize = true, Location = new Point(x, y), BackColor = Theme.Back
            });
        }

        // ---------- Actualizaciones ----------

        void CheckUpdate(bool manual)
        {
            if (snapping || (toast != null && !toast.IsDisposed)) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var r = Updater.Check();
                if (!IsHandleCreated) return;
                BeginInvoke(new Action(() =>
                {
                    if (r != null) ShowToast(r);
                    else if (manual) updStatus.Text = "Ya tienes la última versión.";
                }));
            });
        }

        void ShowToast(Updater.Release r)
        {
            if (toast != null && !toast.IsDisposed) return;
            toast = new UpdateToast(r);
            toast.UpdateClicked += (s, e) => ThreadPool.QueueUserWorkItem(_ =>
            {
                string err = Updater.Prepare(r, Application.ExecutablePath);
                BeginInvoke(new Action(() =>
                {
                    if (err == null) Quit();
                    else toast.Fail(err);
                }));
            });
            toast.Popup();
        }

        // ---------- Ventana y bandeja ----------

        protected override void SetVisibleCore(bool value)
        {
            // Primera vez: respetar "iniciar minimizado".
            if (!IsHandleCreated && Settings.StartMinimized)
            {
                CreateHandle();
                value = false;
            }
            base.SetVisibleCore(value);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Poll();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Theme.Line)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        void ShowWindow()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            Poll();
        }

        void Quit()
        {
            quitting = true;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!quitting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            timer.Stop();
            tray.Visible = false;
            // Devolver el G1 a su función de fábrica.
            try { if (headset.Dev.IsOpen) headset.DivertGKey(false); } catch { }
            headset.Dev.Dispose();
            mouse.Dev.Dispose();
            base.OnFormClosing(e);
        }

        void Poll()
        {
            if (snapping || Interlocked.Exchange(ref busy, 1) == 1) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                int hs = -1, ms = -1;
                bool charging;
                try
                {
                    if (headset.Ensure())
                    {
                        hs = headset.Battery(out charging);
                        if (hs < 0) headset.Dev.Close();
                        else headset.DivertGKey(true); // el auricular olvida esto al apagarse
                    }
                }
                catch { headset.Dev.Close(); }
                try
                {
                    if (mouse.Ensure())
                    {
                        ms = mouse.Battery(out charging);
                        if (ms < 0) mouse.Dev.Close();
                    }
                }
                catch { mouse.Dev.Close(); }
                bool ghub = GHubRunning();
                busy = 0;
                if (IsHandleCreated) BeginInvoke(new Action(() => Render(hs, ms, ghub)));
            });
        }

        static string Pct(int v) { return v < 0 ? "sin conexión" : v + "%"; }

        void Render(int hs, int ms, bool ghub)
        {
            mouseLevel = ms;
            headsetLevel = hs;
            cardMouse.Title = mouse.Name;
            cardMouse.Level = ms;
            cardHeadset.Level = hs;
            popup.MouseName = mouse.Name;
            popup.MouseLevel = ms;
            popup.KeyboardLevel = hs;
            popup.Invalidate();
            ghubWarn.Visible = ghub;
            LayoutCards();
            foreach (Control c in device.Controls) if (c is Panel) c.Invalidate();
            string tip = mouse.Name + " " + Pct(ms) + " · G733 " + Pct(hs);
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        }

        public void Snap(string dir)
        {
            snapping = true;
            tray.Visible = false;
            timer.Stop();
            Render(61, 90, true);
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-4000, -4000);
            base.SetVisibleCore(true);
            popup.Location = new Point(-4000, -3000);
            popup.Show();
            Application.DoEvents();
            Action<Control, string> save = (c, file) =>
            {
                using (var bmp = new Bitmap(c.Width, c.Height))
                {
                    c.DrawToBitmap(bmp, new Rectangle(0, 0, c.Width, c.Height));
                    bmp.Save(System.IO.Path.Combine(dir, file));
                }
            };
            ShowPage(home); save(this, "home.png");
            OpenHeadset(); Application.DoEvents(); save(this, "g733.png");
            OpenMouse(); Application.DoEvents(); save(this, "g305.png");
            ShowPage(settings); save(this, "settings.png");
            save(popup, "tray.png");
            var o = new Osd();
            o.Location = new Point(-4000, -2000);
            o.Flash("Micrófono silenciado");
            Application.DoEvents();
            save(o, "osd.png");
        }

        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    }

    // Aviso flotante abajo al centro (p. ej. "Micrófono silenciado"). No roba el foco.
    class Osd : Form
    {
        static readonly Color Bg = Color.FromArgb(30, 30, 33);
        readonly System.Windows.Forms.Timer hide = new System.Windows.Forms.Timer { Interval = 1600 };
        readonly Font font = Theme.Bold(11f);
        string text = "";

        public Osd()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Bg;
            DoubleBuffered = true;
            Size = new Size(280, 56);
            hide.Tick += (s, e) => { hide.Stop(); Hide(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80 | 0x08000000 | 0x20; // tool, no activate, transparent al ratón
                return cp;
            }
        }

        public void Flash(string message)
        {
            text = message;
            if (Location.X > -1000)
            {
                var wa = Screen.PrimaryScreen.WorkingArea;
                Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Bottom - Height - 60);
            }
            Show();
            Rounded.Apply(this, 14);
            Invalidate();
            hide.Stop();
            hide.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Bg);
            using (var p = Art.Round(new RectangleF(0.5f, 0.5f, Width - 2, Height - 2), 14))
            using (var pen = new Pen(Color.FromArgb(62, 62, 68))) g.DrawPath(pen, p);
            Art.DrawLogo(g, 14, 12, 32);
            TextRenderer.DrawText(g, text, font, new Rectangle(56, 0, Width - 66, Height), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }
    }

    class IconButton : Control
    {
        public enum Kind { Gear, Min, Max, Close }
        public bool Restore;
        readonly Kind kind;
        bool hot;

        public IconButton(Kind kind)
        {
            this.kind = kind;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            BackColor = Theme.Back;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(hot ? (kind == Kind.Close ? Color.FromArgb(200, 30, 30) : Theme.CardHot) : Theme.Back);
            float cx = Width / 2f, cy = Height / 2f;
            using (var pen = new Pen(Theme.Text, 1.6f))
            {
                if (kind == Kind.Min) g.DrawLine(pen, cx - 6, cy, cx + 6, cy);
                else if (kind == Kind.Max && !Restore) g.DrawRectangle(pen, cx - 6, cy - 6, 12, 12);
                else if (kind == Kind.Max)
                {
                    g.DrawRectangle(pen, cx - 6, cy - 3, 9, 9);
                    g.DrawLines(pen, new[] { new PointF(cx - 3, cy - 3), new PointF(cx - 3, cy - 6), new PointF(cx + 6, cy - 6), new PointF(cx + 6, cy + 3), new PointF(cx + 3, cy + 3) });
                }
                else if (kind == Kind.Close) { g.DrawLine(pen, cx - 6, cy - 6, cx + 6, cy + 6); g.DrawLine(pen, cx + 6, cy - 6, cx - 6, cy + 6); }
                else
                {
                    g.DrawEllipse(pen, cx - 4, cy - 4, 8, 8);
                    for (int k = 0; k < 8; k++)
                    {
                        double a = k * Math.PI / 4;
                        g.DrawLine(pen, cx + (float)Math.Cos(a) * 7, cy + (float)Math.Sin(a) * 7, cx + (float)Math.Cos(a) * 10, cy + (float)Math.Sin(a) * 10);
                    }
                    g.DrawEllipse(pen, cx - 7, cy - 7, 14, 14);
                }
            }
        }
    }
}
