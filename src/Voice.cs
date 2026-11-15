using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace GControl
{
    // Ajustes del procesado de voz. Mismos controles que la pantalla de micrófono de G HUB.
    class VoiceSettings
    {
        public bool Enabled;
        public float Output = 0;                       // nivel general de salida, -80..0 dB
        public bool EqOn = true;
        public float[] EqFreq = { 160, 900, 4550 };    // bajo (estante), medio (pico), alto (estante)
        public float[] EqGain = { 0, 0, 0 };           // -15..+15 dB
        public bool HpOn = true; public float HpHz = 80;          // eliminador de ruido, 20..400 Hz
        public bool NrOn = false; public float NrDb = 0;          // reducción de ruido, -60..0 dB
        public bool GateOn = true; public float GateDb = -50;     // expansor/bloqueador, -72..-24 dB
        public bool DeessOn = false; public float DeessRatio = 1; // eliminación de siseo, 1..20 :1
        public bool CompOn = true; public float CompDb = -20;     // compresor, -48..0 dB
        public float CompRatio = 4;
        public float LimitDb = 0;                                 // limitador, -20..20 dB
        public int OutDevice = -1;                                // salida waveOut (-1 = ninguna)
        public string OutName = "";

        public VoiceSettings Clone() { return Parse(Serialize()); }

        public string Serialize()
        {
            var c = CultureInfo.InvariantCulture;
            return string.Join(";", new[]
            {
                "on=" + (Enabled ? 1 : 0), "out=" + Output.ToString(c),
                "eq=" + (EqOn ? 1 : 0), "f0=" + EqFreq[0].ToString(c), "f1=" + EqFreq[1].ToString(c), "f2=" + EqFreq[2].ToString(c),
                "g0=" + EqGain[0].ToString(c), "g1=" + EqGain[1].ToString(c), "g2=" + EqGain[2].ToString(c),
                "hp=" + (HpOn ? 1 : 0), "hpz=" + HpHz.ToString(c),
                "nr=" + (NrOn ? 1 : 0), "nrd=" + NrDb.ToString(c),
                "gt=" + (GateOn ? 1 : 0), "gtd=" + GateDb.ToString(c),
                "ds=" + (DeessOn ? 1 : 0), "dsr=" + DeessRatio.ToString(c),
                "cp=" + (CompOn ? 1 : 0), "cpd=" + CompDb.ToString(c), "cpr=" + CompRatio.ToString(c),
                "lm=" + LimitDb.ToString(c), "dev=" + OutDevice, "devn=" + OutName.Replace(";", ",").Replace("=", "-"),
            });
        }

        public static VoiceSettings Parse(string text)
        {
            var v = new VoiceSettings();
            if (string.IsNullOrEmpty(text)) return v;
            var c = CultureInfo.InvariantCulture;
            foreach (var part in text.Split(';'))
            {
                int i = part.IndexOf('=');
                if (i < 0) continue;
                string k = part.Substring(0, i), s = part.Substring(i + 1);
                float f;
                float.TryParse(s, NumberStyles.Float, c, out f);
                bool b = s == "1";
                switch (k)
                {
                    case "on": v.Enabled = b; break;
                    case "out": v.Output = f; break;
                    case "eq": v.EqOn = b; break;
                    case "f0": v.EqFreq[0] = f; break;
                    case "f1": v.EqFreq[1] = f; break;
                    case "f2": v.EqFreq[2] = f; break;
                    case "g0": v.EqGain[0] = f; break;
                    case "g1": v.EqGain[1] = f; break;
                    case "g2": v.EqGain[2] = f; break;
                    case "hp": v.HpOn = b; break;
                    case "hpz": v.HpHz = f; break;
                    case "nr": v.NrOn = b; break;
                    case "nrd": v.NrDb = f; break;
                    case "gt": v.GateOn = b; break;
                    case "gtd": v.GateDb = f; break;
                    case "ds": v.DeessOn = b; break;
                    case "dsr": v.DeessRatio = f; break;
                    case "cp": v.CompOn = b; break;
                    case "cpd": v.CompDb = f; break;
                    case "cpr": v.CompRatio = f; break;
                    case "lm": v.LimitDb = f; break;
                    case "dev": v.OutDevice = (int)f; break;
                    case "devn": v.OutName = s; break;
                }
            }
            return v;
        }

        // Preajustes de fábrica (valores propios, pensados para voz hablada).
        public static readonly string[] BuiltInNames =
        {
            "Radio AM", "Broadcaster 1", "Broadcaster 2", "Emisora FM",
            "Voz alta - Fuerte", "Voz alta - Suave", "Voz baja - Fuerte", "Voz baja - Suave",
        };

        public static VoiceSettings BuiltIn(string name, VoiceSettings current)
        {
            var v = current.Clone();
            v.EqOn = v.HpOn = v.GateOn = v.CompOn = true;
            v.NrOn = v.DeessOn = false;
            v.EqFreq = new float[] { 160, 900, 4550 };
            switch (name)
            {
                case "Radio AM": v.HpHz = 300; Eq(v, -15, 8, -12); v.GateDb = -50; v.CompDb = -30; v.LimitDb = 6; break;
                case "Broadcaster 1": v.HpHz = 80; Eq(v, 4, -2, 3); v.GateDb = -52; v.CompDb = -24; v.LimitDb = 4; v.DeessOn = true; v.DeessRatio = 6; break;
                case "Broadcaster 2": v.HpHz = 100; Eq(v, 6, -3, 5); v.GateDb = -50; v.CompDb = -28; v.LimitDb = 6; v.DeessOn = true; v.DeessRatio = 8; break;
                case "Emisora FM": v.HpHz = 70; Eq(v, 5, -1, 4); v.GateDb = -54; v.CompDb = -30; v.LimitDb = 6; v.DeessOn = true; v.DeessRatio = 5; break;
                case "Voz alta - Fuerte": v.HpHz = 140; Eq(v, -4, 2, 4); v.GateDb = -45; v.CompDb = -30; v.LimitDb = 8; break;
                case "Voz alta - Suave": v.HpHz = 120; Eq(v, -2, 1, 2); v.GateDb = -55; v.CompDb = -18; v.LimitDb = 2; break;
                case "Voz baja - Fuerte": v.HpHz = 60; Eq(v, 6, -2, 3); v.GateDb = -45; v.CompDb = -30; v.LimitDb = 8; break;
                case "Voz baja - Suave": v.HpHz = 50; Eq(v, 3, -1, 1); v.GateDb = -55; v.CompDb = -18; v.LimitDb = 2; break;
            }
            return v;
        }

        static void Eq(VoiceSettings v, float lo, float mid, float hi) { v.EqGain = new[] { lo, mid, hi }; }

        // Guardado: registro de Windows.
        const string Key = @"Software\GController\Voice";

        public static VoiceSettings Load()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(Key))
                return Parse(k == null ? null : k.GetValue("Current") as string);
        }

        public void Save() { Write("Current", Serialize()); }

        public static List<string> CustomNames()
        {
            var list = new List<string>();
            using (var k = Registry.CurrentUser.OpenSubKey(Key + @"\Presets"))
                if (k != null) list.AddRange(k.GetValueNames());
            list.Sort(StringComparer.CurrentCultureIgnoreCase);
            return list;
        }

        public static VoiceSettings LoadCustom(string name, VoiceSettings current)
        {
            using (var k = Registry.CurrentUser.OpenSubKey(Key + @"\Presets"))
            {
                var v = Parse(k == null ? null : k.GetValue(name) as string);
                v.Enabled = current.Enabled; v.OutDevice = current.OutDevice; v.OutName = current.OutName;
                return v;
            }
        }

        public void SaveCustom(string name) { Write(@"Presets\" + name, Serialize(), true); }

        public static void DeleteCustom(string name)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(Key + @"\Presets")) k.DeleteValue(name, false);
            }
            catch { }
        }

        static void Write(string name, string value, bool preset = false)
        {
            try
            {
                string sub = Key, val = name;
                if (preset) { sub = Key + @"\Presets"; val = name.Substring(8); }
                using (var k = Registry.CurrentUser.CreateSubKey(sub)) k.SetValue(val, value);
            }
            catch { }
        }
    }

    // Filtro bicuadrático (fórmulas RBJ).
    class Biquad
    {
        double b0, b1, b2, a1, a2, z1, z2;

        public void Pass(double v) { b0 = v; b1 = b2 = a1 = a2 = 0; }

        public void HighPass(double fs, double f, double q)
        {
            double w = 2 * Math.PI * f / fs, c = Math.Cos(w), al = Math.Sin(w) / (2 * q), a0 = 1 + al;
            Set((1 + c) / 2 / a0, -(1 + c) / a0, (1 + c) / 2 / a0, -2 * c / a0, (1 - al) / a0);
        }

        public void BandPass(double fs, double f, double q)
        {
            double w = 2 * Math.PI * f / fs, al = Math.Sin(w) / (2 * q), a0 = 1 + al;
            Set(al / a0, 0, -al / a0, -2 * Math.Cos(w) / a0, (1 - al) / a0);
        }

        public void Peak(double fs, double f, double q, double db)
        {
            double A = Math.Pow(10, db / 40), w = 2 * Math.PI * f / fs, al = Math.Sin(w) / (2 * q), c = Math.Cos(w), a0 = 1 + al / A;
            Set((1 + al * A) / a0, -2 * c / a0, (1 - al * A) / a0, -2 * c / a0, (1 - al / A) / a0);
        }

        public void Shelf(double fs, double f, double db, bool high)
        {
            double A = Math.Pow(10, db / 40), w = 2 * Math.PI * f / fs, c = Math.Cos(w), s = Math.Sin(w);
            double al = s / 2 * Math.Sqrt(2), sq = 2 * Math.Sqrt(A) * al;
            if (high)
            {
                double a0 = (A + 1) - (A - 1) * c + sq;
                Set(A * ((A + 1) + (A - 1) * c + sq) / a0, -2 * A * ((A - 1) + (A + 1) * c) / a0, A * ((A + 1) + (A - 1) * c - sq) / a0,
                    2 * ((A - 1) - (A + 1) * c) / a0, ((A + 1) - (A - 1) * c - sq) / a0);
            }
            else
            {
                double a0 = (A + 1) + (A - 1) * c + sq;
                Set(A * ((A + 1) - (A - 1) * c + sq) / a0, 2 * A * ((A - 1) - (A + 1) * c) / a0, A * ((A + 1) - (A - 1) * c - sq) / a0,
                    -2 * ((A - 1) + (A + 1) * c) / a0, ((A + 1) + (A - 1) * c - sq) / a0);
            }
        }

        void Set(double nb0, double nb1, double nb2, double na1, double na2) { b0 = nb0; b1 = nb1; b2 = nb2; a1 = na1; a2 = na2; }

        public double Run(double x)
        {
            double y = b0 * x + z1;
            z1 = b1 * x - a1 * y + z2;
            z2 = b2 * x - a2 * y;
            return y;
        }
    }

    // Cadena de procesado: paso alto, reducción de ruido, expansor, ecualizador,
    // eliminación de siseo, compresor, limitador y nivel de salida.
    class VoiceDsp
    {
        public const int Rate = 48000;
        readonly Biquad hp = new Biquad(), lo = new Biquad(), mid = new Biquad(), hi = new Biquad(), sib = new Biquad();
        VoiceSettings s = new VoiceSettings();
        double nrEnv, floorEnv = 1e-4, nrGain = 1, gateEnv, gateGain = 1, sibEnv, compEnv, compGain = 1, limGain = 1;
        public float Peak; // pico de salida del último bloque (0..1)

        static double Coef(double ms) { return Math.Exp(-1.0 / (Rate * ms / 1000.0)); }
        static double Db(double lin) { return 20 * Math.Log10(Math.Max(lin, 1e-9)); }
        static double Lin(double db) { return Math.Pow(10, db / 20); }

        public void Configure(VoiceSettings v)
        {
            s = v.Clone();
            if (s.HpOn) hp.HighPass(Rate, Clamp(s.HpHz, 20, 400), 0.707); else hp.Pass(1);
            if (s.EqOn)
            {
                lo.Shelf(Rate, Clamp(s.EqFreq[0], 20, 1000), Clamp(s.EqGain[0], -15, 15), false);
                mid.Peak(Rate, Clamp(s.EqFreq[1], 200, 5000), 1.0, Clamp(s.EqGain[1], -15, 15));
                hi.Shelf(Rate, Clamp(s.EqFreq[2], 1000, 16000), Clamp(s.EqGain[2], -15, 15), true);
            }
            else { lo.Pass(1); mid.Pass(1); hi.Pass(1); }
            sib.BandPass(Rate, 6500, 1.4);
        }

        static float Clamp(float v, float a, float b) { return Math.Max(a, Math.Min(b, v)); }

        public void Process(float[] buf, int n)
        {
            double aFast = Coef(5), rFast = Coef(60), aGate = Coef(1), rGate = Coef(120), aGain = Coef(2), rGain = Coef(150);
            double aComp = Coef(6), rComp = Coef(90), rLim = Coef(60), floorUp = Coef(3000);
            double gateFloor = Lin(-60), nrMax = Lin(Math.Min(0, s.NrDb)), outGain = Lin(Clamp(s.Output, -80, 0)), limIn = Lin(Clamp(s.LimitDb, -20, 20));
            double ceiling = Lin(-1);
            float peak = 0;
            for (int i = 0; i < n; i++)
            {
                double x = hp.Run(buf[i]);
                double ax = Math.Abs(x);

                if (s.NrOn && s.NrDb < 0)
                {
                    // Suelo de ruido: baja rápido, sube despacio. Cerca del suelo se atenúa.
                    nrEnv = ax > nrEnv ? aFast * nrEnv + (1 - aFast) * ax : rFast * nrEnv + (1 - rFast) * ax;
                    floorEnv = nrEnv < floorEnv ? nrEnv : floorUp * floorEnv + (1 - floorUp) * nrEnv;
                    double target = nrEnv < floorEnv * 3 ? nrMax : 1;
                    nrGain = target < nrGain ? aGain * nrGain + (1 - aGain) * target : rGain * nrGain + (1 - rGain) * target;
                    x *= nrGain;
                }

                if (s.GateOn)
                {
                    gateEnv = ax > gateEnv ? aGate * gateEnv + (1 - aGate) * ax : rGate * gateEnv + (1 - rGate) * ax;
                    double under = Db(gateEnv) - s.GateDb;
                    double target = under >= 0 ? 1 : Math.Max(gateFloor, Lin(under * 3)); // expansor 1:4
                    gateGain = target < gateGain ? rGain * gateGain + (1 - rGain) * target : aGain * gateGain + (1 - aGain) * target;
                    x *= gateGain;
                }

                x = hi.Run(mid.Run(lo.Run(x)));

                if (s.DeessOn && s.DeessRatio > 1)
                {
                    double sb = Math.Abs(sib.Run(x));
                    sibEnv = sb > sibEnv ? aFast * sibEnv + (1 - aFast) * sb : rFast * sibEnv + (1 - rFast) * sb;
                    double over = Db(sibEnv) + 30; // umbral -30 dB en la banda de las eses
                    if (over > 0) x *= Lin(-over * (1 - 1 / s.DeessRatio));
                }

                if (s.CompOn)
                {
                    double ac = Math.Abs(x);
                    compEnv = ac > compEnv ? aComp * compEnv + (1 - aComp) * ac : rComp * compEnv + (1 - rComp) * ac;
                    double over = Db(compEnv) - s.CompDb;
                    double target = over > 0 ? Lin(-over * (1 - 1 / Math.Max(1, s.CompRatio))) : 1;
                    compGain = target < compGain ? aGain * compGain + (1 - aGain) * target : rGain * compGain + (1 - rGain) * target;
                    x *= compGain;
                }

                // Limitador: ganancia de entrada y techo a -1 dBFS.
                x *= limIn;
                double al = Math.Abs(x) * limGain;
                if (al > ceiling) limGain = ceiling / Math.Abs(x);
                else limGain = rLim * limGain + (1 - rLim) * 1.0;
                x *= limGain * outGain;

                float y = (float)Math.Max(-1, Math.Min(1, x));
                buf[i] = y;
                if (Math.Abs(y) > peak) peak = Math.Abs(y);
            }
            Peak = peak;
        }
    }

    // Captura del micrófono, procesado y salida a otro dispositivo (p. ej. un cable de audio virtual).
    class VoiceEngine : IDisposable
    {
        const int Block = 480;      // 10 ms
        const int Buffers = 6;
        public readonly VoiceDsp Dsp = new VoiceDsp();
        Thread thread;
        volatile bool running;
        readonly object gate = new object();
        List<float> recording;      // prueba de micrófono
        public string Error;

        public bool Running { get { return running; } }
        public bool Recording { get { lock (gate) return recording != null; } }

        public void Start(int outDevice)
        {
            Stop();
            Error = null;
            running = true;
            thread = new Thread(() => Loop(outDevice)) { IsBackground = true, Priority = ThreadPriority.AboveNormal };
            thread.Start();
        }

        public void Stop()
        {
            running = false;
            if (thread != null) { thread.Join(1000); thread = null; }
        }

        public void Dispose() { Stop(); }

        public void BeginRecord() { lock (gate) recording = new List<float>(VoiceDsp.Rate * 5); }

        public float[] EndRecord()
        {
            lock (gate)
            {
                var r = recording == null ? null : recording.ToArray();
                recording = null;
                return r;
            }
        }

        void Loop(int outDevice)
        {
            var fmt = Wave.Format(VoiceDsp.Rate);
            IntPtr hin, hout = IntPtr.Zero;
            if (Wave.waveInOpen(out hin, -1, ref fmt, IntPtr.Zero, IntPtr.Zero, 0) != 0) { Error = "No se pudo abrir el micrófono."; running = false; return; }
            if (outDevice >= 0 && Wave.waveOutOpen(out hout, outDevice, ref fmt, IntPtr.Zero, IntPtr.Zero, 0) != 0) hout = IntPtr.Zero;
            var ins = new Wave.Buffer[Buffers];
            var outs = new Wave.Buffer[Buffers * 2];
            for (int i = 0; i < ins.Length; i++) { ins[i] = new Wave.Buffer(Block * 2); ins[i].PrepareIn(hin); Wave.waveInAddBuffer(hin, ins[i].Header, Wave.HdrSize); }
            if (hout != IntPtr.Zero) for (int i = 0; i < outs.Length; i++) { outs[i] = new Wave.Buffer(Block * 2); outs[i].PrepareOut(hout); }
            Wave.waveInStart(hin);
            var samples = new float[Block];
            var pcm = new short[Block];
            int next = 0;
            try
            {
                while (running)
                {
                    bool any = false;
                    for (int k = 0; k < ins.Length; k++)
                    {
                        var b = ins[next];
                        if (!b.Done) break;
                        any = true;
                        int n = b.Recorded / 2;
                        Marshal.Copy(b.Data, pcm, 0, n);
                        for (int i = 0; i < n; i++) samples[i] = pcm[i] / 32768f;
                        Dsp.Process(samples, n);
                        lock (gate)
                            if (recording != null && recording.Count < VoiceDsp.Rate * 10)
                                for (int i = 0; i < n; i++) recording.Add(samples[i]);
                        if (hout != IntPtr.Zero)
                        {
                            var o = Array.Find(outs, x => x.Free);
                            if (o != null)
                            {
                                for (int i = 0; i < n; i++) pcm[i] = (short)(samples[i] * 32767);
                                Marshal.Copy(pcm, 0, o.Data, n);
                                o.Write(hout, n * 2);
                            }
                        }
                        b.Requeue(hin);
                        next = (next + 1) % ins.Length;
                    }
                    if (!any) Thread.Sleep(2);
                }
            }
            finally
            {
                Wave.waveInStop(hin);
                Wave.waveInReset(hin);
                foreach (var b in ins) b.UnprepareIn(hin);
                Wave.waveInClose(hin);
                if (hout != IntPtr.Zero)
                {
                    Wave.waveOutReset(hout);
                    foreach (var b in outs) b.UnprepareOut(hout);
                    Wave.waveOutClose(hout);
                }
                foreach (var b in ins) b.Free_();
                foreach (var b in outs) if (b != null) b.Free_();
            }
        }

        // Reproduce una grabación por la salida de audio por defecto.
        public static void Play(float[] audio, Action done)
        {
            new Thread(() =>
            {
                var fmt = Wave.Format(VoiceDsp.Rate);
                IntPtr h;
                if (audio == null || audio.Length == 0 || Wave.waveOutOpen(out h, -1, ref fmt, IntPtr.Zero, IntPtr.Zero, 0) != 0) { done(); return; }
                var b = new Wave.Buffer(audio.Length * 2);
                var pcm = new short[audio.Length];
                for (int i = 0; i < audio.Length; i++) pcm[i] = (short)(Math.Max(-1, Math.Min(1, audio[i])) * 32767);
                Marshal.Copy(pcm, 0, b.Data, pcm.Length);
                b.PrepareOut(h);
                b.Write(h, pcm.Length * 2);
                while (!b.Done) Thread.Sleep(20);
                b.UnprepareOut(h);
                Wave.waveOutClose(h);
                b.Free_();
                done();
            }) { IsBackground = true }.Start();
        }

        public static List<string> OutputDevices()
        {
            var list = new List<string>();
            int n = Wave.waveOutGetNumDevs();
            for (int i = 0; i < n; i++)
            {
                var caps = new Wave.WAVEOUTCAPS();
                list.Add(Wave.waveOutGetDevCaps(i, ref caps, Marshal.SizeOf(caps)) == 0 ? caps.szPname : "Salida " + i);
            }
            return list;
        }
    }

    static class Wave
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct WAVEFORMATEX { public ushort wFormatTag, nChannels; public uint nSamplesPerSec, nAvgBytesPerSec; public ushort nBlockAlign, wBitsPerSample, cbSize; }

        [StructLayout(LayoutKind.Sequential)]
        struct WAVEHDR { public IntPtr lpData; public uint dwBufferLength, dwBytesRecorded; public IntPtr dwUser; public uint dwFlags, dwLoops; public IntPtr lpNext, reserved; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct WAVEOUTCAPS
        {
            public ushort wMid, wPid; public uint vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
            public uint dwFormats; public ushort wChannels, wReserved1; public uint dwSupport;
        }

        public static readonly int HdrSize = Marshal.SizeOf(typeof(WAVEHDR));
        static readonly int FlagsAt = (int)Marshal.OffsetOf(typeof(WAVEHDR), "dwFlags");
        static readonly int RecordedAt = (int)Marshal.OffsetOf(typeof(WAVEHDR), "dwBytesRecorded");
        static readonly int LengthAt = (int)Marshal.OffsetOf(typeof(WAVEHDR), "dwBufferLength");
        const int WHDR_DONE = 1, WHDR_INQUEUE = 0x10;

        public static WAVEFORMATEX Format(int rate)
        {
            return new WAVEFORMATEX { wFormatTag = 1, nChannels = 1, nSamplesPerSec = (uint)rate, nAvgBytesPerSec = (uint)rate * 2, nBlockAlign = 2, wBitsPerSample = 16 };
        }

        // Búfer de audio con su cabecera en memoria fija.
        public class Buffer
        {
            public readonly IntPtr Data, Header;
            readonly int size;
            bool written;

            public Buffer(int bytes)
            {
                size = bytes;
                Data = Marshal.AllocHGlobal(bytes);
                Header = Marshal.AllocHGlobal(HdrSize);
                Marshal.StructureToPtr(new WAVEHDR { lpData = Data, dwBufferLength = (uint)bytes }, Header, false);
            }

            int Flags { get { return Marshal.ReadInt32(Header, FlagsAt); } }
            public bool Done { get { return (Flags & WHDR_DONE) != 0; } }
            public bool Free { get { return !written || (Flags & WHDR_INQUEUE) == 0; } }
            public int Recorded { get { return Marshal.ReadInt32(Header, RecordedAt); } }

            public void PrepareIn(IntPtr h) { waveInPrepareHeader(h, Header, HdrSize); }
            public void PrepareOut(IntPtr h) { waveOutPrepareHeader(h, Header, HdrSize); }
            public void UnprepareIn(IntPtr h) { waveInUnprepareHeader(h, Header, HdrSize); }
            public void UnprepareOut(IntPtr h) { waveOutUnprepareHeader(h, Header, HdrSize); }

            public void Requeue(IntPtr h)
            {
                Marshal.WriteInt32(Header, FlagsAt, Flags & ~WHDR_DONE);
                waveInAddBuffer(h, Header, HdrSize);
            }

            public void Write(IntPtr h, int bytes)
            {
                Marshal.WriteInt32(Header, LengthAt, Math.Min(bytes, size));
                Marshal.WriteInt32(Header, FlagsAt, Flags & ~WHDR_DONE);
                written = true;
                waveOutWrite(h, Header, HdrSize);
            }

            public void Free_() { Marshal.FreeHGlobal(Data); Marshal.FreeHGlobal(Header); }
        }

        [DllImport("winmm.dll")] public static extern int waveInOpen(out IntPtr h, int dev, ref WAVEFORMATEX f, IntPtr cb, IntPtr inst, int flags);
        [DllImport("winmm.dll")] public static extern int waveInPrepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] public static extern int waveInUnprepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] public static extern int waveInAddBuffer(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] public static extern int waveInStart(IntPtr h);
        [DllImport("winmm.dll")] public static extern int waveInStop(IntPtr h);
        [DllImport("winmm.dll")] public static extern int waveInReset(IntPtr h);
        [DllImport("winmm.dll")] public static extern int waveInClose(IntPtr h);
        [DllImport("winmm.dll")] public static extern int waveOutOpen(out IntPtr h, int dev, ref WAVEFORMATEX f, IntPtr cb, IntPtr inst, int flags);
        [DllImport("winmm.dll")] public static extern int waveOutPrepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] public static extern int waveOutUnprepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] public static extern int waveOutWrite(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] public static extern int waveOutReset(IntPtr h);
        [DllImport("winmm.dll")] public static extern int waveOutClose(IntPtr h);
        [DllImport("winmm.dll")] public static extern int waveOutGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Auto)] public static extern int waveOutGetDevCaps(int id, ref WAVEOUTCAPS caps, int size);
    }
}
