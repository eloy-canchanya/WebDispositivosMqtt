using Microsoft.AspNetCore.SignalR;
namespace WebDispositivosMqtt.Hubs
{
    public class NewDeviceHub : Hub
    {
        // 🔹 Cuando un cliente se conecta
        public override async Task OnConnectedAsync()
        {
            var connectionId = Context.ConnectionId;

            // opcional: log
            Console.WriteLine($"Cliente conectado: {connectionId}");

            await base.OnConnectedAsync();
        }

        // 🔹 Cuando se desconecta
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var connectionId = Context.ConnectionId;

            Console.WriteLine($"Cliente desconectado: {connectionId}");

            await base.OnDisconnectedAsync(exception);
        }

        // 🔹 (Opcional) Unirse a grupo (ej: tenant)
        public async Task JoinTenantGroup(string tenant)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, tenant);
        }

        // 🔹 (Opcional) Salir de grupo
        public async Task LeaveTenantGroup(string tenant)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, tenant);
        }


    }


}
