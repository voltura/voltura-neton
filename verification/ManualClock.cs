internal sealed class ManualClock : TimeProvider
{
    private DateTimeOffset utcNow = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
    private long timestamp;
    public override DateTimeOffset GetUtcNow() => utcNow;
    public override long GetTimestamp() => timestamp;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    internal void Advance(TimeSpan elapsed) { timestamp += elapsed.Ticks; utcNow += elapsed; }
    internal void JumpClock(TimeSpan delta) => utcNow += delta;
}
