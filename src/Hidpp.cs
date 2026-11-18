using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace GControl
{
    // HID++ 2.0 por el reporte largo (0x11, 20 bytes): [0x11, dev, feature, fn<<4|sw, params...].
    class Hidpp : IDisposable
    {
        const byte Long = 0x11;
        const byte Sw = 0x0A;
        readonly string filter;
        readonly ushort page, usage;
        public readonly byte Device;
        SafeFileHandle wr, rd;
        readonly BlockingCollection<byte[]> inbox = new BlockingCollection<byte[]>();
        readonly Dictionary<ushort, byte> features = new Dictionary<ushort, byte>();
        readonly object gate = new object();

        // Notificaciones del aparato (sw id 0): feature index, función, datos.
        public event Action<byte, byte, byte[]> Notification;

        public Hidpp(string filter, ushort page, ushort usage, byte device)
        {
            this.filter = filter; this.page = page; this.usage = usage; Device = device;
        }

        public bool IsOpen { get { return wr != null && !wr.IsClosed; } }

        public bool Open()
        {
            lock (gate)
            {
                Close();
                string path = Native.Find(filter, page, usage);
                if (path == null) return false;
                wr = Native.CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                rd = Native.CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                if (wr.IsInvalid || rd.IsInvalid) { Close(); return false; }
                var h = rd;
                new Thread(() => ReadLoop(h)) { IsBackground = true }.Start();
                return true;
            }
        }

        // Termina cuando se cierra el aparato. Si el handle se cierra entre dos lecturas, ReadFile
        // tira ObjectDisposedException: antes eso mataba la app entera (hilo sin try).
        void ReadLoop(SafeFileHandle h)
        {
            try
            {
                while (!h.IsClosed)
                {
                    var b = new byte[20];
                    int n;
                    if (!Native.ReadFile(h, b, 20, out n, IntPtr.Zero) || n == 0) break;
                    if (b[0] != Long || b[1] != Device) continue;
                    if ((b[3] & 0x0F) == 0 && b[2] != 0xFF)
                    {
                        var data = new byte[16];
                        Array.Copy(b, 4, data, 0, 16);
                        var ev = Notification;
                        try { if (ev != null) ev(b[2], (byte)(b[3] >> 4), data); }
                        catch (Exception e) { Program.Log(e); }
                    }
                    else inbox.Add(b);
                }
            }
            catch (ObjectDisposedException) { }
            catch (Exception e) { Program.Log(e); }
        }

        public void Close()
        {
            lock (gate)
            {
                if (rd != null) { Native.CancelIoEx(rd, IntPtr.Zero); rd.Dispose(); rd = null; }
                if (wr != null) { wr.Dispose(); wr = null; }
                features.Clear();
                byte[] x;
                while (inbox.TryTake(out x)) { }
            }
        }

        public void Dispose() { Close(); }

        byte[] Send(byte index, byte fn, byte[] args)
        {
            if (!IsOpen) throw new System.IO.IOException("closed");
            byte[] x;
            while (inbox.TryTake(out x)) { }
            var p = new byte[20];
            p[0] = Long; p[1] = Device; p[2] = index; p[3] = (byte)(fn << 4 | Sw);
            if (args != null) Array.Copy(args, 0, p, 4, Math.Min(args.Length, 16));
            int n;
            if (!Native.WriteFile(wr, p, 20, out n, IntPtr.Zero)) throw new System.IO.IOException("write");
            var until = DateTime.UtcNow.AddMilliseconds(1500);
            while (true)
            {
                int left = (int)(until - DateTime.UtcNow).TotalMilliseconds;
                if (left <= 0 || !inbox.TryTake(out x, left)) return null;
                if (x[2] == index && x[3] == p[3]) { var r = new byte[16]; Array.Copy(x, 4, r, 0, 16); return r; }
                if (x[2] == 0xFF && x[3] == index && x[4] == p[3]) return null; // error
            }
        }

        // Índice de una feature (0 = no existe). Se pregunta a IRoot y se guarda.
        public byte Index(ushort feature)
        {
            lock (gate)
            {
                byte i;
                if (features.TryGetValue(feature, out i)) return i;
                var r = Send(0, 0, new[] { (byte)(feature >> 8), (byte)feature });
                i = r == null ? (byte)0 : r[0];
                features[feature] = i;
                return i;
            }
        }

        // Llama una función de una feature. null si no existe o falla.
        public byte[] Call(ushort feature, byte fn, params byte[] args)
        {
            lock (gate)
            {
                byte i = Index(feature);
                if (i == 0) return null;
                return Send(i, fn, args);
            }
        }

        public string Name()
        {
            var c = Call(0x0005, 0);
            if (c == null) return null;
            var sb = new System.Text.StringBuilder();
            for (int off = 0; off < c[0]; off += 16)
            {
                var r = Call(0x0005, 1, (byte)off);
                if (r == null) break;
                for (int k = 0; k < 16 && off + k < c[0]; k++) sb.Append((char)r[k]);
            }
            return sb.ToString().Trim('\0', ' ');
        }
    }

    static class Native
    {
        [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
        [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
        [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr p);
        [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr p, out HIDP_CAPS c);
        [DllImport("setupapi.dll")] static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr w, int f);
        [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, int i, ref DID did);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref DID did, IntPtr buf, int size, out int req, IntPtr d);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern SafeFileHandle CreateFile(string n, uint a, uint s, IntPtr sa, uint c, uint f, IntPtr t);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool ReadFile(SafeFileHandle h, byte[] b, int len, out int read, IntPtr o);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool WriteFile(SafeFileHandle h, byte[] b, int len, out int written, IntPtr o);
        [DllImport("kernel32.dll")] public static extern bool CancelIoEx(SafeFileHandle h, IntPtr o);

        [StructLayout(LayoutKind.Sequential)] struct DID { public int cb; public Guid g; public int f; public IntPtr r; }

        [StructLayout(LayoutKind.Sequential)]
        struct HIDP_CAPS
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
                NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
                NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }

        // Primera interfaz HID cuya ruta contiene 'filter' y cuya colección es page/usage.
        public static string Find(string filter, ushort page, ushort usage)
        {
            Guid g;
            HidD_GetHidGuid(out g);
            IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12);
            try
            {
                for (int i = 0; ; i++)
                {
                    var did = new DID();
                    did.cb = Marshal.SizeOf(did);
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref did)) return null;
                    int req;
                    SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0, out req, IntPtr.Zero);
                    IntPtr buf = Marshal.AllocHGlobal(req);
                    Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                    string path = null;
                    if (SetupDiGetDeviceInterfaceDetail(set, ref did, buf, req, out req, IntPtr.Zero))
                        path = Marshal.PtrToStringUni(buf + 4);
                    Marshal.FreeHGlobal(buf);
                    if (path == null || path.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    using (var h = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
                    {
                        IntPtr pp;
                        if (h.IsInvalid || !HidD_GetPreparsedData(h, out pp)) continue;
                        HIDP_CAPS c;
                        HidP_GetCaps(pp, out c);
                        HidD_FreePreparsedData(pp);
                        if (c.UsagePage == page && c.Usage == usage && c.OutputReportByteLength == 20) return path;
                    }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
        }
    }
}
