using System.Security.Principal;

namespace Voltura.NetOn;

internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly EventWaitHandle activate;
    private readonly EventWaitHandle exit;
    private readonly bool ownsMutex;
    private RegisteredWaitHandle? listener;
    private RegisteredWaitHandle? exitListener;
    private bool disposed;
    public bool IsPrimary => !disposed && ownsMutex;

    public SingleInstance(string? name = null)
    {
        string key = name ?? $"Local\\Voltura.NetOn.v2.{WindowsIdentity.GetCurrent().User?.Value}";
        mutex = new Mutex(false, key);
        try { ownsMutex = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { ownsMutex = true; }
        activate = new EventWaitHandle(false, EventResetMode.AutoReset, key + ".Activate");
        exit = new EventWaitHandle(false, EventResetMode.AutoReset, key + $".Exit.{Environment.ProcessId}");
    }

    public void RequestActivation() => activate.Set();
    public void Listen(Action show) => listener = ThreadPool.RegisterWaitForSingleObject(activate, (_, _) => show(), null, Timeout.Infinite, false);
    public void ListenForExit(Action close) => exitListener = ThreadPool.RegisterWaitForSingleObject(exit, (_, _) => close(), null, Timeout.Infinite, false);
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        listener?.Unregister(null);
        exitListener?.Unregister(null);
        activate.Dispose();
        exit.Dispose();
        if (ownsMutex) mutex.ReleaseMutex();
        mutex.Dispose();
    }
}
