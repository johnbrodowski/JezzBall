using System;
using System.IO;
using System.Runtime.InteropServices;

namespace JezzBall
{
    // Low-latency SFX via the winmm waveOut API. Each sound keeps its wave device(s)
    // open and pre-prepared, so triggering is instant instead of re-opening the audio
    // device on every call (which is what made PlaySound lag). A small pool of voices
    // lets rapid/overlapping bounces play without cutting each other off.
    sealed class SoundFx
    {
        const int Voices = 4;
        const uint WAVE_MAPPER = 0xFFFFFFFF;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct WAVEFORMATEX
        {
            public ushort wFormatTag, nChannels;
            public uint nSamplesPerSec, nAvgBytesPerSec;
            public ushort nBlockAlign, wBitsPerSample, cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WAVEHDR
        {
            public IntPtr lpData;
            public uint dwBufferLength, dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags, dwLoops;
            public IntPtr lpNext, reserved;
        }

        [DllImport("winmm.dll")] static extern int waveOutOpen(out IntPtr h, uint dev, ref WAVEFORMATEX fmt, IntPtr cb, IntPtr inst, uint flags);
        [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutReset(IntPtr h);

        readonly IntPtr[] devs = new IntPtr[Voices];
        readonly IntPtr[] hdrs = new IntPtr[Voices];
        readonly bool ok;
        int next;

        SoundFx(WAVEFORMATEX fmt, byte[] pcm)
        {
            IntPtr data = Marshal.AllocHGlobal(pcm.Length);
            Marshal.Copy(pcm, 0, data, pcm.Length);
            int hdrSize = Marshal.SizeOf<WAVEHDR>();

            for (int i = 0; i < Voices; i++)
            {
                if (waveOutOpen(out devs[i], WAVE_MAPPER, ref fmt, IntPtr.Zero, IntPtr.Zero, 0) != 0)
                    return; // device unavailable -> stays silent
                var hdr = new WAVEHDR { lpData = data, dwBufferLength = (uint)pcm.Length };
                hdrs[i] = Marshal.AllocHGlobal(hdrSize);
                Marshal.StructureToPtr(hdr, hdrs[i], false);
                waveOutPrepareHeader(devs[i], hdrs[i], hdrSize);
            }
            ok = true;
        }

        public void Play()
        {
            if (!ok) return;
            int v = next; next = (next + 1) % Voices;
            int hdrSize = Marshal.SizeOf<WAVEHDR>();
            waveOutReset(devs[v]);                 // retrigger this voice from the start
            waveOutWrite(devs[v], hdrs[v], hdrSize);
        }

        // Load a WAV, optionally repeating its audio `loops` times (JEZZDEAD.WAV is very
        // short, so we lengthen it to match the original feel).
        public static SoundFx Load(string path, int loops = 1)
        {
            byte[] file = File.ReadAllBytes(path);
            var fmt = ReadFormat(file);
            byte[] pcm = ReadData(file);
            if (loops > 1)
            {
                byte[] longer = new byte[pcm.Length * loops];
                for (int t = 0; t < loops; t++) Array.Copy(pcm, 0, longer, t * pcm.Length, pcm.Length);
                pcm = longer;
            }
            return new SoundFx(fmt, pcm);
        }

        static WAVEFORMATEX ReadFormat(byte[] b)
        {
            int p = FindChunk(b, "fmt ");
            var f = new WAVEFORMATEX
            {
                wFormatTag = BitConverter.ToUInt16(b, p + 8),
                nChannels = BitConverter.ToUInt16(b, p + 10),
                nSamplesPerSec = BitConverter.ToUInt32(b, p + 12),
                nAvgBytesPerSec = BitConverter.ToUInt32(b, p + 16),
                nBlockAlign = BitConverter.ToUInt16(b, p + 20),
                wBitsPerSample = BitConverter.ToUInt16(b, p + 22),
                cbSize = 0,
            };
            return f;
        }

        static byte[] ReadData(byte[] b)
        {
            int p = FindChunk(b, "data");
            int size = BitConverter.ToInt32(b, p + 4);
            int start = p + 8;
            if (start + size > b.Length) size = b.Length - start;
            byte[] pcm = new byte[size];
            Array.Copy(b, start, pcm, 0, size);
            return pcm;
        }

        static int FindChunk(byte[] b, string tag)
        {
            int p = 12;
            while (p + 8 <= b.Length)
            {
                if (b[p] == tag[0] && b[p + 1] == tag[1] && b[p + 2] == tag[2] && b[p + 3] == tag[3]) return p;
                int size = BitConverter.ToInt32(b, p + 4);
                p += 8 + size + (size & 1);
            }
            return -1;
        }
    }
}
