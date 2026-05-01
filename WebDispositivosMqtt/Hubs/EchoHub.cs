using Microsoft.AspNetCore.SignalR;

namespace WebDispositivosMqtt.Hubs
{
    public class EchoHub : Hub
    {
        public async Task EnviarMensaje(string usuario, string mensaje)
        {
            var fecha = DateTime.Now.ToString("HH:mm:ss");
            await Clients.All.SendAsync("RecibirMensaje", usuario, mensaje, fecha);
        }
    }
}
