using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;
using WebDispositivosMqtt.Services.Alarms;
using WebDispositivosMqtt.Services.Commands;
using WebDispositivosMqtt.Services.Devices;
using WebDispositivosMqtt.Services.Telemetria;
using WebDispositivosMqtt.Utils;

namespace WebDispositivosMqtt.Services.Mqtt
{
    public class MqttListenerService : BackgroundService
    {
        private readonly MqttOptions _options;
        private readonly ILogger<MqttListenerService> _logger;
        private IMqttClient? _mqttClient;
        private readonly IDeviceConnectionService _deviceConnectionService;
        private readonly ICommandAckService _commandAckService;
        private readonly IServiceScopeFactory _scopeFactory;
        private int _reconnecting = 0;

        public MqttListenerService(
            IOptions<MqttOptions> options,
            ILogger<MqttListenerService> logger,
            IDeviceConnectionService devConnService,
            ICommandAckService commandAckService,
            IServiceScopeFactory scopeFactory)
        {
            _options = options.Value;
            _logger = logger;
            _deviceConnectionService = devConnService;
            _commandAckService = commandAckService;
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var factory = new MqttClientFactory();
            _mqttClient = factory.CreateMqttClient();


            _mqttClient.ApplicationMessageReceivedAsync += async eventArgs =>
            {
                var topic = eventArgs.ApplicationMessage.Topic;
                var payload = eventArgs.ApplicationMessage.ConvertPayloadToString() ?? string.Empty;

                var segments = topic.Split('/', StringSplitOptions.RemoveEmptyEntries);

                if (segments.Length is < 3 or > 4)
                {
                    return;
                }

                string domain = segments[0];
                string entityId = segments[1];
                string resource = segments[2];
                string subtype = segments.Length > 3 ? segments[3] : string.Empty;


                if (domain != "devices")
                {
                    return;
                }

                if (!DeviceMac.IsValid(entityId))
                {
                    _logger.LogWarning("Topic descartado por MAC inválida. Topic: {Topic}", topic);
                    return;
                }

                if (resource == "commands" && subtype == "ack")
                {
                    await _commandAckService.AcknowledgeAsync(entityId, payload);
                }
                else if (resource == "tel")
                {
                    using var scope = _scopeFactory.CreateScope();
                    var telemetriaService = scope.ServiceProvider.GetRequiredService<ITelemetriaService>();
                    await telemetriaService.ProcesarAsync(entityId, topic, payload);
                }
                else if (resource == "alarm")
                {
                    using var scope = _scopeFactory.CreateScope();
                    var alarmService = scope.ServiceProvider.GetRequiredService<IAlarmService>();
                    await alarmService.ProcessAsync(entityId, payload);
                }
                else
                {
                    await _deviceConnectionService.EvaluateTopicAsync(domain, entityId, resource, subtype, payload);
                }

                _logger.LogInformation("MQTT recibido. Topic: {Topic} Payload: {Payload}", topic, payload);
            };

            _mqttClient.ConnectedAsync += async _ =>
            {
                _logger.LogInformation("Conectado al broker MQTT {Host}:{Port}", _options.Host, _options.Port);

                foreach (var topic in _options.SubscribeTopics)
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

                if (Interlocked.CompareExchange(ref _reconnecting, 1, 0) != 0)
                    return;

                try
                {
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        try
                        {
                            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                            await _mqttClient.ConnectAsync(BuildOptions(), stoppingToken);
                            return;
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Reintento de conexión MQTT fallido");
                        }
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref _reconnecting, 0);
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
                .WithTcpServer(_options.Host, _options.Port)
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(_options.KeepAliveSeconds))
                .WithClientId(_options.Listener.ClientId)
                .WithCredentials(_options.Listener.Username, _options.Listener.Password);

            if (_options.UseTls)
                builder = builder.WithTlsOptions(_ => { });

            return builder.Build();
        }


    }
}
