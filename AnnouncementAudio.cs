using NAudio.Wave;

namespace Voltura.NetOn;

// Never ends the stream between clips: reopening an idle endpoint can swallow speech starts.
internal sealed class AnnouncementAudio : IWaveProvider
{
    private readonly object gate = new();
    private byte[]? recording;
    private int position;
    private long bytesRead;
    private long? endPosition;
    public long? EndPosition { get { lock (gate) return endPosition; } }
    public WaveFormat WaveFormat { get; } = new(Speech.SampleRate, 16, 1);

    public void Play(byte[] pcm)
    {
        lock (gate) { recording = pcm; position = 0; endPosition = null; }
    }

    public void Clear()
    {
        lock (gate) { recording = null; position = 0; endPosition = null; }
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        // One PCM step (-90 dBFS), inaudible in normal use, keeps silence-gated outputs awake.
        // This signal is used only between recordings and never mixed into the recorded voice.
        for (int i = 0; i < count; i += 2)
        {
            bool positive = ((bytesRead + i) / 2 & 1) == 0;
            buffer[offset + i] = positive ? (byte)1 : (byte)255;
            if (i + 1 < count) buffer[offset + i + 1] = positive ? (byte)0 : (byte)255;
        }
        lock (gate)
        {
            if (recording is not null)
            {
                int length = Math.Min(count, recording.Length - position);
                Buffer.BlockCopy(recording, position, buffer, offset, length);
                position += length;
                if (position == recording.Length) { endPosition = bytesRead + length; recording = null; position = 0; }
            }
            bytesRead += count;
        }
        return count;
    }
}
