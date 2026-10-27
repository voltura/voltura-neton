using System.Runtime.InteropServices;

namespace Voltura.NetOn;

internal sealed class Speech : IDisposable
{
    private object? voice;
    public void Say(string text)
    {
        try
        {
            voice ??= Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpVoice")!);
            ((dynamic)voice!).Speak(text, 3);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { /* Speech availability never blocks network controls. */ }
    }
    public void Dispose() { if (voice is not null && Marshal.IsComObject(voice)) Marshal.FinalReleaseComObject(voice); voice = null; }
}
