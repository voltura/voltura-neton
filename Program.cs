namespace Voltura.NetOn;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 0) return 2;
        ApplicationConfiguration.Initialize();
        // Keep the launcher asInvoker for Windows startup, then elevate the whole tray session.
        if (!NetworkAdapters.IsAdministrator())
            return AppLaunch.Elevate();
        using var instance = new SingleInstance();
        if (!instance.IsPrimary)
        {
            instance.RequestActivation();
            return 0;
        }
        using var form = new MainForm();
        _ = form.Handle;
        instance.Listen(form.RequestShow);
        instance.ListenForExit(form.RequestExit);
        form.Start();
        Application.Run(form);
        return 0;
    }
}
