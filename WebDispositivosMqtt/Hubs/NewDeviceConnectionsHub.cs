using Microsoft.AspNetCore.SignalR;
namespace WebDispositivosMqtt.Hubs
{
    public class NewDeviceConnectionsHub : Hub
    {
        // 🔹 Cuando un cliente se conecta
        public override async Task OnConnectedAsync()
        {
            var connectionId = Context.ConnectionId;

            // opcional: log
            Console.WriteLine($"NewDevicesHub: Cliente conectado: {connectionId}");

            await base.OnConnectedAsync();
        }

        // 🔹 Cuando se desconecta
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var connectionId = Context.ConnectionId;

            Console.WriteLine($"NewDevicesHub: Cliente desconectado: {connectionId}");

            await base.OnDisconnectedAsync(exception);
        }

    }


}
