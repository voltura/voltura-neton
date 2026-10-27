using System.Runtime.InteropServices;
using System.Security;

namespace Voltura.NetOn;

internal sealed class Speech : IDisposable
{
    private object? voice;
    internal static string BuildPrompt(string text) => $"<silence msec=\"650\"/>{SecurityElement.Escape(text)}";
    public void Say(string text)
    {
        try
        {
            voice ??= Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpVoice")!);
            // Silence is part of the audio stream, so the output can wake up before words start.
            ((dynamic)voice!).Speak(BuildPrompt(text), 1 | 2 | 8);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { /* Speech availability never blocks network controls. */ }
    }
    public void Dispose() { if (voice is not null && Marshal.IsComObject(voice)) Marshal.FinalReleaseComObject(voice); voice = null; }
}
