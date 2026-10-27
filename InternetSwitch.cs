namespace Voltura.NetOn;

internal sealed class InternetSwitch(IAdapterService service, AppSettings preferences, Action save)
{
    internal async Task<List<Adapter>> SetEnabledAsync(bool enabled, CancellationToken token)
    {
        var before = await service.ReadAsync(token);
        if (before.Count == 0) throw new InvalidOperationException("No network adapters were found. Internet OFF cannot be confirmed.");
        List<Guid> targets;
        if (!enabled)
        {
            targets = before.Where(a => a.Enabled).Select(a => a.Id).ToList();
            preferences.RestoreAdapterIds = preferences.OffRequested
                ? preferences.RestoreAdapterIds.Union(targets).ToList()
                : targets.ToList();
            preferences.OffRequested = true;
            // Persist recovery information before changing any adapter.
            save();
        }
        else
        {
            targets = preferences.RestoreAdapterIds.Count > 0
                ? preferences.RestoreAdapterIds.ToList()
                : before.Select(a => a.Id).ToList();
        }
        await service.SetEnabledAsync(targets, enabled, token);
        var after = await service.ReadAsync(token);
        if (after.Count == 0 || (!enabled && after.Any(a => a.Enabled)) ||
            (enabled && targets.Any(id => after.FirstOrDefault(a => a.Id == id) is not { Enabled: true })))
            throw new InvalidOperationException(enabled
                ? "Windows has not confirmed that all previous connections were restored. Try ON again."
                : "Some adapters are still enabled. Internet is not OFF. Try OFF again or use ON to restore connections.");
        if (enabled)
        {
            preferences.RestoreAdapterIds = [];
            preferences.OffRequested = false;
            save();
        }
        return after;
    }
}
