using System.Diagnostics;
using FileControl.Data;
using FileControl.Models;
using FileControl.Services;
using Microsoft.Extensions.Hosting.WindowsServices;

var builder=WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(o=>o.ServiceName="FileControl");
builder.Services.Configure<FileControlOptions>(builder.Configuration.GetSection("FileControl"));
builder.Services.AddSingleton<EventRepository>();
builder.Services.AddSingleton<DailyReportService>();
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddHostedService<SecurityAuditMonitor>();
builder.Services.AddHostedService(sp=>sp.GetRequiredService<DailyReportService>());
var app=builder.Build();
await app.Services.GetRequiredService<EventRepository>().InitializeAsync();
app.UseDefaultFiles(); app.UseStaticFiles();
app.MapGet("/api/events",async(DateTimeOffset? from,DateTimeOffset? to,string? user,string? document,int? limit,EventRepository repo)=>Results.Ok(await repo.QueryAsync(from??DateTimeOffset.Now.AddDays(-1),to??DateTimeOffset.Now.AddDays(1),user,document,limit??5000)));
app.MapGet("/api/settings",(SettingsService settings)=>Results.Ok(settings.Current()));
app.MapPut("/api/settings",async(SettingsRequest request,SettingsService settings)=>{var result=await settings.SaveAsync(request);return result.Success?Results.Ok(new{message=result.Message}):Results.BadRequest(new{message=result.Message});});
app.MapPost("/api/reports/{date}",async(string date,DailyReportService svc)=>DateOnly.TryParse(date,out var day)?Results.Ok(new{path=await svc.GenerateAsync(day)}):Results.BadRequest("Дата должна быть в формате YYYY-MM-DD"));
app.MapGet("/api/health",()=>Results.Ok(new{status="ok",time=DateTimeOffset.Now}));

// When launched by double-click, open the HTML GUI. A real Windows service must not open a browser.
if (OperatingSystem.IsWindows() && Environment.UserInteractive && !WindowsServiceHelpers.IsWindowsService())
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        try { Process.Start(new ProcessStartInfo("http://127.0.0.1:8787") { UseShellExecute=true }); }
        catch { /* The URL remains available in the console output. */ }
    });
}
app.Run();
