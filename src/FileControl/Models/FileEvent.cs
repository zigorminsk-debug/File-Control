namespace FileControl.Models;

public sealed record FileEventRecord(
    long Id, DateTimeOffset Timestamp, string User, string Domain, string Path,
    string DocumentName, string Action, string? Process, string? Details, long EventRecordId);

public sealed class FileControlOptions
{
    public string[] MonitoredPaths { get; set; } = [@"C:\Shared"];
    public string DatabasePath { get; set; } = @"data\filecontrol.db";
    public string ReportDirectory { get; set; } = "reports";
    public int ReportHour { get; set; } = 1;
    public int RetentionDays { get; set; } = 365;
    public int OfficeCorrelationSeconds { get; set; } = 15;
    public string[] IgnoredUsers { get; set; } = ["SYSTEM", "LOCAL SERVICE", "NETWORK SERVICE"];
}
