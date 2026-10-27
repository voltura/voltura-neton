namespace Voltura.NetOn;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, "Local\\Voltura.NetOn", out bool first);
        ApplicationConfiguration.Initialize();
        if (!first)
        {
            MessageBox.Show("NetOn is already running. Open it from the system tray.", "Voltura NetOn");
            return;
        }
        Application.Run(new MainForm());
    }
}
