using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Voltura.NetOn;

internal sealed record Adapter(string Name, Guid Id, string Description, string Status, int AdminStatus)
{
    public bool Enabled => AdminStatus == 1;
    public override string ToString() => Name;
}

internal static class NetworkAdapters
{
    public static async Task<List<Adapter>> ReadAsync()
    {
        string json = await RunAsync("@(Get-NetAdapter -Physical -ErrorAction Stop | Where-Object Status -ne 'Not Present' | ForEach-Object { [pscustomobject]@{ Name=$_.Name; Id=([guid]$_.InterfaceGuid).ToString('D'); Description=$_.InterfaceDescription; Status=[string]$_.Status; AdminStatus=[int]$_.AdminStatus } }) | ConvertTo-Json -Compress -Depth 3");
        if (string.IsNullOrWhiteSpace(json)) return [];
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.ValueKind == JsonValueKind.Array
            ? JsonSerializer.Deserialize<List<Adapter>>(json) ?? []
            : [JsonSerializer.Deserialize<Adapter>(json)!];
    }

    public static async Task SetEnabledAsync(Guid id, bool enabled)
    {
        // Only a parsed GUID enters the command. Never interpolate adapter names.
        await RunAsync($"$adapter = @(Get-NetAdapter -Physical -ErrorAction Stop | Where-Object {{ $_.InterfaceGuid -eq '{{{id:D}}}' }}); if ($adapter.Count -ne 1) {{ throw 'The selected adapter is no longer available.' }}; $adapter | {(enabled ? "Enable" : "Disable")}-NetAdapter -Confirm:$false -ErrorAction Stop");
    }

    private static async Task<string> RunAsync(string script)
    {
        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (string arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes("$ErrorActionPreference='Stop'; [Console]::OutputEncoding=[System.Text.UTF8Encoding]::new($false); " + script)) })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Windows PowerShell.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException("Windows took too long to respond. Refresh the adapter status before trying again.");
        }
        string result = await output;
        string failure = await error;
        if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(failure) ? "Windows could not change the network adapter." : failure.Trim());
        return result;
    }
}
