using FileControl.Data;
using FileControl.Models;
using FileControl.Services;

var builder=WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(o=>o.ServiceName="FileControl");
builder.Services.Configure<FileControlOptions>(builder.Configuration.GetSection("FileControl"));
builder.Services.AddSingleton<EventRepository>(); builder.Services.AddSingleton<DailyReportService>();
builder.Services.AddHostedService<SecurityAuditMonitor>(); builder.Services.AddHostedService(sp=>sp.GetRequiredService<DailyReportService>());
var app=builder.Build(); await app.Services.GetRequiredService<EventRepository>().InitializeAsync();
app.UseDefaultFiles(); app.UseStaticFiles();
app.MapGet("/api/events",async(DateTimeOffset? from,DateTimeOffset? to,string? user,string? document,int? limit,EventRepository repo)=>Results.Ok(await repo.QueryAsync(from??DateTimeOffset.Now.AddDays(-1),to??DateTimeOffset.Now.AddDays(1),user,document,limit??5000)));
app.MapPost("/api/reports/{date}",async(string date,DailyReportService svc)=>DateOnly.TryParse(date,out var day)?Results.Ok(new{path=await svc.GenerateAsync(day)}):Results.BadRequest("Дата должна быть в формате YYYY-MM-DD"));
app.MapGet("/api/health",()=>Results.Ok(new{status="ok",time=DateTimeOffset.Now}));
app.Run();
