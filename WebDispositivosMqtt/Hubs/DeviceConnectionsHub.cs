using Microsoft.AspNetCore.SignalR;
namespace WebDispositivosMqtt.Hubs
{
    public class DeviceConnectionsHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            var connectionId = Context.ConnectionId;

            Console.WriteLine($"Cliente de Devices conectado: {connectionId}");

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var connectionId = Context.ConnectionId;

            Console.WriteLine($"Cliente desconectado: {connectionId}");

            await base.OnDisconnectedAsync(exception);
        }

    }

}
