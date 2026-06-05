using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WebDispositivosMqtt.Data;
using WebDispositivosMqtt.Services.Commands;
using WebDispositivosMqtt.Services.Devices;

namespace WebDispositivosMqtt.Hubs
{
    [Authorize]
    public class DeviceConnectionsHub(
        IDeviceConnectionService deviceConnectionService,
        DatabaseContext db) : Hub
    {
        private const string AdminGroup = "Admins";

        public override async Task OnConnectedAsync()
        {
            var isAdmin = Context.User?.IsInRole("Admin") ?? false;

            if (isAdmin)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, AdminGroup);
                await Clients.Caller.SendAsync("EstadoInicial", deviceConnectionService.GetAll());
            }
            else
            {
                var userId = Context.UserIdentifier;
                var userMacs = await db.UserDevices
                    .Where(ud => ud.UserId == userId)
                    .Select(ud => ud.Device.MacAddress)
                    .ToHashSetAsync();

                foreach (var mac in userMacs)
                    await Groups.AddToGroupAsync(Context.ConnectionId, mac);

                var filtered = deviceConnectionService.GetAll()
                    .Where(d => userMacs.Contains(d.MacAddress))
                    .ToList();

                await Clients.Caller.SendAsync("EstadoInicial", filtered);
            }

            await base.OnConnectedAsync();
        }

        public static Task NotifyStatusChangedAsync(IHubContext<DeviceConnectionsHub> hub, string macAddress, bool isOnline, DateTime changedAtUtc, LastSeenType? lastSeenType)
            => hub.Clients.Groups([macAddress, AdminGroup]).SendAsync("EstadoDispositivoCambiado", new
            {
                macAddress,
                isOnline,
                changedAtUtc,
                lastSeenType = lastSeenType?.ToString()
            });

        public static Task NotifyDeviceExpiredAsync(IHubContext<DeviceConnectionsHub> hub, string macAddress)
            => hub.Clients.Groups([macAddress, AdminGroup]).SendAsync("DispositivoExpirado", new { macAddress });

        public static Task NotifyCommandAckedAsync(IHubContext<DeviceConnectionsHub> hub, CommandRecord record)
            => hub.Clients.Groups([record.Mac, AdminGroup]).SendAsync("ComandoAcknowledged", new
            {
                record.CommandId,
                record.Mac,
                record.Cmd,
                record.AckStatus,
                record.AckedAtUtc,
                record.Response
            });
    }
}
