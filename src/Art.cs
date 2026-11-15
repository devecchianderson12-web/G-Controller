using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace GControl
{
    // Dibujos vectoriales: icono de la app, batería y señal inalámbrica.
    static class Art
    {
        static readonly Color Blue = Color.FromArgb(26, 159, 255);

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // Icono minimalista: cuadro oscuro, anillo azul abierto y un punto (un dial de control).
        public static void DrawLogo(Graphics g, float x, float y, float size)
        {
            var st = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TranslateTransform(x, y);
            g.ScaleTransform(size / 100f, size / 100f);
            var box = new RectangleF(4, 4, 92, 92);
            using (var p = Round(box, 24))
            {
                using (var b = new SolidBrush(Color.FromArgb(18, 19, 22))) g.FillPath(b, p);
                if (size >= 32) using (var edge = new Pen(Color.FromArgb(48, 50, 56), 2)) g.DrawPath(edge, p);
            }
            var ring = new RectangleF(24, 24, 52, 52);
            using (var pen = new Pen(Blue, size < 32 ? 13 : 10) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(pen, ring, -40, 300);
            using (var dot = new SolidBrush(Color.White)) g.FillEllipse(dot, 43, 43, 14, 14);
            g.Restore(st);
        }

        // Batería horizontal. level <0 = sin dato.
        public static void DrawBattery(Graphics g, RectangleF r, int level, Color fg)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var body = new RectangleF(r.X, r.Y, r.Width - r.Height * 0.18f, r.Height);
            using (var pen = new Pen(fg, Math.Max(1f, r.Height / 9f)))
            using (var bp = Round(body, r.Height * 0.18f)) g.DrawPath(pen, bp);
            using (var b = new SolidBrush(fg))
                g.FillRectangle(b, body.Right + 0.5f, r.Y + r.Height * 0.3f, r.Height * 0.14f, r.Height * 0.4f);
            if (level < 0) return;
            float pad = Math.Max(2f, r.Height / 5f);
            var fill = RectangleF.Inflate(body, -pad, -pad);
            fill.Width = Math.Max(1f, fill.Width * level / 100f);
            using (var b = new SolidBrush(LevelColor(level, fg))) g.FillRectangle(b, fill);
        }

        public static Color LevelColor(int level, Color normal)
        {
            if (level < 0) return Color.FromArgb(120, 120, 128);
            if (level <= 15) return Color.FromArgb(240, 70, 70);
            if (level <= 30) return Color.FromArgb(240, 180, 50);
            return normal;
        }

        // Ondas de señal inalámbrica (abanico hacia arriba).
        public static void DrawWireless(Graphics g, RectangleF r, Color fg)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float cx = r.X + r.Width / 2, by = r.Bottom;
            using (var pen = new Pen(fg, Math.Max(1.2f, r.Height / 8f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                for (int i = 1; i <= 3; i++)
                {
                    float rad = r.Height * i / 3f;
                    g.DrawArc(pen, cx - rad, by - rad, rad * 2, rad * 2, 225, 90);
                }
            using (var b = new SolidBrush(fg))
                g.FillEllipse(b, cx - r.Height / 9f, by - r.Height / 4.5f, r.Height / 4.5f, r.Height / 4.5f);
        }
    }
}
