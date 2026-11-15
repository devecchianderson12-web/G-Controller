using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GControl
{
    // Pantalla de micrófono del G733: nivel, preajustes, prueba y procesado de voz.
    partial class MainForm
    {
        VoiceSettings voice = VoiceSettings.Load();
        readonly VoiceEngine engine = new VoiceEngine();
        string presetName = "Personalizado";

        // El motor corre si el procesado está activo con una salida elegida, o mientras se ve la pantalla.
        void UpdateEngine(bool pageOpen)
        {
            if (snapping) return;
            int dev = voice.Enabled ? ResolveDevice() : -1;
            bool want = dev >= 0 || pageOpen;
            engine.Dsp.Configure(voice);
            if (!want) { engine.Stop(); return; }
            if (!engine.Running || currentOut != dev) { engine.Start(dev); currentOut = dev; }
        }

        int currentOut = -2;

        // Los índices de salida cambian al conectar aparatos: se busca por nombre.
        int ResolveDevice()
        {
            if (voice.OutDevice < 0) return -1;
            var list = VoiceEngine.OutputDevices();
            int byName = list.IndexOf(voice.OutName);
            if (byName >= 0) return byName;
            return voice.OutDevice < list.Count ? voice.OutDevice : -1;
        }

        void OpenMic()
        {
            RightSplit = 300; CenterRight = true;
            device.SuspendLayout();
            foreach (Control c in device.Controls) c.Dispose();
            device.Controls.Clear();

            var back = new FlatButton { Text = "‹  G733", Width = 110, Location = new Point(24, 14) };
            back.Click += (s, e) => { UpdateEngine(false); OpenHeadset(); };
            device.Controls.Add(back);

            var controls = new List<Action>(); // refrescan la UI desde 'voice'
            var saveTimer = new System.Windows.Forms.Timer { Interval = 400 };
            saveTimer.Tick += (s, e) => { saveTimer.Stop(); voice.Save(); };
            Action changed = () =>
            {
                engine.Dsp.Configure(voice);
                saveTimer.Stop();
                saveTimer.Start();
            };

            // ----- Columna izquierda -----
            var left = new Panel { Name = "growH", Location = new Point(24, 56), Size = new Size(260, 548), BackColor = Theme.Card };
            left.Paint += (s, e) => { };
            device.Controls.Add(left);
            int lx = 16, ly = 12, lw = 228;
            left.Controls.Add(new Label { Text = "Micrófono", Font = Theme.Bold(14f), ForeColor = Theme.Text, AutoSize = true, Location = new Point(lx, ly), BackColor = Theme.Card });
            ly += 40;
            AddSmall(left, "NIVEL DE MICRÓFONO", lx, ly);
            var micVal = AddValue(left, lx + lw - 50, ly);
            ly += 20;
            var mic = new ValueSlider { Min = 0, Max = 100, Location = new Point(lx - 6, ly), Size = new Size(lw + 12, 24), BackColor = Theme.Card };
            int micNow = Audio.MicLevel();
            if (micNow >= 0) { mic.Value = micNow; micVal.Text = micNow.ToString(); }
            mic.ValueChanged += (s, e) => { if (Audio.SetMicLevel((int)mic.Value)) micVal.Text = ((int)mic.Value).ToString(); };
            left.Controls.Add(mic);
            ly += 28;
            var meter = new Meter { Location = new Point(lx, ly), Size = new Size(lw, 12), BackColor = Theme.Card };
            left.Controls.Add(meter);
            ly += 26;

            var enable = new Check { Text = "Activar procesado de voz", Location = new Point(lx, ly), Width = lw, BackColor = Theme.Card };
            enable.Changed += (s, e) => { voice.Enabled = enable.On; changed(); UpdateEngine(true); };
            controls.Add(() => enable.On = voice.Enabled);
            left.Controls.Add(enable);
            ly += 30;
            AddSmall(left, "NIVEL GENERAL DE SALIDA", lx, ly);
            var outVal = AddValue(left, lx + lw - 50, ly);
            ly += 20;
            var output = new ValueSlider { Min = -80, Max = 0, Location = new Point(lx - 6, ly), Size = new Size(lw + 12, 24), BackColor = Theme.Card };
            output.ValueChanged += (s, e) => { voice.Output = output.Value; outVal.Text = output.Value + " dB"; changed(); };
            controls.Add(() => { output.Value = voice.Output; outVal.Text = voice.Output + " dB"; });
            left.Controls.Add(output);
            ly += 34;

            AddSmall(left, "PREAJUSTES", lx, ly);
            ly += 22;
            var presets = new Panel { Location = new Point(lx, ly), Size = new Size(lw, 548 - ly - 12), BackColor = Theme.Card, AutoScroll = true, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom };
            left.Controls.Add(presets);
            Label title = null;
            Action refresh = () => { foreach (var a in controls) a(); if (title != null) title.Text = presetName; };
            Action<string, Func<VoiceSettings>> apply = (name, load) =>
            {
                voice = load();
                presetName = name;
                refresh();
                changed();
            };
            Action fillPresets = null;
            fillPresets = () =>
            {
                presets.Controls.Clear();
                int py = 0;
                presets.Controls.Add(new Label { Text = "De fábrica", Font = Theme.Bold(9.5f), ForeColor = Theme.Text, AutoSize = true, Location = new Point(0, py), BackColor = Theme.Card });
                py += 24;
                foreach (var n in VoiceSettings.BuiltInNames)
                {
                    string name = n;
                    var l = PresetLabel(name, py);
                    l.Click += (s, e) => apply(name, () => VoiceSettings.BuiltIn(name, voice));
                    presets.Controls.Add(l);
                    py += 24;
                }
                py += 8;
                presets.Controls.Add(new Label { Text = "Personalizados", Font = Theme.Bold(9.5f), ForeColor = Theme.Text, AutoSize = true, Location = new Point(0, py), BackColor = Theme.Card });
                py += 24;
                foreach (var n in VoiceSettings.CustomNames())
                {
                    string name = n;
                    var l = PresetLabel(name, py);
                    l.ForeColor = Theme.Accent;
                    l.Click += (s, e) => apply(name, () => VoiceSettings.LoadCustom(name, voice));
                    var menu = new ContextMenuStrip();
                    menu.Items.Add("Borrar", null, (s, e) => { VoiceSettings.DeleteCustom(name); fillPresets(); });
                    l.ContextMenuStrip = menu;
                    presets.Controls.Add(l);
                    py += 24;
                }
            };
            fillPresets();

            // ----- Derecha -----
            int x = 312;
            title = new Label { Text = presetName, Font = Theme.Bold(15f), ForeColor = Theme.Text, AutoSize = true, Location = new Point(x, 56), BackColor = Theme.Back };
            device.Controls.Add(title);

            // Prueba de micrófono
            int y = 96;
            Header("PRUEBA DE MICRÓFONO", x, y);
            y += 26;
            var rec = new RoundButton { Kind = RoundButton.Glyph.Record, Location = new Point(x, y), Size = new Size(32, 32) };
            var play = new RoundButton { Kind = RoundButton.Glyph.Play, Location = new Point(x + 40, y), Size = new Size(32, 32), Enabled = false };
            var time = new Label { Text = "00:00", ForeColor = Theme.Text, AutoSize = true, Location = new Point(x + 82, y + 7), BackColor = Theme.Back };
            device.Controls.AddRange(new Control[] { rec, play, time });
            y += 40;
            device.Controls.Add(new Label { Text = "Graba una muestra (hasta 10 s) para oír cómo suenas.", ForeColor = Theme.Dim, AutoSize = true, Location = new Point(x, y), BackColor = Theme.Back });
            float[] sample = null;
            DateTime recStart = DateTime.MinValue;
            var clock = new System.Windows.Forms.Timer { Interval = 50 };
            clock.Tick += (s, e) =>
            {
                meter.Level = engine.Running ? engine.Dsp.Peak : 0;
                if (engine.Recording)
                {
                    var t = DateTime.Now - recStart;
                    time.Text = string.Format("00:{0:00}", (int)t.TotalSeconds);
                    if (t.TotalSeconds >= 10) rec.PerformClick();
                }
            };
            clock.Start();
            rec.Click += (s, e) =>
            {
                if (!engine.Recording)
                {
                    UpdateEngine(true);
                    engine.BeginRecord();
                    recStart = DateTime.Now;
                    rec.Kind = RoundButton.Glyph.Stop;
                    play.Enabled = false;
                }
                else
                {
                    sample = engine.EndRecord();
                    rec.Kind = RoundButton.Glyph.Record;
                    play.Enabled = sample != null && sample.Length > 0;
                }
                rec.Invalidate(); play.Invalidate();
            };
            play.Click += (s, e) =>
            {
                if (sample == null) return;
                play.Enabled = false; rec.Enabled = false;
                VoiceEngine.Play(sample, () => BeginInvoke(new Action(() => { if (!play.IsDisposed) { play.Enabled = true; rec.Enabled = true; } })));
            };
            y += 30;

            // Salida del procesado
            Header("ENVIAR VOZ PROCESADA A", x, y);
            y += 26;
            var outBtn = new FlatButton { Size = new Size(330, 32), Location = new Point(x, y) };
            Action outText = () => outBtn.Text = voice.OutDevice < 0 ? "Elegir salida…" : voice.OutName;
            controls.Add(outText);
            outBtn.Click += (s, e) =>
            {
                var menu = new ContextMenuStrip();
                menu.Items.Add("Ninguna", null, (o, a) => { voice.OutDevice = -1; voice.OutName = ""; outText(); changed(); UpdateEngine(true); });
                var devs = VoiceEngine.OutputDevices();
                for (int i = 0; i < devs.Count; i++)
                {
                    int idx = i;
                    menu.Items.Add(devs[i], null, (o, a) => { voice.OutDevice = idx; voice.OutName = devs[idx]; outText(); changed(); UpdateEngine(true); });
                }
                menu.Show(outBtn, 0, outBtn.Height);
            };
            device.Controls.Add(outBtn);
            device.Controls.Add(new Label
            {
                Text = "Para Discord u OBS: instala un cable de audio virtual (p. ej. VB-CABLE), elige aquí «CABLE Input» y en la otra app usa «CABLE Output» como micrófono.",
                ForeColor = Theme.Dim, AutoSize = false, Size = new Size(380, 50), Location = new Point(x, y + 38), BackColor = Theme.Back, Font = Theme.Ui(8.5f),
            });

            // Ecualización de voz (arriba a la derecha)
            int ex = x + 400, ey = 96;
            var eqOn = new Check { Text = "Ecualización de voz", Location = new Point(ex, ey), Width = 260 };
            eqOn.Changed += (s, e) => { voice.EqOn = eqOn.On; changed(); };
            controls.Add(() => eqOn.On = voice.EqOn);
            device.Controls.Add(eqOn);
            string[] bands = { "BAJO", "MEDIO", "ALTO" };
            float[] fmin = { 20, 200, 1000 }, fmax = { 1000, 5000, 16000 };
            for (int b = 0; b < 3; b++)
            {
                int band = b;
                int bx = ex + 40 + b * 90;
                var freq = new TextBox
                {
                    Location = new Point(bx - 8, ey + 28), Width = 56, TextAlign = HorizontalAlignment.Center,
                    BackColor = Theme.Card, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Font = Theme.Ui(10f),
                };
                EventHandler commit = (s, e) =>
                {
                    float f;
                    if (float.TryParse(freq.Text.Replace("Hz", "").Trim(), out f))
                    {
                        voice.EqFreq[band] = Math.Max(fmin[band], Math.Min(fmax[band], f));
                        changed();
                    }
                    freq.Text = ((int)voice.EqFreq[band]).ToString();
                };
                freq.Leave += commit;
                freq.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { commit(s, e); e.SuppressKeyPress = true; } };
                controls.Add(() => freq.Text = ((int)voice.EqFreq[band]).ToString());
                var gain = new ValueSlider { Vertical = true, Min = -15, Max = 15, Step = 0.5f, Location = new Point(bx, ey + 52), Size = new Size(40, 110) };
                var gainVal = new Label { ForeColor = Theme.Dim, AutoSize = false, Size = new Size(56, 18), Location = new Point(bx - 8, ey + 164), TextAlign = ContentAlignment.MiddleCenter, BackColor = Theme.Back };
                gain.ValueChanged += (s, e) => { voice.EqGain[band] = gain.Value; gainVal.Text = (gain.Value > 0 ? "+" : "") + gain.Value + " dB"; changed(); };
                controls.Add(() => { gain.Value = voice.EqGain[band]; gainVal.Text = (voice.EqGain[band] > 0 ? "+" : "") + voice.EqGain[band] + " dB"; });
                device.Controls.Add(freq);
                device.Controls.Add(gain);
                device.Controls.Add(gainVal);
                device.Controls.Add(new Label { Text = bands[b], ForeColor = Theme.Text, AutoSize = false, Size = new Size(70, 18), Location = new Point(bx - 15, ey + 184), TextAlign = ContentAlignment.MiddleCenter, BackColor = Theme.Back, Font = Theme.Bold(9f) });
            }
            foreach (var mark in new[] { Tuple.Create("+15", 60), Tuple.Create("0", 107), Tuple.Create("-15", 154) })
                device.Controls.Add(new Label { Text = mark.Item1, ForeColor = Theme.Dim, AutoSize = false, Size = new Size(30, 14), TextAlign = ContentAlignment.MiddleRight, Location = new Point(ex - 4, ey + mark.Item2 - 7), BackColor = Theme.Back, Font = Theme.Ui(8f) });

            // Filas de efectos: dos columnas, como en G HUB.
            int ry = 330, colW = 330;
            Func<int, int, string, float, float, float, string, Func<bool>, Action<bool>, Func<float>, Action<float>, Action> row = null;
            row = (cx, cy, name, min, max, step, unit, getOn, setOn, get, set) =>
            {
                Check chk = null;
                if (getOn != null)
                {
                    chk = new Check { Text = name, Location = new Point(cx, cy), Width = colW - 70 };
                    chk.Changed += (s, e) => { setOn(chk.On); changed(); };
                    device.Controls.Add(chk);
                }
                else device.Controls.Add(new Label { Text = name.ToUpperInvariant(), Font = Theme.Bold(9f), ForeColor = Theme.Text, AutoSize = true, Location = new Point(cx, cy + 3), BackColor = Theme.Back });
                var val = new Label { ForeColor = Theme.Text, Font = Theme.Bold(9.5f), AutoSize = false, Size = new Size(70, 20), Location = new Point(cx + colW - 70, cy + 22), TextAlign = ContentAlignment.MiddleRight, BackColor = Theme.Back };
                var sl = new ValueSlider { Min = min, Max = max, Step = step, Location = new Point(cx - 6, cy + 20), Size = new Size(colW - 70, 24) };
                Func<float, string> fmt = v => unit == ":1" ? v + " :1" : v + " " + unit;
                sl.ValueChanged += (s, e) => { set(sl.Value); val.Text = fmt(sl.Value); changed(); };
                device.Controls.Add(sl);
                device.Controls.Add(val);
                device.Controls.Add(new Label { Text = fmt(min), ForeColor = Theme.Dim, Font = Theme.Ui(7.5f), AutoSize = true, Location = new Point(cx, cy + 44), BackColor = Theme.Back });
                var maxL = new Label { Text = fmt(max), ForeColor = Theme.Dim, Font = Theme.Ui(7.5f), AutoSize = false, Size = new Size(80, 14), Location = new Point(cx + colW - 70 - 80 - 6, cy + 44), TextAlign = ContentAlignment.TopRight, BackColor = Theme.Back };
                device.Controls.Add(maxL);
                return () => { if (chk != null) chk.On = getOn(); sl.Value = get(); val.Text = fmt(sl.Value); };
            };
            int c1 = x, c2 = x + 380;
            controls.Add(row(c1, ry, "Eliminador de ruido", 20, 400, 1, "Hz", () => voice.HpOn, v => voice.HpOn = v, () => voice.HpHz, v => voice.HpHz = v));
            controls.Add(row(c1, ry + 70, "Reducción de ruido", -60, 0, 1, "dB", () => voice.NrOn, v => voice.NrOn = v, () => voice.NrDb, v => voice.NrDb = v));
            controls.Add(row(c1, ry + 140, "Expansor/Bloqueador", -72, -24, 1, "dB", () => voice.GateOn, v => voice.GateOn = v, () => voice.GateDb, v => voice.GateDb = v));
            controls.Add(row(c2, ry, "Eliminación de siseo", 1, 20, 1, ":1", () => voice.DeessOn, v => voice.DeessOn = v, () => voice.DeessRatio, v => voice.DeessRatio = v));
            controls.Add(row(c2, ry + 70, "Compresor", -48, 0, 1, "dB", () => voice.CompOn, v => voice.CompOn = v, () => voice.CompDb, v => voice.CompDb = v));
            controls.Add(row(c2, ry + 140, "Limitador", -20, 20, 1, "dB", null, null, () => voice.LimitDb, v => voice.LimitDb = v));

            // Proporción del compresor (menú •••).
            var ratio = new FlatButton { Text = "•••", Size = new Size(36, 22), Location = new Point(c2 + colW - 36, ry + 70) };
            ratio.Click += (s, e) =>
            {
                var menu = new ContextMenuStrip();
                foreach (var r in new[] { 2f, 3f, 4f, 6f, 8f, 10f })
                {
                    float rv = r;
                    var item = new ToolStripMenuItem("Proporción " + r + ":1") { Checked = voice.CompRatio == r };
                    item.Click += (o, a) => { voice.CompRatio = rv; changed(); };
                    menu.Items.Add(item);
                }
                menu.Show(ratio, 0, ratio.Height);
            };
            device.Controls.Add(ratio);

            // Restablecer y guardar como preajuste.
            var reset = new FlatButton { Text = "RESTABLECER", Size = new Size(170, 34), Location = new Point(x + 160, 566) };
            var save = new FlatButton { Text = "GUARDAR", Filled = true, Size = new Size(170, 34), Location = new Point(x + 350, 566) };
            reset.Click += (s, e) =>
            {
                var d = new VoiceSettings { Enabled = voice.Enabled, OutDevice = voice.OutDevice, OutName = voice.OutName };
                apply("Personalizado", () => d);
            };
            save.Click += (s, e) =>
            {
                string name = Prompt.Ask(this, "Guardar preajuste", "Nombre del preajuste:", presetName == "Personalizado" ? "" : presetName);
                if (string.IsNullOrWhiteSpace(name)) return;
                name = name.Trim().Replace(@"\", "-");
                voice.SaveCustom(name);
                presetName = name;
                refresh();
                fillPresets();
            };
            device.Controls.Add(reset);
            device.Controls.Add(save);

            device.Disposed += (s, e) => { clock.Dispose(); saveTimer.Dispose(); };
            refresh();
            UpdateEngine(true);
            device.ResumeLayout();
            ShowPage(device);
        }

        static Label AddSmall(Control parent, string text, int x, int y)
        {
            var l = new Label { Text = text, ForeColor = Theme.Dim, Font = Theme.Bold(8f), AutoSize = true, Location = new Point(x, y), BackColor = parent.BackColor };
            parent.Controls.Add(l);
            return l;
        }

        static Label AddValue(Control parent, int x, int y)
        {
            var l = new Label { Text = "—", ForeColor = Theme.Text, Font = Theme.Bold(9f), AutoSize = false, Size = new Size(50, 16), Location = new Point(x, y - 1), TextAlign = ContentAlignment.MiddleRight, BackColor = parent.BackColor };
            parent.Controls.Add(l);
            return l;
        }

        static Label PresetLabel(string text, int y)
        {
            return new Label { Text = text, ForeColor = Theme.Dim, AutoSize = true, Location = new Point(0, y), Cursor = Cursors.Hand, BackColor = Theme.Card };
        }
    }

    // Botón redondo de grabar / parar / reproducir.
    class RoundButton : Button
    {
        public enum Glyph { Record, Stop, Play }
        public Glyph Kind;

        public RoundButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            FlatStyle = FlatStyle.Flat;
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Back);
            var c = Enabled ? Color.FromArgb(200, 200, 206) : Theme.Track;
            using (var pen = new Pen(c, 1.6f)) g.DrawEllipse(pen, 1, 1, Width - 3, Height - 3);
            float cx = Width / 2f, cy = Height / 2f;
            if (Kind == Glyph.Record)
                using (var b = new SolidBrush(Enabled ? Color.FromArgb(240, 60, 70) : Theme.Track)) g.FillEllipse(b, cx - 7, cy - 7, 14, 14);
            else if (Kind == Glyph.Stop)
                using (var b = new SolidBrush(Color.FromArgb(240, 60, 70))) g.FillRectangle(b, cx - 6, cy - 6, 12, 12);
            else
                using (var b = new SolidBrush(c)) g.FillPolygon(b, new[] { new PointF(cx - 4, cy - 7), new PointF(cx + 7, cy), new PointF(cx - 4, cy + 7) });
        }
    }

    // Ventanita para pedir un texto.
    static class Prompt
    {
        public static string Ask(IWin32Window owner, string title, string label, string value)
        {
            using (var f = new Form { Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, ClientSize = new Size(320, 120), BackColor = Theme.Card, ForeColor = Theme.Text, Font = Theme.Ui(10f) })
            {
                var l = new Label { Text = label, AutoSize = true, Location = new Point(14, 14) };
                var t = new TextBox { Text = value, Location = new Point(14, 40), Width = 292, BackColor = Theme.Back, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
                var ok = new Button { Text = "Guardar", DialogResult = DialogResult.OK, Location = new Point(146, 78), Width = 76, FlatStyle = FlatStyle.Flat };
                var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Location = new Point(230, 78), Width = 76, FlatStyle = FlatStyle.Flat };
                f.Controls.AddRange(new Control[] { l, t, ok, cancel });
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                return f.ShowDialog(owner) == DialogResult.OK ? t.Text : null;
            }
        }
    }
}
