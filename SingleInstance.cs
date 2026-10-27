using System.Security.Principal;

namespace Voltura.NetOn;

internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly EventWaitHandle activate;
    private readonly bool ownsMutex;
    private RegisteredWaitHandle? listener;
    public bool IsPrimary => ownsMutex;

    public SingleInstance(string? name = null)
    {
        string key = name ?? $"Local\\Voltura.NetOn.v2.{WindowsIdentity.GetCurrent().User?.Value}";
        mutex = new Mutex(false, key);
        try { ownsMutex = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { ownsMutex = true; }
        activate = new EventWaitHandle(false, EventResetMode.AutoReset, key + ".Activate");
    }

    public void RequestActivation() => activate.Set();
    public void Listen(Action show) => listener = ThreadPool.RegisterWaitForSingleObject(activate, (_, _) => show(), null, Timeout.Infinite, false);
    public void Dispose()
    {
        listener?.Unregister(null);
        activate.Dispose();
        if (ownsMutex) mutex.ReleaseMutex();
        mutex.Dispose();
    }
}
