using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Text.Json;
using WebDispositivosMqtt.Hubs;

namespace WebDispositivosMqtt.Services.Commands
{
    public class CommandRecord
    {
        public string CommandId { get; init; } = default!;
        public string Mac      { get; init; } = default!;
        public string Cmd      { get; init; } = default!;
        public DateTime SentAtUtc  { get; init; }
        public DateTime? AckedAtUtc { get; set; }
        public string? AckStatus   { get; set; }
        public string? Response    { get; set; }
        public bool IsAcked => AckedAtUtc.HasValue;
    }

    public interface ICommandAckService
    {
        string RegisterCommand(string mac, string cmd);
        Task AcknowledgeAsync(string mac, string rawPayload);
        IReadOnlyList<CommandRecord> GetPendingFor(string mac);
    }

    public class CommandAckService(IHubContext<DeviceConnectionsHub> hub, ILogger<CommandAckService> logger) : ICommandAckService
    {
        private static readonly TimeSpan CommandTtl = TimeSpan.FromMinutes(5);
        private readonly ConcurrentDictionary<string, CommandRecord> _commands = new();

        public string RegisterCommand(string mac, string cmd)
        {
            var commandId = Guid.NewGuid().ToString("N");
            _commands[commandId] = new CommandRecord
            {
                CommandId = commandId,
                Mac       = mac,
                Cmd       = cmd,
                SentAtUtc = DateTime.UtcNow
            };
            return commandId;
        }

        // Remueve control chars inválidos en JSON (< 0x20 excepto \t).
        // El ESP32 puede incluir 0x1A u otros bytes de control en el buffer de respuesta.
        private static string SanitizeForJson(string s)
            => new(s.Where(c => c >= 0x20 || c == '\t').ToArray());

        public async Task AcknowledgeAsync(string mac, string rawPayload)
        {
            string? commandId = null;
            string? status    = null;
            string? response  = null;

            try
            {
                using var doc = JsonDocument.Parse(SanitizeForJson(rawPayload));
                var root = doc.RootElement;
                if (root.TryGetProperty("commandId", out var cid))  commandId = cid.GetString();
                if (root.TryGetProperty("status",    out var st))   status    = st.GetString();
                if (root.TryGetProperty("response",  out var resp)) response  = resp.GetString();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Ack de {Mac}: JSON inválido incluso tras sanitizar. Preview: {Preview}",
                    mac, rawPayload.Length > 120 ? rawPayload[..120] : rawPayload);
                return;
            }

            if (commandId is null || !_commands.TryGetValue(commandId, out var record))
            {
                logger.LogWarning("Ack de {Mac}: commandId={CommandId} sin match ({Count} comandos activos)",
                    mac, commandId ?? "(null)", _commands.Count);
                return;
            }

            record.AckedAtUtc = DateTime.UtcNow;
            record.AckStatus  = status ?? "ok";
            record.Response   = response;

            await DeviceConnectionsHub.NotifyCommandAckedAsync(hub, record);
        }

        public IReadOnlyList<CommandRecord> GetPendingFor(string mac)
        {
            var cutoff = DateTime.UtcNow - CommandTtl;
            return _commands.Values
                .Where(c => c.Mac == mac && !c.IsAcked && c.SentAtUtc > cutoff)
                .ToList();
        }
    }
}
