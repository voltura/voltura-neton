using System.Collections.Concurrent;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;

namespace Voltura.NetOn;

internal sealed class Speech : IDisposable
{
    internal const int SampleRate = 22050;
    internal const int CueMilliseconds = 1200;
    private readonly BlockingCollection<string> pending = new(1);
    private readonly ManualResetEventSlim shutdown = new();
    private readonly object gate = new();
    private SoundPlayer? player;
    private bool disposed;
    private Thread? worker;

    public void Say(string text)
    {
        lock (gate)
        {
            if (disposed) return;
            if (worker is null)
            {
                worker = new Thread(PlayAnnouncements) { IsBackground = true, Name = "NetOn speech" };
                worker.SetApartmentState(ApartmentState.STA);
                worker.Start();
            }
            // Finish the current phrase rather than cutting it off; retain only the latest pending one.
            pending.TryTake(out _);
            pending.TryAdd(text);
        }
    }

    private void PlayAnnouncements()
    {
        foreach (string text in pending.GetConsumingEnumerable())
        {
            if (shutdown.IsSet) return;
            try
            {
                byte[] wave = RenderWave(text);
                using var stream = new MemoryStream(wave);
                using var output = new SoundPlayer(stream);
                output.Load();
                lock (gate)
                {
                    if (disposed) return;
                    player = output;
                    output.Play();
                }
                // Cue and speech share one audio stream, with no device restart between them.
                int duration = (wave.Length - 44) * 1000 / (SampleRate * 2) + 250;
                shutdown.Wait(duration);
                lock (gate) { output.Stop(); player = null; }
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException or IOException or TimeoutException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
            {
                // Missing speech/audio must not block network controls or Exit.
            }
        }
    }

    internal static byte[] RenderWave(string text)
    {
        object? voice = null, memory = null;
        try
        {
            voice = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpVoice")!);
            memory = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpMemoryStream")!);
            dynamic output = memory!;
            output.Format.Type = 22; // 22.05 kHz, 16-bit mono PCM.
            ((dynamic)voice!).AudioOutputStream = output;
            ((dynamic)voice).Speak(text, 16); // Synthesize the whole phrase as plain text, without playback.
            byte[] speech = (byte[])output.GetData();
            int cueSamples = SampleRate * CueMilliseconds / 1000;
            int dataLength = cueSamples * 2 + speech.Length;
            using var wave = new MemoryStream(44 + dataLength);
            using var writer = new BinaryWriter(wave, Encoding.ASCII, leaveOpen: true);
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + dataLength);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(SampleRate);
            writer.Write(SampleRate * 2); writer.Write((short)2); writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(dataLength);
            for (int i = 0; i < cueSamples; i++)
            {
                // A real, quiet signal wakes outputs that discard digital silence.
                double envelope = Math.Min(1, Math.Min(i, cueSamples - 1 - i) / (SampleRate * .015));
                writer.Write((short)(2200 * envelope * Math.Sin(2 * Math.PI * 523.25 * i / SampleRate)));
            }
            writer.Write(speech);
            return wave.ToArray();
        }
        finally
        {
            if (voice is not null && Marshal.IsComObject(voice)) Marshal.FinalReleaseComObject(voice);
            if (memory is not null && Marshal.IsComObject(memory)) Marshal.FinalReleaseComObject(memory);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            shutdown.Set();
            pending.CompleteAdding();
            player?.Stop();
        }
    }
}
