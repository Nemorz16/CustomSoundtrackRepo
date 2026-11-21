using System;
using System.IO;
using System.Text;
using CustomSoundtrack;
static class HeaderTests
{
    public static void Run()
    {
        string path = Path.GetTempFileName();
        try
        {
            byte[] valid;
            using (var ms = new MemoryStream())
            {
                using (var w = new BinaryWriter(ms, Encoding.ASCII, true))
                {
                    w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(40u); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                    w.Write(16u); w.Write((ushort)1); w.Write((ushort)2); w.Write(48000u); w.Write(192000u);
                    w.Write((ushort)4); w.Write((ushort)16); w.Write(Encoding.ASCII.GetBytes("data")); w.Write(4u); w.Write(0u);
                }
                valid = ms.ToArray();
            }
            File.WriteAllBytes(path, valid);
            if (WavHeader.Check(path) != null) throw new Exception("Valid PCM rejected");
            foreach (int offset in new[] { 4, 40 })
            {
                byte[] bad = (byte[])valid.Clone(); Array.Copy(BitConverter.GetBytes(uint.MaxValue), 0, bad, offset, 4);
                File.WriteAllBytes(path, bad);
                if (WavHeader.Check(path) == null) throw new Exception("Placeholder chunk size accepted");
            }
            for (int length = 0; length < valid.Length; length++)
            {
                File.WriteAllBytes(path, valid.AsSpan(0, length).ToArray());
                if (WavHeader.Check(path) == null) throw new Exception("Truncated WAV accepted");
            }
            byte[] wrongAlign = (byte[])valid.Clone(); wrongAlign[32] = 2;
            File.WriteAllBytes(path, wrongAlign);
            if (WavHeader.Check(path) == null) throw new Exception("Wrong PCM alignment accepted");
            Console.WriteLine("PASS: valid PCM, placeholder sizes, truncation and format alignment.");
        }
        finally { File.Delete(path); }
    }
}
