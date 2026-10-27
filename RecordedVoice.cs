namespace Voltura.NetOn;

internal static class RecordedVoice
{
    internal const string Id = "recorded-offline";
    internal static readonly IReadOnlyDictionary<string, string> Phrases = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Internet enabled"] = "internet-on", ["Internet disabled"] = "internet-off",
        ["NetOn by Voltura AB"] = "about", ["NetOn set to start with Windows"] = "startup-on",
        ["NetOn will not start with Windows"] = "startup-off", ["Colors reset"] = "colors-reset",
        ["Please select foreground color"] = "foreground", ["Please select background color"] = "background"
    };

    internal static byte[]? ReadPcm(string text)
    {
        if (!Phrases.TryGetValue(text, out string? id)) return null;
        using var stream = typeof(RecordedVoice).Assembly.GetManifestResourceStream($"Voltura.NetOn.Voice.{id}.wav");
        if (stream is null) return null;
        using var reader = new BinaryReader(stream);
        string FourCc() => System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (FourCc() != "RIFF") throw new InvalidDataException("Invalid embedded voice recording.");
        reader.ReadUInt32();
        if (FourCc() != "WAVE") throw new InvalidDataException("Invalid embedded voice recording.");
        bool validFormat = false;
        while (stream.Position + 8 <= stream.Length)
        {
            string chunk = FourCc();
            int length = reader.ReadInt32();
            long next = stream.Position + length + (length & 1);
            if (length < 0 || next > stream.Length) throw new InvalidDataException("Invalid voice recording length.");
            if (chunk == "fmt ")
            {
                int format = reader.ReadUInt16(), channels = reader.ReadUInt16(), rate = reader.ReadInt32();
                reader.ReadInt32(); reader.ReadUInt16(); int bits = reader.ReadUInt16();
                validFormat = format == 1 && channels == 1 && rate == Speech.SampleRate && bits == 16;
            }
            if (chunk == "data" && validFormat) return reader.ReadBytes(length);
            stream.Position = next;
        }
        throw new InvalidDataException("Embedded voice recording is not supported PCM audio.");
    }
}
