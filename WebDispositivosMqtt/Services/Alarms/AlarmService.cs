using System.Text.Json;
using FirebaseAdmin.Messaging;
using Microsoft.EntityFrameworkCore;
using WebDispositivosMqtt.Data;
using WebDispositivosMqtt.Data.Models;

namespace WebDispositivosMqtt.Services.Alarms;

public class AlarmService(DatabaseContext db, ILogger<AlarmService> logger) : IAlarmService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task ProcessAsync(string mac, string payload)
    {
        var device = await db.Devices
            .FirstOrDefaultAsync(d => d.MacAddress == mac);

        if (device is null)
        {
            logger.LogWarning("Alarma descartada: dispositivo no encontrado para MAC {Mac}", mac);
            return;
        }

        AlarmPayload parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<AlarmPayload>(payload, JsonOptions)
                     ?? throw new InvalidOperationException("Payload nulo");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Alarma descartada: payload inválido. MAC: {Mac}, Payload: {Payload}", mac, payload);
            return;
        }

        var alarm = new Alarm
        {
            DeviceId = device.DeviceId,
            Type = parsed.Type,
            Description = parsed.Description,
            Severity = parsed.Severity,
        };
        db.Alarms.Add(alarm);
        await db.SaveChangesAsync();

        var tokens = await db.UserDevices
            .Where(ud => ud.DeviceId == device.DeviceId)
            .SelectMany(ud => ud.User.FcmTokens.Select(t => t.Token))
            .ToListAsync();

        if (tokens.Count == 0)
            return;

        await SendFcmAsync(alarm, device.Name, tokens);
    }

    private async Task SendFcmAsync(Alarm alarm, string deviceName, List<string> tokens)
    {
        var messages = tokens.Select(token => new Message
        {
            Token = token,
            Notification = new Notification
            {
                Title = $"Alarma: {deviceName}",
                Body = alarm.Description,
            },
            Data = new Dictionary<string, string>
            {
                ["alarmId"] = alarm.Id.ToString(),
                ["type"] = alarm.Type,
                ["severity"] = alarm.Severity.ToString(),
            }
        }).ToList();

        var response = await FirebaseMessaging.DefaultInstance.SendEachAsync(messages);

        if (response.FailureCount == 0)
            return;

        var invalidTokens = response.Responses
            .Select((r, i) => (r, tokens[i]))
            .Where(x => !x.r.IsSuccess && x.r.Exception?.MessagingErrorCode == MessagingErrorCode.Unregistered)
            .Select(x => x.Item2)
            .ToList();

        if (invalidTokens.Count > 0)
        {
            var toRemove = await db.FcmTokens
                .Where(t => invalidTokens.Contains(t.Token))
                .ToListAsync();
            db.FcmTokens.RemoveRange(toRemove);
            await db.SaveChangesAsync();
            logger.LogInformation("Eliminados {Count} tokens FCM inválidos", toRemove.Count);
        }
    }

    private record AlarmPayload(string Type, string Description, byte Severity);
}
