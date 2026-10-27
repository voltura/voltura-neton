using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace Voltura.NetOn;

internal sealed record Adapter(string Name, Guid Id, string Description, string Status, int AdminStatus)
{
    public bool Enabled => AdminStatus == 1;
    public override string ToString() => Name;
}

internal interface IAdapterService
{
    Task<List<Adapter>> ReadAsync(CancellationToken cancellationToken);
    Task SetEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken);
}

internal sealed class NetworkAdapters : IAdapterService
{
    public Task<List<Adapter>> ReadAsync(CancellationToken cancellationToken) => Task.Run(Read, cancellationToken);

    // Native MIB_IF_ROW2: 1352 bytes, aligned to 8 bytes. The unused tail holds counters.
    // https://learn.microsoft.com/windows/win32/api/netioapi/ns-netioapi-mib_if_row2
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Size = 1352)]
    private struct InterfaceRow
    {
        public ulong Luid;
        public uint Index;
        public Guid Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string Alias;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string Description;
        public uint AddressLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Address;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] PermanentAddress;
        public uint Mtu, Type, TunnelType, MediaType, PhysicalMediumType, AccessType, Direction;
        public byte Flags;
        public uint OperationalStatus, AdminStatus, MediaConnectState;
        public Guid NetworkGuid;
        public uint ConnectionType;
    }

    [DllImport("iphlpapi.dll")] private static extern uint GetIfTable2(out IntPtr table);
    [DllImport("iphlpapi.dll")] private static extern void FreeMibTable(IntPtr table);

    internal static List<Adapter> Read()
    {
        uint error = GetIfTable2(out IntPtr table);
        if (error != 0) throw new Win32Exception((int)error);
        try
        {
            var result = new List<Adapter>();
            int count = Marshal.ReadInt32(table);
            for (int i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<InterfaceRow>(IntPtr.Add(table, 8 + i * 1352));
                bool hardware = (row.Flags & 1) != 0;
                bool filter = (row.Flags & 2) != 0;
                if (!hardware || filter || row.OperationalStatus == 6 || row.Type is not (6 or 71)) continue;
                string state = row.AdminStatus != 1 ? "Disabled" : row.OperationalStatus == 1 ? "Connected" : "Disconnected";
                result.Add(new Adapter(row.Alias, row.Id, row.Description, state, (int)row.AdminStatus));
            }
            return result.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        finally { FreeMibTable(table); }
    }

    public async Task SetEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken)
    {
        if (IsAdministrator()) { await SetAsAdministratorAsync(id, enabled, cancellationToken); return; }
        Guid operation = Guid.NewGuid();
        string resultPath = OperationPath(operation);
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
        foreach (string arg in new[] { "--set-adapter", id.ToString("D"), enabled ? "on" : "off", operation.ToString("D") }) start.ArgumentList.Add(arg);
        try
        {
            using var helper = Process.Start(start) ?? throw new InvalidOperationException("Could not start the administrator helper.");
            await helper.WaitForExitAsync(cancellationToken);
            if (helper.ExitCode != 0)
            {
                string message = File.Exists(resultPath) ? JsonSerializer.Deserialize<string>(await File.ReadAllTextAsync(resultPath, cancellationToken)) ?? "Windows could not change the adapter." : "Windows could not change the adapter.";
                throw new InvalidOperationException(message);
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { throw new InvalidOperationException("The administrator request was canceled. The adapter was not changed."); }
        finally { try { if (File.Exists(resultPath)) File.Delete(resultPath); } catch (IOException) { } }
    }

    internal static string OperationPath(Guid operation) => Path.Combine(AppSettings.DirectoryPath, "operations", operation.ToString("D") + ".json");
    internal static bool IsAdministrator() => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    internal static async Task SetAsAdministratorAsync(Guid id, bool enabled, CancellationToken cancellationToken)
    {
        // A GUID is the only value inserted in the PowerShell command.
        string script = $"$ErrorActionPreference='Stop'; [Console]::OutputEncoding=[Text.UTF8Encoding]::new($false); $a=@(Get-NetAdapter -Physical -IncludeHidden -ErrorAction Stop | Where-Object {{ [guid]$_.InterfaceGuid -eq [guid]'{id:D}' }}); if($a.Count -ne 1){{ throw 'The selected adapter is no longer available.' }}; $a | {(enabled ? "Enable" : "Disable")}-NetAdapter -Confirm:$false -ErrorAction Stop";
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (string arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Windows PowerShell.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            if (cancellationToken.IsCancellationRequested) throw;
            throw new TimeoutException("Windows took too long to change the adapter. Refresh its status before trying again.");
        }
        await output;
        string failure = await error;
        if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(failure) ? "Windows could not change the adapter." : failure.Trim());
        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (Read().FirstOrDefault(a => a.Id == id) is { } actual && actual.Enabled == enabled) return;
            await Task.Delay(300, cancellationToken);
        }
        throw new InvalidOperationException("Windows has not confirmed the requested state. Refresh the adapter status.");
    }
}
