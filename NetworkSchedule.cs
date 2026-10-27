namespace Voltura.NetOn;

internal sealed class NetworkSchedule
{
    internal static readonly (string Label, TimeSpan Delay)[] Options =
    [
        ("10 minutes", TimeSpan.FromMinutes(10)), ("30 minutes", TimeSpan.FromMinutes(30)),
        ("1 hour", TimeSpan.FromHours(1)), ("2 hours", TimeSpan.FromHours(2)),
        ("4 hours", TimeSpan.FromHours(4)), ("8 hours", TimeSpan.FromHours(8)),
        ("24 hours", TimeSpan.FromHours(24))
    ];
    internal bool Enabled { get; private set; }
    internal DateTimeOffset? Due { get; private set; }

    internal void Set(bool enabled, TimeSpan delay, DateTimeOffset now)
    {
        if (delay <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(delay));
        Enabled = enabled;
        Due = now + delay;
    }

    internal void Cancel() => Due = null;

    internal bool? TakeDue(DateTimeOffset now)
    {
        if (Due is null || now < Due) return null;
        Due = null;
        return Enabled;
    }

    internal string Describe(DateTimeOffset now)
    {
        if (Due is not { } due) return "No scheduled change";
        TimeSpan remaining = due - now;
        string countdown = remaining <= TimeSpan.Zero ? "due now"
            : remaining.TotalMinutes < 1 ? "in less than a minute"
            : remaining.TotalHours < 1 ? $"in {Math.Ceiling(remaining.TotalMinutes):0} minutes"
            : $"in {(int)remaining.TotalHours}h {remaining.Minutes}m";
        return $"Turn internet {(Enabled ? "on" : "off")} {countdown} ({due.ToLocalTime():t})";
    }
}
