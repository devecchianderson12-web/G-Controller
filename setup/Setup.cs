using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace GControl
{
    // Instalador de G Controller: un solo .exe que lleva adentro GController.exe.
    // Instala para el usuario actual (sin administrador) en %LOCALAPPDATA%\Programs\G Controller,
    // crea accesos directos y se registra en "Aplicaciones instaladas" de Windows.
    // Con /uninstall desinstala sin dejar rastros. Con /S instala sin ventana.
    static class Setup
    {
        public const string AppName = "G Controller";
        public const string ExeName = "GController.exe";
        public const string UninstallerName = "Desinstalar.exe";
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\GController";

        public static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName); } }
        public static string Exe { get { return Path.Combine(Dir, ExeName); } }
        static string StartMenuLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk"); } }
        static string DesktopLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk"); } }

        [STAThread]
        static void Main(string[] args)
        {
            bool uninstall = Array.Exists(args, a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase));
            bool silent = Array.Exists(args, a => a.Equals("/S", StringComparison.OrdinalIgnoreCase));
            if (silent)
            {
                try { if (uninstall) Uninstall(); else Install(true); }
                catch { Environment.Exit(1); }
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm(uninstall));
        }

        // Versión del GController.exe que va adentro (su constante Program.Version).
        public static string Version()
        {
            try
            {
                var asm = Assembly.Load(Payload());
                var f = asm.GetType("GControl.Program").GetField("Version");
                return (string)f.GetRawConstantValue();
            }
            catch { return ""; }
        }

        static byte[] payload;
        static byte[] Payload()
        {
            if (payload != null) return payload;
            using (var s = typeof(Setup).Assembly.GetManifestResourceStream("app.exe"))
            using (var m = new MemoryStream())
            {
                s.CopyTo(m);
                return payload = m.ToArray();
            }
        }

        public static bool Installed { get { return File.Exists(Exe); } }

        static void CloseApp()
        {
            foreach (var p in Process.GetProcessesByName("GController"))
            {
                try { p.Kill(); p.WaitForExit(5000); } catch { }
            }
        }

        public static void Install(bool desktopIcon)
        {
            CloseApp();
            Directory.CreateDirectory(Dir);
            // Reintenta un poco por si Windows todavía tiene el .exe viejo bloqueado.
            for (int i = 0; ; i++)
            {
                try { File.WriteAllBytes(Exe, Payload()); break; }
                catch (IOException) { if (i >= 20) throw; Thread.Sleep(250); }
            }
            string self = Assembly.GetExecutingAssembly().Location;
            string uninstaller = Path.Combine(Dir, UninstallerName);
            if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(uninstaller), StringComparison.OrdinalIgnoreCase))
                File.Copy(self, uninstaller, true);

            Shortcut(StartMenuLink, Exe);
            if (desktopIcon) Shortcut(DesktopLink, Exe);
            else if (File.Exists(DesktopLink)) File.Delete(DesktopLink);

            using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", AppName);
                k.SetValue("DisplayVersion", Version());
                k.SetValue("DisplayIcon", Exe);
                k.SetValue("Publisher", AppName);
                k.SetValue("InstallLocation", Dir);
                k.SetValue("UninstallString", "\"" + uninstaller + "\" /uninstall");
                k.SetValue("QuietUninstallString", "\"" + uninstaller + "\" /uninstall /S");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)(Payload().Length / 1024) + 1, RegistryValueKind.DWord);
            }
            // Si "iniciar con Windows" apuntaba a un portable, que ahora apunte a la versión instalada.
            using (var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                if (run != null && run.GetValue("GController") != null) run.SetValue("GController", "\"" + Exe + "\"");
        }

        // Borra todo: programa, accesos, registro, configuración y el log de errores.
        public static void Uninstall()
        {
            CloseApp();
            foreach (var l in new[] { StartMenuLink, DesktopLink })
                try { if (File.Exists(l)) File.Delete(l); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\GController", false); } catch { }
            try
            {
                using (var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                    if (run != null) run.DeleteValue("GController", false);
            }
            catch { }
            try { Directory.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GController"), true); } catch { }
            try { File.Delete(Exe); } catch { }
            // El desinstalador está dentro de la carpeta: se borra la carpeta cuando este proceso termine.
            string cmd = "/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"" + Dir + "\"";
            Process.Start(new ProcessStartInfo("cmd.exe", cmd) { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Path.GetTempPath() });
        }

        static void Shortcut(string link, string target)
        {
            var t = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(t);
            try
            {
                object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { link });
                var st = sc.GetType();
                st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
                st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(target) });
                st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { target + ",0" });
                st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { AppName });
                st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
                Marshal.FinalReleaseComObject(sc);
            }
            finally { Marshal.FinalReleaseComObject(shell); }
        }
    }

    // Ventana oscura y redondeada, al estilo de la app.
    class SetupForm : Form
    {
        static readonly Color Back = Color.FromArgb(14, 14, 16), Text_ = Color.FromArgb(240, 240, 244),
            Dim = Color.FromArgb(140, 140, 148), Accent = Color.FromArgb(26, 159, 255), Danger = Color.FromArgb(230, 70, 70),
            Line = Color.FromArgb(44, 44, 50);

        readonly bool uninstall;
        readonly string version = Setup.Version();
        readonly Pill main = new Pill(), close = new Pill();
        readonly CheckRow desktop = new CheckRow { Text = "Crear acceso directo en el escritorio", Checked = true };
        readonly Label title = new Label(), sub = new Label(), status = new Label();
        enum Step { Ask, Working, Done }
        Step step = Step.Ask;

        public SetupForm(bool uninstall)
        {
            this.uninstall = uninstall;
            Text = uninstall ? "Desinstalar G Controller" : "Instalar G Controller";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(460, 290);
            BackColor = Back;
            ForeColor = Text_;
            Font = new Font("Segoe UI", 10f);
            DoubleBuffered = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            title.Text = "G Controller";
            title.Font = new Font("Segoe UI Semibold", 18f, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(104, 40);
            sub.Font = new Font("Segoe UI", 10f);
            sub.ForeColor = Dim;
            sub.AutoSize = true;
            sub.Location = new Point(106, 78);
            sub.Text = uninstall ? "Se va a quitar de tu PC sin dejar rastros."
                : (version.Length > 0 ? "Versión " + version : "") + (Setup.Installed ? "  ·  ya instalada, se actualiza" : "");

            desktop.Location = new Point(36, 136);
            desktop.Size = new Size(388, 30);
            desktop.Visible = !uninstall;

            status.ForeColor = Dim;
            status.AutoSize = false;
            status.Location = new Point(36, 176);
            status.Size = new Size(388, 40);

            main.Text = uninstall ? "Desinstalar" : "Instalar";
            main.Fill = uninstall ? Danger : Accent;
            main.Bounds = new Rectangle(292, 228, 132, 40);
            main.Click += (s, e) => Go();

            close.Text = "Cancelar";
            close.Fill = Color.FromArgb(38, 39, 43);
            close.Bounds = new Rectangle(176, 228, 108, 40);
            close.Click += (s, e) => Close();

            Controls.AddRange(new Control[] { title, sub, desktop, status, main, close });
            foreach (Control c in Controls) if (c is Label) c.BackColor = Back;
            MouseDown += Drag; title.MouseDown += Drag; sub.MouseDown += Drag;
        }

        void Go()
        {
            if (step == Step.Done)
            {
                if (!uninstall) try { Process.Start(new ProcessStartInfo(Setup.Exe) { WorkingDirectory = Setup.Dir }); } catch { }
                Close();
                return;
            }
            if (step == Step.Working) return;
            step = Step.Working;
            main.Enabled = close.Enabled = desktop.Enabled = false;
            status.ForeColor = Dim;
            status.Text = uninstall ? "Desinstalando…" : "Instalando…";
            bool icon = desktop.Checked;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string err = null;
                try { if (uninstall) Setup.Uninstall(); else Setup.Install(icon); }
                catch (UnauthorizedAccessException) { err = "Sin permiso para escribir. Cerrá G Controller y probá de nuevo."; }
                catch (Exception e) { err = e.Message; }
                BeginInvoke(new Action(() => Finish(err)));
            });
        }

        void Finish(string err)
        {
            if (err != null)
            {
                status.ForeColor = Danger;
                status.Text = "No se pudo completar: " + err;
                main.Text = "Reintentar";
                main.Enabled = close.Enabled = desktop.Enabled = true;
                step = Step.Ask;
                return;
            }
            step = Step.Done;
            status.ForeColor = Text_;
            status.Text = uninstall ? "Listo. G Controller se desinstaló." : "Listo. G Controller quedó instalado.";
            main.Text = uninstall ? "Cerrar" : "Abrir";
            main.Fill = Accent;
            main.Enabled = true;
            close.Text = "Cerrar";
            close.Enabled = true;
            close.Visible = !uninstall;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float k = g.DpiX / 96f;
            Art.DrawLogo(g, 36 * k, 36 * k, 58 * k);
            using (var p = Art.Round(new RectangleF(0.5f, 0.5f, ClientSize.Width - 1, ClientSize.Height - 1), 16))
            using (var pen = new Pen(Line)) g.DrawPath(pen, p);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            using (var p = Art.Round(new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), 16)) Region = new Region(p);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (step == Step.Working) e.Cancel = true; // no cortar a mitad de la instalación
            base.OnFormClosing(e);
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; } // sombra
        }

        void Drag(object s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
        }

        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    }

    // Botón redondeado.
    class Pill : Control
    {
        public Color Fill = Color.FromArgb(26, 159, 255);
        bool hot;

        public Pill()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.FromArgb(14, 14, 16);
            ForeColor = Color.White;
            Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            var c = !Enabled ? Color.FromArgb(60, 60, 66) : hot ? ControlPaint.Light(Fill, 0.15f) : Fill;
            using (var p = Art.Round(new RectangleF(0, 0, Width - 1, Height - 1), Height / 2f))
            using (var b = new SolidBrush(c)) g.FillPath(b, p);
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, Enabled ? ForeColor : Color.FromArgb(150, 150, 156),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    // Casilla redondeada con texto.
    class CheckRow : Control
    {
        public bool Checked;

        public CheckRow()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(14, 14, 16);
            ForeColor = Color.FromArgb(240, 240, 244);
            Font = new Font("Segoe UI", 10f);
            Cursor = Cursors.Hand;
        }

        protected override void OnClick(EventArgs e) { if (Enabled) { Checked = !Checked; Invalidate(); } base.OnClick(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            int s = 20, y = (Height - s) / 2;
            var box = new RectangleF(0.5f, y + 0.5f, s, s);
            using (var p = Art.Round(box, 6))
            {
                if (Checked) using (var b = new SolidBrush(Color.FromArgb(26, 159, 255))) g.FillPath(b, p);
                else using (var pen = new Pen(Color.FromArgb(90, 90, 98), 1.5f)) g.DrawPath(pen, p);
            }
            if (Checked)
                using (var pen = new Pen(Color.White, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(pen, new[] { new PointF(5, y + 10.5f), new PointF(9, y + 14.5f), new PointF(16, y + 6.5f) });
            TextRenderer.DrawText(g, Text, Font, new Rectangle(s + 12, 0, Width - s - 12, Height), Enabled ? ForeColor : Color.FromArgb(120, 120, 128),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }
}
