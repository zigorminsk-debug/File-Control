namespace FileControl.Services;

public sealed class MonitorDiagnostics
{
    private long _received;
    private long _accepted;
    public bool SubscriptionActive { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? LastSecurityEvent { get; set; }
    public DateTimeOffset? LastAcceptedEvent { get; set; }
    public long Received => Interlocked.Read(ref _received);
    public long Accepted => Interlocked.Read(ref _accepted);
    public void ReceivedEvent() { Interlocked.Increment(ref _received); LastSecurityEvent = DateTimeOffset.Now; }
    public void AcceptedEvent() { Interlocked.Increment(ref _accepted); LastAcceptedEvent = DateTimeOffset.Now; }
}
