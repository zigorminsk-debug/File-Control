using System.Text.Encodings.Web;
using System.Text.Json;
using FileControl.Data;
using FileControl.Models;
using Microsoft.Extensions.Options;

namespace FileControl.Services;
public sealed class DailyReportService : BackgroundService
{
    private readonly EventRepository _repo; private readonly FileControlOptions _options; private readonly IWebHostEnvironment _env; private readonly ILogger<DailyReportService> _log;
    public DailyReportService(EventRepository repo,IOptions<FileControlOptions> options,IWebHostEnvironment env,ILogger<DailyReportService> log)=>(_repo,_options,_env,_log)=(repo,options.Value,env,log);
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        while(!token.IsCancellationRequested)
        {
            var now=DateTimeOffset.Now; var next=new DateTimeOffset(now.Year,now.Month,now.Day,Math.Clamp(_options.ReportHour,0,23),0,0,now.Offset); if(next<=now) next=next.AddDays(1);
            await Task.Delay(next-now,token); await GenerateAsync(DateOnly.FromDateTime(DateTime.Today.AddDays(-1)),token); await _repo.PurgeAsync(DateTimeOffset.UtcNow.AddDays(-_options.RetentionDays));
        }
    }
    public async Task<string> GenerateAsync(DateOnly day,CancellationToken token=default)
    {
        var from=new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue),TimeZoneInfo.Local.GetUtcOffset(day.ToDateTime(TimeOnly.MinValue))); var rows=await _repo.QueryAsync(from,from.AddDays(1),null,null,20000);
        var json=JsonSerializer.Serialize(rows,new JsonSerializerOptions{Encoder=JavaScriptEncoder.Default}); var template=await File.ReadAllTextAsync(Path.Combine(_env.WebRootPath,"report-template.html"),token);
        var dir=Path.IsPathRooted(_options.ReportDirectory)?_options.ReportDirectory:Path.Combine(_env.ContentRootPath,_options.ReportDirectory); Directory.CreateDirectory(dir);
        var path=Path.Combine(dir,$"FileControl-{day:yyyy-MM-dd}.html"); await File.WriteAllTextAsync(path,template.Replace("__REPORT_DATE__",day.ToString("yyyy-MM-dd")).Replace("__EVENT_DATA__",json),token); _log.LogInformation("Daily report created: {Path}",path); return path;
    }
}
