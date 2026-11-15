using System;
using System.Runtime.InteropServices;

namespace GControl
{
    // Lo que puede hacer el botón G1. El id se guarda en el registro.
    class GAction
    {
        public readonly int Id;
        public readonly string Name;
        readonly byte vk;

        GAction(int id, string name, byte vk) { Id = id; Name = name; this.vk = vk; }

        public const int MicMute = 7;

        public static readonly GAction[] All =
        {
            new GAction(1, "Reproducir/Pausa", 0xB3),
            new GAction(MicMute, "Silenciar micrófono", 0),
            new GAction(2, "Siguiente pista", 0xB0),
            new GAction(3, "Pista anterior", 0xB1),
            new GAction(4, "Subir volumen", 0xAF),
            new GAction(5, "Bajar volumen", 0xAE),
            new GAction(6, "Silenciar sonido", 0xAD),
            new GAction(0, "Ninguna", 0),
        };

        public static GAction Get(int id)
        {
            foreach (var a in All) if (a.Id == id) return a;
            return All[0];
        }

        // Devuelve el texto a mostrar, o null si no hay nada que avisar.
        public string Run()
        {
            if (Id == MicMute)
            {
                bool muted;
                if (!Audio.ToggleMicMute(out muted)) return "No hay micrófono";
                return muted ? "Micrófono silenciado" : "Micrófono activo";
            }
            if (vk == 0) return null;
            keybd_event(vk, 0, 1, UIntPtr.Zero);
            keybd_event(vk, 0, 3, UIntPtr.Zero);
            return null;
        }

        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    }

    // Silencio del micrófono por defecto con la API de audio de Windows.
    static class Audio
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(int flow, int mask, out IntPtr devices);
            int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            int Activate(ref Guid iid, int ctx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object o);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioEndpointVolume
        {
            int RegisterControlChangeNotify(IntPtr n);
            int UnregisterControlChangeNotify(IntPtr n);
            int GetChannelCount(out int n);
            int SetMasterVolumeLevel(float db, ref Guid ctx);
            int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
            int GetMasterVolumeLevel(out float db);
            int GetMasterVolumeLevelScalar(out float level);
            int SetChannelVolumeLevel(int ch, float db, ref Guid ctx);
            int SetChannelVolumeLevelScalar(int ch, float level, ref Guid ctx);
            int GetChannelVolumeLevel(int ch, out float db);
            int GetChannelVolumeLevelScalar(int ch, out float level);
            int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
            int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }

        static IAudioEndpointVolume Mic()
        {
            var en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            IMMDevice dev;
            if (en.GetDefaultAudioEndpoint(1, 2, out dev) != 0 || dev == null) return null; // captura, comunicaciones
            var iid = typeof(IAudioEndpointVolume).GUID;
            object o;
            if (dev.Activate(ref iid, 23, IntPtr.Zero, out o) != 0) return null;
            return (IAudioEndpointVolume)o;
        }

        public static bool ToggleMicMute(out bool muted)
        {
            muted = false;
            try
            {
                var v = Mic();
                if (v == null) return false;
                v.GetMute(out muted);
                muted = !muted;
                var ctx = Guid.Empty;
                return v.SetMute(muted, ref ctx) == 0;
            }
            catch { return false; }
        }

        // Nivel del micrófono por defecto (0..100), como el control de Windows.
        public static int MicLevel()
        {
            try
            {
                var v = Mic();
                float f;
                if (v == null || v.GetMasterVolumeLevelScalar(out f) != 0) return -1;
                return (int)Math.Round(f * 100);
            }
            catch { return -1; }
        }

        public static bool SetMicLevel(int level)
        {
            try
            {
                var v = Mic();
                var ctx = Guid.Empty;
                return v != null && v.SetMasterVolumeLevelScalar(Math.Max(0, Math.Min(100, level)) / 100f, ref ctx) == 0;
            }
            catch { return false; }
        }

        public static bool? MicMuted()
        {
            try
            {
                var v = Mic();
                bool m;
                if (v == null || v.GetMute(out m) != 0) return null;
                return m;
            }
            catch { return null; }
        }
    }
}
