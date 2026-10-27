namespace Voltura.NetOn;

internal sealed class NetworkSchedule(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private bool scheduled;
    private long started;
    private TimeSpan duration;
    private TimeSpan Remaining => duration - clock.GetElapsedTime(started);
    internal static readonly (string Label, TimeSpan Delay)[] Options =
    [
        ("10 minutes", TimeSpan.FromMinutes(10)), ("30 minutes", TimeSpan.FromMinutes(30)),
        ("1 hour", TimeSpan.FromHours(1)), ("2 hours", TimeSpan.FromHours(2)),
        ("4 hours", TimeSpan.FromHours(4)), ("8 hours", TimeSpan.FromHours(8)),
        ("24 hours", TimeSpan.FromHours(24))
    ];
    internal bool Enabled { get; private set; }
    internal DateTimeOffset? Due => scheduled ? clock.GetUtcNow() + Remaining : null;

    internal void Set(bool enabled, TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(delay));
        Enabled = enabled;
        duration = delay;
        started = clock.GetTimestamp();
        scheduled = true;
    }

    internal void Cancel() => scheduled = false;

    internal bool? TakeDue()
    {
        if (!scheduled || Remaining > TimeSpan.Zero) return null;
        scheduled = false;
        return Enabled;
    }

    internal string Describe()
    {
        if (Due is not { } due) return "No scheduled change";
        TimeSpan remaining = Remaining;
        string countdown = remaining <= TimeSpan.Zero ? "due now"
            : remaining.TotalMinutes < 1 ? "in less than a minute"
            : remaining.TotalHours < 1 ? $"in {Math.Ceiling(remaining.TotalMinutes):0} minutes"
            : $"in {(int)remaining.TotalHours}h {remaining.Minutes}m";
        return $"Turn internet {(Enabled ? "on" : "off")} {countdown} ({due.ToLocalTime():t})";
    }
}
