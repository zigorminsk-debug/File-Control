using System.Net;
using System.Net.Mail;
using System.Threading.Channels;
using FileControl.Models;
using Microsoft.Extensions.Options;

namespace FileControl.Services;

public sealed class AnomalyAlertService : BackgroundService
{
    private sealed record Activity(DateTimeOffset Time,string User,string Domain,string Action,string Path);
    private readonly Channel<Activity> _channel=Channel.CreateUnbounded<Activity>();
    private readonly IOptionsMonitor<FileControlOptions> _options;
    private readonly ILogger<AnomalyAlertService> _logger;
    private readonly Dictionary<string,List<Activity>> _activity=new();
    private readonly Dictionary<string,DateTimeOffset> _lastAlert=new();

    public AnomalyAlertService(IOptionsMonitor<FileControlOptions> options,ILogger<AnomalyAlertService> logger)=>(_options,_logger)=(options,logger);
    public void Record(DateTimeOffset time,string user,string domain,string action,string path)
    { if(action is "Удаление" or "Изменение") _channel.Writer.TryWrite(new(time,user,domain,action,path)); }

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        await foreach(var item in _channel.Reader.ReadAllAsync(token))
        {
            var cfg=_options.CurrentValue.EmailAlerts; if(!cfg.Enabled) continue;
            var key=$"{item.Domain}\\{item.User}|{item.Action}"; var cutoff=item.Time.AddMinutes(-Math.Clamp(cfg.WindowMinutes,1,1440));
            if(!_activity.TryGetValue(key,out var list)) _activity[key]=list=[];
            list.RemoveAll(x=>x.Time<cutoff); list.Add(item);
            var distinct=list.Select(x=>x.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if(distinct.Count<Math.Clamp(cfg.Threshold,2,100000)) continue;
            if(_lastAlert.TryGetValue(key,out var last)&&item.Time-last<TimeSpan.FromMinutes(Math.Clamp(cfg.CooldownMinutes,1,1440))) continue;
            _lastAlert[key]=item.Time;
            await SendAsync(cfg,item,distinct,token);
        }
    }

    private async Task SendAsync(EmailAlertOptions cfg,Activity item,List<string> paths,CancellationToken token)
    {
        try
        {
            using var message=new MailMessage { From=new MailAddress(string.IsNullOrWhiteSpace(cfg.From)?cfg.Username:cfg.From), Subject=$"File Control: массовое {item.Action.ToLowerInvariant()} файлов", Body=$"Пользователь: {item.Domain}\\{item.User}\r\nОперация: {item.Action}\r\nФайлов за {cfg.WindowMinutes} мин.: {paths.Count}\r\nВремя: {item.Time:dd.MM.yyyy HH:mm:ss}\r\n\r\n"+string.Join("\r\n",paths.Take(100)) };
            foreach(var recipient in cfg.Recipients.Where(x=>!string.IsNullOrWhiteSpace(x))) message.To.Add(recipient.Trim());
            using var smtp=new SmtpClient(cfg.SmtpHost,cfg.SmtpPort) { EnableSsl=cfg.UseSsl,Credentials=string.IsNullOrWhiteSpace(cfg.Username)?CredentialCache.DefaultNetworkCredentials:new NetworkCredential(cfg.Username,cfg.Password) };
            await smtp.SendMailAsync(message,token); _logger.LogWarning("Mass file activity email sent for {User}: {Count} {Action}",item.User,paths.Count,item.Action);
        }
        catch(Exception ex) { _logger.LogError(ex,"Failed to send mass file activity email"); }
    }
}
