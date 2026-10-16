using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using FileControl.Models;
using Microsoft.Extensions.Options;

namespace FileControl.Services;

public sealed record SettingsRequest(string[] MonitoredPaths, int ReportHour, int RetentionDays, bool EmailEnabled=false, int EmailThreshold=10, int EmailWindowMinutes=2, int EmailCooldownMinutes=30, string? SmtpHost=null, int SmtpPort=587, bool SmtpSsl=true, string? SmtpUsername=null, string? SmtpPassword=null, string? EmailFrom=null, string[]? EmailRecipients=null);

public sealed class SettingsService
{
    private readonly IWebHostEnvironment _environment;
    private readonly IOptionsMonitor<FileControlOptions> _options;
    private readonly ILogger<SettingsService> _logger;

    public SettingsService(IWebHostEnvironment environment, IOptionsMonitor<FileControlOptions> options, ILogger<SettingsService> logger)
        => (_environment, _options, _logger) = (environment, options, logger);

    public object Current() => new
    {
        monitoredPaths = _options.CurrentValue.MonitoredPaths,
        reportHour = _options.CurrentValue.ReportHour,
        retentionDays = _options.CurrentValue.RetentionDays,
        emailEnabled = _options.CurrentValue.EmailAlerts.Enabled,
        emailThreshold = _options.CurrentValue.EmailAlerts.Threshold,
        emailWindowMinutes = _options.CurrentValue.EmailAlerts.WindowMinutes,
        emailCooldownMinutes = _options.CurrentValue.EmailAlerts.CooldownMinutes,
        smtpHost = _options.CurrentValue.EmailAlerts.SmtpHost,
        smtpPort = _options.CurrentValue.EmailAlerts.SmtpPort,
        smtpSsl = _options.CurrentValue.EmailAlerts.UseSsl,
        smtpUsername = _options.CurrentValue.EmailAlerts.Username,
        hasSmtpPassword = !string.IsNullOrEmpty(_options.CurrentValue.EmailAlerts.Password),
        emailFrom = _options.CurrentValue.EmailAlerts.From,
        emailRecipients = _options.CurrentValue.EmailAlerts.Recipients,
        drives = DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => new
        {
            path = d.RootDirectory.FullName,
            label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? d.Name : $"{d.Name} ({d.VolumeLabel})",
            type = d.DriveType.ToString()
        })
    };

    public async Task<(bool Success, string Message)> SaveAsync(SettingsRequest request)
    {
        var paths = request.MonitoredPaths.Select(p => p.Trim()).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Length == 0) return (false, "Выберите хотя бы один диск или каталог.");
        foreach (var path in paths)
            if (!Path.IsPathRooted(path) || !Directory.Exists(path)) return (false, $"Каталог не найден: {path}");

        var configPath = Path.Combine(_environment.ContentRootPath, "appsettings.json");
        var root = JsonNode.Parse(await File.ReadAllTextAsync(configPath))!.AsObject();
        var section = root["FileControl"]!.AsObject();
        section["MonitoredPaths"] = JsonSerializer.SerializeToNode(paths);
        section["ReportHour"] = Math.Clamp(request.ReportHour, 0, 23);
        section["RetentionDays"] = Math.Clamp(request.RetentionDays, 1, 3650);
        var oldPassword=section["EmailAlerts"]?["Password"]?.GetValue<string>() ?? "";
        section["EmailAlerts"] = JsonSerializer.SerializeToNode(new EmailAlertOptions {
            Enabled=request.EmailEnabled, Threshold=Math.Clamp(request.EmailThreshold,2,100000), WindowMinutes=Math.Clamp(request.EmailWindowMinutes,1,1440), CooldownMinutes=Math.Clamp(request.EmailCooldownMinutes,1,1440),
            SmtpHost=request.SmtpHost?.Trim()??"", SmtpPort=Math.Clamp(request.SmtpPort,1,65535), UseSsl=request.SmtpSsl, Username=request.SmtpUsername?.Trim()??"", Password=string.IsNullOrEmpty(request.SmtpPassword)?oldPassword:request.SmtpPassword,
            From=request.EmailFrom?.Trim()??"", Recipients=request.EmailRecipients?.Where(x=>!string.IsNullOrWhiteSpace(x)).Select(x=>x.Trim()).ToArray()??[] });
        await File.WriteAllTextAsync(configPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var audit = await ApplyAuditAsync(paths);
        return audit ? (true, "Настройки сохранены. Аудит Windows применён.")
                     : (true, "Настройки сохранены, но аудит Windows применить не удалось. Запустите программу от администратора или Install-Auditing.ps1.");
    }

    private async Task<bool> ApplyAuditAsync(string[] paths)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            var script = Path.Combine(_environment.ContentRootPath, "scripts", "Install-Auditing.ps1");
            if (!File.Exists(script)) return false;
            foreach (var path in paths)
            {
                var start = new ProcessStartInfo { FileName = "powershell.exe", UseShellExecute = false, CreateNoWindow = true };
                start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-ExecutionPolicy"); start.ArgumentList.Add("Bypass");
                start.ArgumentList.Add("-File"); start.ArgumentList.Add(script); start.ArgumentList.Add("-Path"); start.ArgumentList.Add(path);
                using var process = Process.Start(start);
                if (process == null) return false;
                await process.WaitForExitAsync();
                if (process.ExitCode != 0) return false;
            }
            return true;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not apply file audit policy"); return false; }
    }
}
