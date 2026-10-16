using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using FileControl.Models;
using Microsoft.Extensions.Options;

namespace FileControl.Services;

public sealed record SettingsRequest(string[] MonitoredPaths, int ReportHour, int RetentionDays);

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
            var quoted = string.Join(",", paths.Select(p => "'" + p.Replace("'", "''") + "'"));
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -Path {quoted}",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process == null) return false;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not apply file audit policy"); return false; }
    }
}
