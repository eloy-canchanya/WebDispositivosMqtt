using Microsoft.AspNetCore.SignalR;
using WebDispositivosMqtt.Services;

namespace WebDispositivosMqtt.Hubs
{
    public class EchoHub : Hub
    {
        private readonly ConnectionTracker _connectionTracker;
        public EchoHub(ConnectionTracker connectionTracker)
        {
            _connectionTracker = connectionTracker;
        }



        public override async Task OnConnectedAsync()
        {
            var totalConexiones = _connectionTracker.Add(Context.ConnectionId);

            await Clients.Caller.SendAsync(
                "ConexionEstablecida",
                Context.ConnectionId,
                totalConexiones);

            await Clients.All.SendAsync(
                "MensajeSistema",
                $"Se ha conectado {Context.ConnectionId}. Total conectados: {totalConexiones}");

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var totalConexiones = _connectionTracker.Remove(Context.ConnectionId);

            await Clients.All.SendAsync(
                "MensajeSistema",
                $"Se ha desconectado {Context.ConnectionId}. Total conectados: {totalConexiones}");

            await Clients.All.SendAsync("ActualizarTotalConexiones", totalConexiones);

            await base.OnDisconnectedAsync(exception);
        }

        public async Task EnviarMensaje(string usuario, string mensaje)
        {
            var fecha = DateTime.Now.ToString("HH:mm:ss");

            await Clients.All.SendAsync(
                "RecibirMensaje",
                usuario,
                mensaje,
                fecha,
                _connectionTracker.Count);
        }


    }
}
