using System;
using System.Collections.Generic;
using System.Drawing;

namespace GControl
{
    class LightEffect
    {
        public readonly string Name;
        public readonly ushort Id;
        public LightEffect(ushort id, string name) { Id = id; Name = name; }
    }

    // Auriculares G733 (USB directo, interfaz HID++ en la colección FF43/0202).
    class G733
    {
        public readonly Hidpp Dev = new Hidpp("vid_046d&pid_0afe", 0xFF43, 0x0202, 0xFF);
        public event Action<bool> GKey; // true = pulsado

        public static readonly LightEffect[] Effects =
        {
            new LightEffect(0x0001, "Fijo"), new LightEffect(0x000A, "Respiración"),
            new LightEffect(0x0003, "Ciclo de color"), new LightEffect(0x0000, "Apagado"),
        };

        // Curva de descarga de G HUB (battery_0afe.xml): mV -> %.
        static readonly int[,] Curve =
        {
            { 3871, 100 }, { 3852, 95 }, { 3836, 90 }, { 3825, 85 }, { 3815, 80 }, { 3806, 75 }, { 3798, 70 },
            { 3790, 65 }, { 3784, 60 }, { 3777, 55 }, { 3769, 50 }, { 3756, 45 }, { 3746, 40 }, { 3736, 35 },
            { 3725, 30 }, { 3710, 25 }, { 3690, 20 }, { 3683, 15 }, { 3678, 10 }, { 3632, 5 }, { 3483, 0 },
        };

        bool gkeyDown;
        byte gkeyIndex;

        public G733()
        {
            Dev.Notification += (index, fn, data) =>
            {
                if (index != gkeyIndex || fn != 0) return;
                bool down = (data[0] & 1) != 0;
                if (down == gkeyDown) return;
                gkeyDown = down;
                var ev = GKey;
                if (ev != null) ev(down);
            };
        }

        public bool Ensure()
        {
            if (Dev.IsOpen) return true;
            if (!Dev.Open()) return false;
            gkeyIndex = Dev.Index(0x8010);
            return true;
        }

        // % de batería o -1. charging = enchufado.
        public int Battery(out bool charging)
        {
            charging = false;
            var r = Dev.Call(0x1F20, 0);
            if (r == null) return -1;
            int mv = r[0] << 8 | r[1];
            if (mv == 0) return -1;
            for (int i = 0; i < Curve.GetLength(0); i++)
                if (mv >= Curve[i, 0])
                {
                    if (i == 0) return 100;
                    double t = (mv - Curve[i, 0]) / (double)(Curve[i - 1, 0] - Curve[i, 0]);
                    return (int)Math.Round(Curve[i, 1] + t * (Curve[i - 1, 1] - Curve[i, 1]));
                }
            return 0;
        }

        // El botón G1 llega como evento en vez de su función de fábrica.
        public bool DivertGKey(bool on) { return Dev.Call(0x8010, 2, (byte)(on ? 1 : 0)) != null; }

        // Apagado automático sin sonido, en minutos (0 = nunca). 0x1F20 fn 1/2, probado en el G733.
        public int PowerOff()
        {
            var r = Dev.Call(0x1F20, 1);
            return r == null ? -1 : r[0];
        }

        public bool SetPowerOff(int minutes) { return Dev.Call(0x1F20, 2, (byte)Math.Max(0, Math.Min(90, minutes))) != null; }

        public int Sidetone()
        {
            var r = Dev.Call(0x8300, 0);
            return r == null ? -1 : r[0];
        }

        public bool SetSidetone(int level) { return Dev.Call(0x8300, 1, (byte)Math.Max(0, Math.Min(100, level))) != null; }

        // Las dos zonas (arriba y abajo). period en ms para respiración y ciclo.
        public bool SetLight(ushort effect, Color c, int periodMs, int zone)
        {
            byte idx = 0xFF;
            for (byte i = 0; i < 8; i++)
            {
                var r = Dev.Call(0x8070, 2, (byte)zone, i);
                if (r == null) break;
                if ((r[2] << 8 | r[3]) == effect) { idx = i; break; }
            }
            if (idx == 0xFF) return false;
            var p = new byte[13];
            p[0] = (byte)zone; p[1] = idx;
            if (effect == 0x0001) { p[2] = c.R; p[3] = c.G; p[4] = c.B; }
            else if (effect == 0x000A) { p[2] = c.R; p[3] = c.G; p[4] = c.B; p[5] = (byte)(periodMs >> 8); p[6] = (byte)periodMs; p[8] = 100; }
            else if (effect == 0x0003) { p[7] = (byte)(periodMs >> 8); p[8] = (byte)periodMs; p[9] = 100; }
            p[12] = 1; // guardar
            return Dev.Call(0x8070, 3, p) != null;
        }
    }

    // Ratón G305 por el receptor LIGHTSPEED (colección FF00/0002), índice 1..6.
    class G305
    {
        public Hidpp Dev = new Hidpp("vid_046d&pid_c53f", 0xFF00, 0x0002, 1);
        public string Name = "G305";

        public bool Ensure()
        {
            if (Dev.IsOpen) return true;
            for (byte d = 1; d <= 6; d++)
            {
                var h = new Hidpp("vid_046d&pid_c53f", 0xFF00, 0x0002, d);
                if (!h.Open()) return false;
                if (h.Index(0x2201) != 0)
                {
                    Dev = h;
                    Name = h.Name() ?? Name;
                    return true;
                }
                h.Close();
            }
            return false;
        }

        public int Battery(out bool charging)
        {
            charging = false;
            var r = Dev.Call(0x1000, 0);
            if (r == null) return -1;
            charging = r[2] >= 1 && r[2] <= 4; // 1 cargando, 2 casi lleno, 3 lleno, 4 carga lenta
            return r[0] > 100 ? -1 : r[0];
        }

        public int Dpi()
        {
            var r = Dev.Call(0x2201, 2, 0);
            return r == null ? -1 : r[1] << 8 | r[2];
        }

        public bool SetDpi(int dpi)
        {
            dpi = Math.Max(200, Math.Min(12000, dpi / 50 * 50));
            return Dev.Call(0x2201, 3, 0, (byte)(dpi >> 8), (byte)dpi) != null;
        }

        public int Rate()
        {
            var r = Dev.Call(0x8060, 1);
            return r == null || r[0] == 0 ? -1 : 1000 / r[0];
        }
    }
}
