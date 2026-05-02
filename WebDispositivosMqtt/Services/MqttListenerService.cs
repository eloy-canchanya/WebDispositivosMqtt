using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;
using WebDispositivosMqtt.Hubs;

namespace WebDispositivosMqtt.Services
{
    public class MqttListenerService : BackgroundService
    {
        private readonly MqttOptions _options;
        private readonly IHubContext<EchoHub> _hubContext;
        private readonly ILogger<MqttListenerService> _logger;
        private IMqttClient? _mqttClient;

        public MqttListenerService(
            IOptions<MqttOptions> options,
            IHubContext<EchoHub> hubContext,
            ILogger<MqttListenerService> logger)
        {
            _options = options.Value;
            _hubContext = hubContext;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var factory = new MqttClientFactory();
            _mqttClient = factory.CreateMqttClient();

            _mqttClient.ApplicationMessageReceivedAsync += async eventArgs =>
            {
                var topic = eventArgs.ApplicationMessage.Topic;
                var payload = eventArgs.ApplicationMessage.ConvertPayloadToString() ?? string.Empty;
                var fecha = DateTime.Now.ToString("HH:mm:ss");

                _logger.LogInformation("MQTT recibido. Topic: {Topic} Payload: {Payload}", topic, payload);

                await _hubContext.Clients.All.SendAsync(
                    "RecibirMensajeMqtt",
                    topic,
                    payload,
                    fecha,
                    cancellationToken: stoppingToken);
            };

            _mqttClient.ConnectedAsync += async _ =>
            {
                _logger.LogInformation("Conectado al broker MQTT {Host}:{Port}", _options.Host, _options.Port);

                foreach (var topic in _options.Topics)
                {
                    await _mqttClient.SubscribeAsync(
                        new MqttTopicFilterBuilder()
                            .WithTopic(topic)
                            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
                            .Build(),
                        stoppingToken);

                    _logger.LogInformation("Suscrito a topic {Topic}", topic);
                }
            };

            _mqttClient.DisconnectedAsync += async _ =>
            {
                _logger.LogWarning("Desconectado del broker MQTT");

                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                        await _mqttClient.ConnectAsync(BuildOptions(), stoppingToken);
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Reintento de conexión MQTT fallido");
                    }
                }
            };

            try
            {
                await _mqttClient.ConnectAsync(BuildOptions(), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "No se pudo conectar al broker MQTT");
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_mqttClient is not null && _mqttClient.IsConnected)
            {
                await _mqttClient.DisconnectAsync(cancellationToken: cancellationToken);
            }

            await base.StopAsync(cancellationToken);
        }

        private MqttClientOptions BuildOptions()
        {
            var builder = new MqttClientOptionsBuilder()
                .WithClientId(_options.ClientId)
                .WithTcpServer(_options.Host, _options.Port);

            if (!string.IsNullOrWhiteSpace(_options.Username))
            {
                builder = builder.WithCredentials(_options.Username, _options.Password);
            }

            if (_options.UseTls)
            {
                builder = builder.WithTlsOptions(_ => { });
            }

            return builder.Build();
        }
    }
}
