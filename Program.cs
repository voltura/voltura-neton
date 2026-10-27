namespace Voltura.NetOn;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 0) return 2;
        // Keep the launcher asInvoker for Windows startup, then elevate the whole tray session.
        if (!NetworkAdapters.IsAdministrator())
        {
            try
            {
                using var elevated = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
                return 0;
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { return 0; }
        }
        ApplicationConfiguration.Initialize();
        using var instance = new SingleInstance();
        if (!instance.IsPrimary)
        {
            instance.RequestActivation();
            return 0;
        }
        using var form = new MainForm();
        _ = form.Handle;
        instance.Listen(form.RequestShow);
        form.Start();
        Application.Run(form);
        return 0;
    }
}
