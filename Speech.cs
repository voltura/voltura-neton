using System.Collections.Concurrent;
using NAudio.Wave;
using System.Runtime.InteropServices;
using System.Text;
using System.Security;
using System.Text.RegularExpressions;

namespace Voltura.NetOn;

internal sealed class Speech : IDisposable
{
    internal sealed record VoiceChoice(string Id, string Name);
    private readonly Func<string?> preferredVoice;
    private readonly Func<IWavePlayer> createOutput;
    public Speech(Func<string?>? preferredVoice = null, Func<IWavePlayer>? createOutput = null)
    {
        this.preferredVoice = preferredVoice ?? (() => null);
        this.createOutput = createOutput ?? (() => new WaveOutEvent { DesiredLatency = 150, NumberOfBuffers = 3 });
    }
    internal const int SampleRate = 22050;
    private readonly BlockingCollection<string> pending = new(1);
    private readonly ManualResetEventSlim shutdown = new();
    private readonly object gate = new();
    private bool disposed;
    private volatile bool muted;
    private int playbackGeneration;
    private int requestGeneration;
    private Thread? worker;

    public void SetMuted(bool value)
    {
        lock (gate)
        {
            if (disposed) return;
            muted = value;
            if (value) { Interlocked.Increment(ref playbackGeneration); pending.TryTake(out _); }
            else { Interlocked.Increment(ref requestGeneration); EnsureWorker(); }
        }
    }

    private void EnsureWorker()
    {
        if (worker is not null) return;
        worker = new Thread(PlayAnnouncements) { IsBackground = true, Name = "NetOn speech" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
    }

    public void Say(string text)
    {
        lock (gate)
        {
            if (disposed || muted) return;
            EnsureWorker();
            // Finish the current recording; retain only the newest waiting announcement.
            pending.TryTake(out _);
            pending.TryAdd(text);
            Interlocked.Increment(ref requestGeneration);
        }
    }

    private void PlayAnnouncements()
    {
        IWavePlayer? output = null;
        var source = new AnnouncementAudio();
        bool failed = false;
        int failedGeneration = 0;
        try
        {
            while (!shutdown.IsSet)
            {
                if (muted)
                {
                    CloseOutput(ref output);
                    source.Clear();
                    shutdown.Wait(50);
                    continue;
                }
                int request = Volatile.Read(ref requestGeneration);
                if (output is null && failed && request == failedGeneration) { shutdown.Wait(50); continue; }
                try
                {
                    if (output is null || output.PlaybackState == PlaybackState.Stopped)
                    {
                        CloseOutput(ref output);
                        source = new AnnouncementAudio();
                        output = createOutput();
                        output.Init(source);
                        output.Play();
                        failed = false;
                        // Warm the endpoint once, without a beep, before sending any speech.
                        for (int i = 0; i < 24 && !muted && !shutdown.IsSet; i++) shutdown.Wait(50);
                    }
                    if (muted || shutdown.IsSet || !pending.TryTake(out string? text, 50)) continue;
                    int generation = Volatile.Read(ref playbackGeneration);
                    byte[] wave = RenderWave(text, preferredVoice());
                    if (muted || shutdown.IsSet || generation != Volatile.Read(ref playbackGeneration)) continue;
                    source.Play(wave[44..]);
                    // Wait for the actual device position, not a guessed recording duration.
                    // Compare modulo 32 bits because WinMM's byte counter wraps on long-running sessions.
                    while (!shutdown.IsSet && !muted && generation == Volatile.Read(ref playbackGeneration))
                    {
                        if (output.PlaybackState != PlaybackState.Playing) break;
                        if (source.EndPosition is long end && unchecked((int)((uint)((IWavePosition)output).GetPosition() - (uint)end)) >= 0) break;
                        shutdown.Wait(25);
                    }
                    source.Clear();
                }
                catch (Exception ex) when (ex is COMException or NAudio.MmException or InvalidOperationException or ArgumentException or IOException or InvalidDataException or TimeoutException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
                {
                    // Audio failure must never block network controls or Exit. Retry on the next request.
                    CloseOutput(ref output);
                    failed = true;
                    failedGeneration = request;
                    shutdown.Wait(250);
                }
            }
        }
        finally
        {
            CloseOutput(ref output);
            // Dispose signals the worker while holding this same gate. Wait until it
            // has completed CompleteAdding before releasing the collection/event.
            lock (gate) { pending.Dispose(); shutdown.Dispose(); }
        }
    }

    private static void CloseOutput(ref IWavePlayer? output)
    {
        var previous = output;
        output = null;
        try { previous?.Dispose(); }
        catch (Exception ex) when (ex is NAudio.MmException or COMException or InvalidOperationException) { }
    }

    internal static IReadOnlyList<VoiceChoice> ListVoices()
    {
        object? voice = null, tokens = null;
        var choices = new List<VoiceChoice>();
        try
        {
            voice = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpVoice")!);
            tokens = ((dynamic)voice!).GetVoices();
            for (int i = 0; i < ((dynamic)tokens).Count; i++)
            {
                object token = ((dynamic)tokens).Item(i);
                try { choices.Add(new VoiceChoice(((dynamic)token).Id, ((dynamic)token).GetDescription())); }
                finally { Marshal.FinalReleaseComObject(token); }
            }
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
        finally { if (tokens is not null) Marshal.FinalReleaseComObject(tokens); if (voice is not null) Marshal.FinalReleaseComObject(voice); }
        return choices;
    }

    internal static string PronunciationMarkup(string text, bool english)
    {
        string escaped = SecurityElement.Escape(text) ?? "";
        string brand = english
            ? "<pron sym=\"v ao l - t uw 1 - r aa\">Voltura</pron><silence msec=\"100\"/><pron sym=\"aa 1 - b eh 1\">AB</pron>"
            : "Voltuura, Aabe";
        return Regex.Replace(escaped, @"\bVoltura\s+AB\b", _ => brand, RegexOptions.IgnoreCase);
    }

    internal static byte[] RenderWave(string text, string? voiceId = null)
    {
        if (voiceId is null or RecordedVoice.Id && RecordedVoice.ReadPcm(text) is { } recording) return WrapPcm(recording);
        object? voice = null, memory = null;
        try
        {
            voice = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpVoice")!);
            object tokens = ((dynamic)voice!).GetVoices();
            bool matchedVoice = false;
            try
            {
                for (int i = 0; i < ((dynamic)tokens).Count; i++)
                {
                    object token = ((dynamic)tokens).Item(i);
                    try
                    {
                        bool matches = voiceId is not null ? ((string)((dynamic)token).Id).Equals(voiceId, StringComparison.OrdinalIgnoreCase)
                            : ((string)((dynamic)token).GetDescription()).Contains("Zira", StringComparison.OrdinalIgnoreCase);
                        if (matches) { ((dynamic)voice).Voice = token; matchedVoice = true; break; }
                    }
                    finally { Marshal.FinalReleaseComObject(token); }
                }
            }
            finally { Marshal.FinalReleaseComObject(tokens); }
            if (voiceId is not null && !matchedVoice && RecordedVoice.ReadPcm(text) is { } fallback) return WrapPcm(fallback);
            ((dynamic)voice).Rate = -1;
            memory = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpMemoryStream")!);
            dynamic output = memory!;
            output.Format.Type = 22; // 22.05 kHz, 16-bit mono PCM.
            ((dynamic)voice!).AudioOutputStream = output;
            object selected = ((dynamic)voice).Voice;
            string language;
            try { language = ((dynamic)selected).GetAttribute("Language"); }
            finally { Marshal.FinalReleaseComObject(selected); }
            bool english = language.Split(';').Any(part => int.TryParse(part, System.Globalization.NumberStyles.HexNumber, null, out int id) && (id & 0x3ff) == 9);
            ((dynamic)voice).Speak(PronunciationMarkup(text, english), 8);
            byte[] speech = (byte[])output.GetData();
            return WrapPcm(speech);
        }
        finally
        {
            if (voice is not null && Marshal.IsComObject(voice)) Marshal.FinalReleaseComObject(voice);
            if (memory is not null && Marshal.IsComObject(memory)) Marshal.FinalReleaseComObject(memory);
        }
    }

    internal static byte[] WrapPcm(byte[] speech)
    {
        using var wave = new MemoryStream(44 + speech.Length);
        using var writer = new BinaryWriter(wave, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + speech.Length);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
        writer.Write((short)1); writer.Write((short)1); writer.Write(SampleRate);
        writer.Write(SampleRate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(speech.Length);
        writer.Write(speech);
        return wave.ToArray();
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            shutdown.Set();
            pending.CompleteAdding();
            if (worker is null) { pending.Dispose(); shutdown.Dispose(); }
        }
    }
}
