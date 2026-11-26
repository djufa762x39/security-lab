namespace Platform.Identity.Tests.Helpers;

/// <summary>The system clock moved by <see cref="Offset"/>, to let a lock or a deadline run out in a test.</summary>
internal sealed class ShiftedClock : TimeProvider
{
    public TimeSpan Offset { get; set; }

    public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + Offset;
}
