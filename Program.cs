namespace Voltura.NetOn;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 4 && args[0] == "--set-adapter" && Guid.TryParse(args[1], out var adapterId) && args[2] is "on" or "off" && Guid.TryParse(args[3], out var operation))
        {
            try
            {
                if (!NetworkAdapters.IsAdministrator()) throw new InvalidOperationException("Administrator permission is required.");
                NetworkAdapters.SetAsAdministratorAsync(adapterId, args[2] == "on", CancellationToken.None).GetAwaiter().GetResult();
                return 0;
            }
            catch (Exception ex)
            {
                string path = NetworkAdapters.OperationPath(operation);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(ex.Message));
                return 1;
            }
        }
        if (args.Length != 0) return 2;
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
