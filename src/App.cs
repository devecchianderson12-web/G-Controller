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
        public const string Version = "1.5.0";

        // true si la app se reabrió sola tras un fallo: arranca oculta en la bandeja.
        public static bool Restarted;
        static readonly DateTime started = DateTime.UtcNow;

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
            Restarted = args.Length > 0 && args[0] == "--restart";

            // Un error inesperado nunca debe cerrar la app: se anota y se sigue.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Log(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Crashed(e.ExceptionObject as Exception);

            bool fresh;
            using (var m = new Mutex(true, "GController.single", out fresh))
            {
                if (!fresh && Restarted)
                {
                    // La copia anterior se está cerrando: esperar a que suelte el mutex.
                    try { fresh = m.WaitOne(15000); }
                    catch (AbandonedMutexException) { fresh = true; }
                }
                if (!fresh)
                {
                    // Ya está abierta (quizá solo en la bandeja): pedirle que muestre su ventana.
                    AllowSetForegroundWindow(-1); // que la otra copia pueda pasar al frente
                    PostMessage((IntPtr)0xFFFF, MainForm.WM_SHOWME, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
        }

        // Último recurso: si un hilo falla igual, se anota y la app se reabre sola en la bandeja.
        static void Crashed(Exception e)
        {
            Log(e);
            if ((DateTime.UtcNow - started).TotalSeconds < 60) return; // evitar un bucle si falla al abrir
            try { Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--restart") { UseShellExecute = false }); } catch { }
        }

        // Errores en %LOCALAPPDATA%\GController\errores.log (máx. ~200 KB).
        public static void Log(Exception e)
        {
            try
            {
                string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GController");
                System.IO.Directory.CreateDirectory(dir);
                string file = System.IO.Path.Combine(dir, "errores.log");
                if (System.IO.File.Exists(file) && new System.IO.FileInfo(file).Length > 200000) System.IO.File.Delete(file);
                System.IO.File.AppendAllText(file, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  v" + Version + "\r\n" + e + "\r\n\r\n");
            }
            catch { }
        }

        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);

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

        // Lo último aplicado a cada aparato (-1 = nunca). Se vuelve a aplicar cada vez que el aparato se conecta.
        public static int LightEffect { get { return Get("LightEffect", -1); } set { Set("LightEffect", value); } }
        public static int LightColor { get { return Get("LightColor", 0); } set { Set("LightColor", value); } }
        public static int Sidetone { get { return Get("Sidetone", -1); } set { Set("Sidetone", value); } }
        public static int PowerOff { get { return Get("PowerOff", -1); } set { Set("PowerOff", value); } }
        public static int Dpi { get { return Get("Dpi", -1); } set { Set("Dpi", value); } }

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

    partial class MainForm : Form
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
            FormBorderStyle = FormBorderStyle.None; // el marco que anima Windows se agrega en CreateParams
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
            // El contenido llena la ventana; al maximizar se reparte el espacio (ver Responsive).
            stage.Dock = DockStyle.Fill;
            stage.BackColor = Theme.Back;
            body.Controls.Add(stage);
            foreach (var p in new[] { home, device, settings }) { p.Dock = DockStyle.Fill; p.BackColor = Theme.Back; stage.Controls.Add(p); }
            Controls.Add(body);
            Controls.Add(top);
            device.Resize += (s, e) => Responsive(device);
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

            headset.GKey += down => { if (down && IsHandleCreated) Ui(new Action(RunG1)); };

            Settings.FixRunPath();
            timer.Interval = 30000;
            timer.Tick += (s, e) => Poll();
            timer.Start();

            // Buscar actualización una sola vez, 8 s después de abrir. El aviso sale solo si hay una versión nueva.
            updTimer.Interval = 8000;
            updTimer.Tick += (s, e) => { updTimer.Stop(); CheckUpdate(false); };
            updTimer.Start();

            // Procesado de voz guardado: arranca si estaba activo.
            UpdateEngine(false);
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

        // Maximizar con la animación nativa de Windows, como G HUB.
        void ToggleMaximize()
        {
            WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        }

        // Windows suma el marco invisible al restaurar; se guarda el tamaño normal y se repone.
        Rectangle normalBounds = new Rectangle(0, 0, 1040, 700);
        FormWindowState lastState = FormWindowState.Normal;
        int restoreUntil;

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            var wa = Screen.FromHandle(Handle).WorkingArea;
            Bounds = normalBounds = new Rectangle(wa.Left + (wa.Width - 1040) / 2, wa.Top + (wa.Height - 700) / 2, 1040, 700);
            MinimumSize = new Size(1040, 700);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Normal)
            {
                if (lastState == FormWindowState.Maximized) restoreUntil = Environment.TickCount + 400;
                else if (IsHandleCreated && Visible && Environment.TickCount > restoreUntil) normalBounds = Bounds;
            }
            lastState = WindowState;
            if (max == null) return;
            max.Restore = WindowState == FormWindowState.Maximized;
            max.Invalidate();
        }

        void Drag(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || e.Clicks > 1) return;
            ReleaseCapture();
            SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
        }

        void ShowPage(Panel p)
        {
            foreach (Control c in stage.Controls) c.Visible = c == p;
            Responsive(p);
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

            var left = new Panel { Name = "grow", Location = new Point(36, 74), Size = new Size(440, 540), BackColor = Theme.Back };
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
                    float bw = left.ClientSize.Width - 20, bh = left.ClientSize.Height - 120;
                    float sc = Math.Min(bw / pic.Width, bh / pic.Height);
                    float w = pic.Width * sc, h = pic.Height * sc;
                    g.DrawImage(pic, (bw - w) / 2, 96 + (bh - h) / 2, w, h);
                }
            };
            device.Controls.Add(left);
            return device;
        }

        // Al agrandar la ventana: la columna derecha se pega al borde derecho y baja al centro,
        // los paneles "grow" crecen y los "growH" crecen solo en alto. Base: 1040 x 616.
        readonly System.Collections.Generic.Dictionary<Control, Rectangle> baseBounds = new System.Collections.Generic.Dictionary<Control, Rectangle>();
        public int RightSplit = 480;
        public bool CenterRight;

        void Responsive(Control page)
        {
            if (page != device) return;
            int dx = Math.Max(0, page.ClientSize.Width - 1040), dy = Math.Max(0, page.ClientSize.Height - 616);
            foreach (Control c in page.Controls)
            {
                Rectangle b;
                if (!baseBounds.TryGetValue(c, out b)) { b = c.Bounds; baseBounds[c] = b; c.Disposed += (s, e) => baseBounds.Remove((Control)s); }
                if (c.Name == "grow") c.Bounds = new Rectangle(b.X, b.Y, b.Width + dx, b.Height + dy);
                else if (c.Name == "growH") c.Bounds = new Rectangle(b.X, b.Y, b.Width, b.Height + dy);
                else if (b.X >= RightSplit) c.Location = new Point(b.X + (CenterRight ? dx / 2 : dx), b.Y + dy / 2);
            }
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
            Bg(() =>
            {
                bool done;
                try { done = work(); } catch { done = false; }
                if (!IsHandleCreated) return;
                Ui(new Action(() => { if (!status.IsDisposed) status.Text = done ? ok : "No responde. ¿Está encendido?"; }));
            });
        }

        void OpenHeadset()
        {
            RightSplit = 480; CenterRight = false;
            NewDevicePage("G733", imgHeadset, () => headsetLevel);
            int x = 520, y = 24, w = 480;
            var micPage = new FlatButton { Text = "Micrófono y voz  ›", Width = 170, Location = new Point(196, 22) };
            micPage.Click += (s, e) => OpenMic();
            device.Controls.Add(micPage);
            var statusOff = new Label { Text = "", ForeColor = Theme.Dim, AutoSize = false, Size = new Size(w - 160, 34), BackColor = Theme.Back, TextAlign = ContentAlignment.MiddleLeft };

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

            // Apagado automático tras minutos sin sonido.
            Header("APAGADO AUTOMÁTICO", x, y);
            y += 28;
            int[] offs = { 0, 5, 10, 15, 30 };
            var offButtons = new FlatButton[offs.Length];
            int obw = (w - (offs.Length - 1) * 8) / offs.Length;
            for (int i = 0; i < offs.Length; i++)
            {
                int m = offs[i], idx = i;
                var b = new FlatButton { Text = m == 0 ? "Nunca" : m + " min", Size = new Size(obw, 32), Location = new Point(x + i * (obw + 8), y) };
                b.Click += (s, e) =>
                {
                    Mark(offButtons, idx);
                    Settings.PowerOff = m;
                    Device(statusOff, () => headset.Ensure() && headset.SetPowerOff(m), m == 0 ? "Apagado automático desactivado." : "Se apaga tras " + m + " min sin sonido.");
                };
                offButtons[i] = b;
                device.Controls.Add(b);
            }
            y += 42;

            // Iluminación de las dos zonas.
            Header("ILUMINACIÓN", x, y);
            y += 30;
            var effects = new FlatButton[G733.Effects.Length];
            // Mostrar lo último guardado.
            int effect = Math.Max(0, Settings.LightEffect);
            Color color = Settings.LightEffect >= 0 ? Color.FromArgb(Settings.LightColor) : Presets[0];
            int savedSwatch = Array.FindIndex(Presets, p => p.ToArgb() == color.ToArgb());
            if (savedSwatch < 0) savedSwatch = Presets.Length;
            int ebw = (w - 3 * 8) / 4;
            for (int i = 0; i < effects.Length; i++)
            {
                int idx = i;
                var b = new FlatButton { Text = G733.Effects[i].Name, Size = new Size(ebw, 32), Location = new Point(x + i * (ebw + 8), y) };
                b.Click += (s, e) => { effect = idx; Mark(effects, idx); };
                effects[i] = b;
                device.Controls.Add(b);
            }
            Mark(effects, effect);
            y += 42;
            var swatches = new Swatch[Presets.Length + 1];
            for (int i = 0; i <= Presets.Length; i++)
            {
                int idx = i;
                var sw = new Swatch { Color = i < Presets.Length ? Presets[i] : (savedSwatch == Presets.Length ? color : Color.Black), Custom = i == Presets.Length && savedSwatch != Presets.Length, Selected = i == savedSwatch, Location = new Point(x + i * 34, y) };
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
            var status = statusOff;
            status.Location = new Point(x, y);
            var apply = new FlatButton { Text = "Aplicar luces", Filled = true, Size = new Size(150, 36), Location = new Point(x + w - 150, y) };
            device.Controls.Add(status);
            device.Controls.Add(apply);
            apply.Click += (s, e) =>
            {
                var c = color;
                Settings.LightEffect = effect;
                Settings.LightColor = c.ToArgb();
                int fxIndex = effect;
                Device(status, () => headset.Ensure() && ApplyLight(fxIndex, c), "Luces aplicadas.");
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
                Settings.Sidetone = v;
                Device(status, () => headset.Ensure() && headset.SetSidetone(v), "Tono lateral: " + v + "%.");
            };
            device.Disposed += (s, e) => sideTimer.Dispose();

            Bg(() =>
            {
                int v = -1, off = -1;
                try { if (headset.Ensure()) { v = headset.Sidetone(); off = headset.PowerOff(); } } catch { }
                if (!IsHandleCreated) return;
                Ui(new Action(() =>
                {
                    if (side.IsDisposed) return;
                    if (v >= 0) side.Value = (v + 5) / 10;
                    Mark(offButtons, Array.IndexOf(offs, off));
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
            RightSplit = 480; CenterRight = false;
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
                Settings.Dpi = v;
                Device(status, () => mouse.Ensure() && mouse.SetDpi(v), "DPI: " + v + ".");
            };
            device.Disposed += (s, e) => dpiTimer.Dispose();

            Bg(() =>
            {
                int d = -1, r = -1;
                try { if (mouse.Ensure()) { d = mouse.Dpi(); r = mouse.Rate(); } } catch { }
                if (!IsHandleCreated) return;
                Ui(new Action(() =>
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
            Bg(() =>
            {
                var r = Updater.Check();
                if (!IsHandleCreated) return;
                Ui(new Action(() =>
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
            toast.UpdateClicked += (s, e) => Bg(() =>
            {
                string err = Updater.Prepare(r, Application.ExecutablePath);
                Ui(new Action(() =>
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
            if (!IsHandleCreated && (Settings.StartMinimized || Program.Restarted))
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
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            SetForegroundWindow(Handle);
            Poll();
        }

        void Quit()
        {
            quitting = true;
            Close();
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            if (WindowState == FormWindowState.Normal && lastState == FormWindowState.Normal && Visible) normalBounds.Location = Location;
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
            engine.Dispose();
            headset.Dev.Dispose();
            mouse.Dev.Dispose();
            base.OnFormClosing(e);
        }

        void Poll()
        {
            if (snapping || Interlocked.Exchange(ref busy, 1) == 1) return;
            Bg(() =>
            {
                int hs = -1, ms = -1;
                bool charging;
                try
                {
                    if (headset.Ensure())
                    {
                        hs = headset.Battery(out charging);
                        if (hs < 0) headset.Dev.Close();
                        else
                        {
                            headset.DivertGKey(true); // el auricular olvida esto al apagarse
                            if (!headsetOnline) ApplyHeadset(); // recién encendido: reponer lo guardado
                        }
                    }
                }
                catch { headset.Dev.Close(); hs = -1; }
                headsetOnline = hs >= 0;
                try
                {
                    if (mouse.Ensure())
                    {
                        ms = mouse.Battery(out charging);
                        if (ms < 0) mouse.Dev.Close();
                        else if (!mouseOnline && Settings.Dpi > 0) mouse.SetDpi(Settings.Dpi);
                    }
                }
                catch { mouse.Dev.Close(); ms = -1; }
                mouseOnline = ms >= 0;
                bool ghub = false;
                try { ghub = GHubRunning(); } catch { }
                busy = 0;
                // Si falta un aparato se revisa cada 3 s para reponer su config apenas se encienda.
                int next = headsetOnline && mouseOnline ? 30000 : 3000;
                Ui(new Action(() => { if (timer.Interval != next) timer.Interval = next; }));
                Ui(new Action(() => Render(hs, ms, ghub)));
            }, () => busy = 0);
        }

        // Trabajo en segundo plano: un error ahí no puede cerrar la app. "after" corre siempre al final.
        static void Bg(Action work, Action after = null)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { work(); }
                catch (Exception e) { Program.Log(e); }
                finally { if (after != null) after(); }
            });
        }

        // Pasa algo al hilo de la ventana sin fallar si la ventana se está cerrando o recreando.
        void Ui(Action a)
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(() => { try { a(); } catch (Exception e) { Program.Log(e); } })); }
            catch { }
        }

        bool headsetOnline, mouseOnline;

        // Luces en las dos zonas del G733.
        bool ApplyLight(int effectIndex, Color c)
        {
            if (effectIndex < 0 || effectIndex >= G733.Effects.Length) return false;
            ushort id = G733.Effects[effectIndex].Id;
            return headset.SetLight(id, c, 4000, 0) && headset.SetLight(id, c, 4000, 1);
        }

        // El G733 vuelve a su config de fábrica al encenderse: se repone todo lo guardado.
        void ApplyHeadset()
        {
            if (Settings.LightEffect >= 0) ApplyLight(Settings.LightEffect, Color.FromArgb(Settings.LightColor));
            if (Settings.Sidetone >= 0) headset.SetSidetone(Settings.Sidetone);
            if (Settings.PowerOff >= 0) headset.SetPowerOff(Settings.PowerOff);
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
            OpenMic(); Application.DoEvents(); save(this, "mic.png");
            ClientSize = new Size(1600, 900); Application.DoEvents();
            OpenHeadset(); Application.DoEvents(); save(this, "g733_max.png");
            OpenMic(); Application.DoEvents(); save(this, "mic_max.png");
            ShowPage(home); Application.DoEvents(); save(this, "home_max.png");
            ClientSize = new Size(1040, 700); Application.DoEvents();
            ShowPage(settings); save(this, "settings.png");
            save(popup, "tray.png");
            var o = new Osd();
            o.Location = new Point(-4000, -2000);
            o.Flash("Micrófono silenciado");
            Application.DoEvents();
            save(o, "osd.png");
        }

        // Estilos de ventana normal (marco, maximizar, minimizar) para que Windows anime igual que en G HUB.
        // WndProc quita el borde visible.
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style |= 0x00C00000 | 0x00040000 | 0x00080000 | 0x00010000 | 0x00020000; // caption, thickframe, sysmenu, max, min
                return cp;
            }
        }

        // Sin borde visible: el tamaño de ventana es igual al del área cliente.
        protected override Size SizeFromClientSize(Size clientSize) { return clientSize; }

        // Otra copia de la app pide mostrar la ventana; el Explorador avisa que se reinició la barra de tareas.
        public static readonly int WM_SHOWME = RegisterWindowMessage("GController.ShowMe");
        static readonly int WM_TASKBARCREATED = RegisterWindowMessage("TaskbarCreated");

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_SHOWME && WM_SHOWME != 0) { ShowWindow(); return; }
            if (m.Msg == WM_TASKBARCREATED && WM_TASKBARCREATED != 0)
            {
                // El Explorador se reinició: volver a poner el icono en la bandeja.
                tray.Visible = false;
                tray.Visible = true;
            }
            const int WM_NCCALCSIZE = 0x83, WM_NCHITTEST = 0x84, WM_WINDOWPOSCHANGING = 0x46;
            bool restoring = lastState == FormWindowState.Maximized || Environment.TickCount < restoreUntil;
            if (m.Msg == WM_WINDOWPOSCHANGING && restoring && !IsZoomed(Handle) && !IsIconic(Handle))
            {
                // Al restaurar desde maximizada, volver exactamente al tamaño normal guardado.
                var wp = (WINDOWPOS)Marshal.PtrToStructure(m.LParam, typeof(WINDOWPOS));
                if ((wp.flags & 0x0001) == 0) { wp.cx = normalBounds.Width; wp.cy = normalBounds.Height; } // sin SWP_NOSIZE
                Marshal.StructureToPtr(wp, m.LParam, false);
            }
            if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
            {
                // Sin barra de título ni borde. Maximizada, Windows la pone un poco fuera de la pantalla: se recorta.
                if (WindowState == FormWindowState.Maximized)
                {
                    var r = (RECT)Marshal.PtrToStructure(m.LParam, typeof(RECT));
                    int fx = GetSystemMetrics(32) + GetSystemMetrics(92), fy = GetSystemMetrics(33) + GetSystemMetrics(92);
                    r.Left += fx; r.Top += fy; r.Right -= fx; r.Bottom -= fy;
                    Marshal.StructureToPtr(r, m.LParam, false);
                }
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg == WM_NCHITTEST && WindowState == FormWindowState.Normal)
            {
                // Bordes para cambiar el tamaño.
                var p = PointToClient(new Point((short)((long)m.LParam & 0xFFFF), (short)(((long)m.LParam >> 16) & 0xFFFF)));
                const int B = 6;
                bool l = p.X < B, r = p.X >= ClientSize.Width - B, t = p.Y < B, b = p.Y >= ClientSize.Height - B;
                int hit = t && l ? 13 : t && r ? 14 : b && l ? 16 : b && r ? 17 : l ? 10 : r ? 11 : t ? 12 : b ? 15 : 0;
                if (hit != 0) { m.Result = (IntPtr)hit; return; }
            }
            base.WndProc(ref m);
        }

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct WINDOWPOS { public IntPtr hwnd, after; public int x, y, cx, cy; public uint flags; }
        [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int RegisterWindowMessage(string name);
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
