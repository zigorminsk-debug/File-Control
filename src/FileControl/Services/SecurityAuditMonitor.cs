using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Xml.Linq;
using FileControl.Data;
using FileControl.Models;
using Microsoft.Extensions.Options;

namespace FileControl.Services;

public sealed class SecurityAuditMonitor : BackgroundService
{
    private readonly EventRepository _repository; private readonly IOptionsMonitor<FileControlOptions> _options; private readonly ILogger<SecurityAuditMonitor> _logger; private readonly MonitorDiagnostics _diagnostics;
    private EventLogWatcher? _watcher;
    private static readonly string[] OfficeExtensions = [".doc", ".docx", ".docm", ".xls", ".xlsx", ".xlsm", ".ppt", ".pptx", ".pptm", ".vsd", ".vsdx", ".pub", ".accdb"];
    public SecurityAuditMonitor(EventRepository repository, IOptionsMonitor<FileControlOptions> options, ILogger<SecurityAuditMonitor> logger, MonitorDiagnostics diagnostics) => (_repository,_options,_logger,_diagnostics)=(repository,options,logger,diagnostics);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!OperatingSystem.IsWindows()) { _logger.LogWarning("Security audit monitor is available only on Windows"); return Task.CompletedTask; }
        try
        {
            var query = new EventLogQuery("Security", PathType.LogName, "*[System[(EventID=4663)]]") { ReverseDirection = false, TolerateQueryErrors = true };
            _watcher = new EventLogWatcher(query); _watcher.EventRecordWritten += OnEvent; _watcher.Enabled = true; _diagnostics.SubscriptionActive = true; _diagnostics.LastError = null;
            _logger.LogInformation("Monitoring Windows Security event 4663 for {Paths}", string.Join(", ", _options.CurrentValue.MonitoredPaths));
            stoppingToken.Register(() => { if (_watcher != null) _watcher.Enabled=false; });
        }
        catch (Exception ex) { _diagnostics.SubscriptionActive=false; _diagnostics.LastError=ex.Message; _logger.LogCritical(ex, "Cannot read the Security log. Run the service as LocalSystem/administrator and execute Install-Auditing.ps1"); }
        return Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async void OnEvent(object? sender, EventRecordWrittenEventArgs args)
    {
        if (args.EventException != null) { _diagnostics.SubscriptionActive=false; _diagnostics.LastError=args.EventException.Message; _logger.LogError(args.EventException, "Security event subscription error"); return; }
        using var record = args.EventRecord; if (record == null) return;
        _diagnostics.ReceivedEvent();
        try
        {
            var root = XDocument.Parse(record.ToXml()).Root!; XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";
            var data = root.Descendants(ns+"Data").Where(x=>x.Attribute("Name")!=null).ToDictionary(x=>x.Attribute("Name")!.Value,x=>x.Value,StringComparer.OrdinalIgnoreCase);
            var path = Get(data,"ObjectName"); var user=Get(data,"SubjectUserName"); var domain=Get(data,"SubjectDomainName");
            if (string.IsNullOrWhiteSpace(path) || !IsMonitored(path) || _options.CurrentValue.IgnoredUsers.Contains(user,StringComparer.OrdinalIgnoreCase) || path.EndsWith("\\",StringComparison.Ordinal)) return;
            var mask = ParseMask(Get(data,"AccessMask")); var action = ActionFromMask(mask); if (action == null) return;
            // 4663 does not have a separate "created" flag. A WriteData operation on a file
            // whose creation time is effectively the event time is a reliable distinction.
            if (action == "Изменение" && File.Exists(path))
            {
                var created = File.GetCreationTimeUtc(path);
                var eventUtc = (record.TimeCreated ?? DateTime.Now).ToUniversalTime();
                if (Math.Abs((created - eventUtc).TotalSeconds) <= 5) action = "Создание";
            }
            var name=Path.GetFileName(path); var ext=Path.GetExtension(name);
            // Office and many editors generate a large number of disposable lock/cache files.
            // They are required for saving, but are noise in the user-facing audit journal.
            if (IsTemporaryFile(name, ext)) return;
            var detail = OfficeExtensions.Contains(ext,StringComparer.OrdinalIgnoreCase) ? "Документ Microsoft Office" : null;
            await _repository.AddAsync(new(0, record.TimeCreated is null ? DateTimeOffset.UtcNow : new DateTimeOffset(record.TimeCreated.Value), user, domain, path, name, action, Get(data,"ProcessName"), detail, record.RecordId ?? 0));
            _diagnostics.AcceptedEvent();
        }
        catch (Exception ex) { _logger.LogError(ex,"Failed to process security audit record {Id}",record.RecordId); }
    }
    private bool IsMonitored(string path) => _options.CurrentValue.MonitoredPaths.Any(p=>path.StartsWith(Path.GetFullPath(p).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase) || path.Equals(Path.GetFullPath(p),StringComparison.OrdinalIgnoreCase));
    private static bool IsTemporaryFile(string name, string extension) =>
        name.StartsWith("~$", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("~", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".temp", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".bak", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".swp", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".part", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".crdownload", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase);
    private static string Get(Dictionary<string,string> d,string key)=>d.TryGetValue(key,out var v)?v:"";
    private static long ParseMask(string value) { value=value.Trim(); return long.TryParse(value.StartsWith("0x")?value[2..]:value, value.StartsWith("0x")?NumberStyles.HexNumber:NumberStyles.Integer,CultureInfo.InvariantCulture,out var n)?n:0; }
    internal static string? ActionFromMask(long mask)
    {
        if ((mask & 0x10000) != 0 || (mask & 0x100) != 0) return "Удаление";
        if ((mask & 0x2) != 0 || (mask & 0x4) != 0 || (mask & 0x10) != 0) return "Изменение";
        return null; // reads are intentionally not retained
    }
    public override void Dispose() { _watcher?.Dispose(); base.Dispose(); }
}
