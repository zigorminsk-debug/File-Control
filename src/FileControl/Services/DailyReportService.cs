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
        var from=new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue),TimeZoneInfo.Local.GetUtcOffset(day.ToDateTime(TimeOnly.MinValue)));
        return await GenerateFilteredAsync(from,from.AddDays(1),null,null,null,$"{day:yyyy-MM-dd}",token);
    }

    public async Task<string> GenerateFilteredAsync(DateTimeOffset from,DateTimeOffset to,string? user,string? document,string? action,string? label=null,CancellationToken token=default)
    {
        var rows=await _repo.QueryAsync(from,to,user,document,20000,action);
        // Match ASP.NET's camelCase JSON used by the dashboard. The standalone
        // report template reads timestamp, user, action, documentName and path.
        var json=JsonSerializer.Serialize(rows,new JsonSerializerOptions{Encoder=JavaScriptEncoder.Default,PropertyNamingPolicy=JsonNamingPolicy.CamelCase});
        var template=await File.ReadAllTextAsync(Path.Combine(_env.WebRootPath,"report-template.html"),token);
        var dir=Path.IsPathRooted(_options.ReportDirectory)?_options.ReportDirectory:Path.Combine(_env.ContentRootPath,_options.ReportDirectory); Directory.CreateDirectory(dir);
        label??=$"{from:dd.MM.yyyy HH:mm} — {to:dd.MM.yyyy HH:mm}";
        var path=Path.Combine(dir,$"FileControl-report-{DateTime.Now:yyyyMMdd-HHmmss-fff}.html");
        await File.WriteAllTextAsync(path,template.Replace("__REPORT_DATE__",HtmlEncoder.Default.Encode(label)).Replace("__EVENT_DATA__",json),token);
        _log.LogInformation("HTML report created: {Path}; events: {Count}",path,rows.Count); return path;
    }
}
