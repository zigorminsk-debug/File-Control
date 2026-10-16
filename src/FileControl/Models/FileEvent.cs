namespace FileControl.Models;

public sealed record FileEventRecord(
    long Id, DateTimeOffset Timestamp, string User, string Domain, string Path,
    string DocumentName, string Action, string? Process, string? Details, long EventRecordId);

public sealed class FileControlOptions
{
    public string[] MonitoredPaths { get; set; } = [];
    public string DatabasePath { get; set; } = @"data\filecontrol.db";
    public string ReportDirectory { get; set; } = "reports";
    public int ReportHour { get; set; } = 1;
    public int RetentionDays { get; set; } = 365;
    public int OfficeCorrelationSeconds { get; set; } = 15;
    public string[] IgnoredUsers { get; set; } = ["SYSTEM", "LOCAL SERVICE", "NETWORK SERVICE"];
    public EmailAlertOptions EmailAlerts { get; set; } = new();
}

public sealed class EmailAlertOptions
{
    public bool Enabled { get; set; }
    public int Threshold { get; set; } = 10;
    public int WindowMinutes { get; set; } = 2;
    public int CooldownMinutes { get; set; } = 30;
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string From { get; set; } = "";
    public string[] Recipients { get; set; } = [];
}
