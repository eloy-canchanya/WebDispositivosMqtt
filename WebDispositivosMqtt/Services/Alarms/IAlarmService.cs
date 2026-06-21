namespace WebDispositivosMqtt.Services.Alarms;

public interface IAlarmService
{
    Task ProcessAsync(string mac, string payload);
}
