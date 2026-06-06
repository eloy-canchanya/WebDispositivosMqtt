using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;
using System.Text;
using System.Text.Json;

namespace WebDispositivosMqtt.Services.Mqtt
{
    public interface IMqttPublisherService
    {
        Task PublishCommandAsync(string mac, string commandId, string cmd);
    }

    public class MqttPublisherService(IOptions<MqttOptions> options, ILogger<MqttPublisherService> logger)
        : IHostedService, IMqttPublisherService
    {
        private readonly MqttOptions _options = options.Value;
        private IMqttClient? _client;
        private readonly SemaphoreSlim _connectLock = new(1, 1);

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _client = new MqttClientFactory().CreateMqttClient();
            try
            {
                await ConnectAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Publisher MQTT: no se pudo conectar al inicio, se reintentará al primer comando");
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_client?.IsConnected == true)
                await _client.DisconnectAsync(cancellationToken: cancellationToken);
        }

        public async Task PublishCommandAsync(string mac, string commandId, string cmd)
        {
            await EnsureConnectedAsync();

            var topic    = _options.PublishTopicTemplates.Commands.Replace("{deviceId}", mac);
            var envelope = new { commandId, cmd };
            var json     = JsonSerializer.Serialize(envelope);

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(Encoding.UTF8.GetBytes(json))
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

            await _client!.PublishAsync(message);
            logger.LogInformation("Comando MQTT publicado. Topic: {Topic} Cmd: {Cmd}", topic, cmd);
        }

        private async Task EnsureConnectedAsync()
        {
            if (_client?.IsConnected == true) return;
            await _connectLock.WaitAsync();
            try
            {
                if (_client?.IsConnected == true) return;
                await ConnectAsync(CancellationToken.None);
            }
            finally
            {
                _connectLock.Release();
            }
        }

        private Task ConnectAsync(CancellationToken ct)
        {
            var builder = new MqttClientOptionsBuilder()
                .WithTcpServer(_options.Host, _options.Port)
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(_options.KeepAliveSeconds))
                .WithClientId(_options.Publisher.ClientId)
                .WithCredentials(_options.Publisher.Username, _options.Publisher.Password);

            if (_options.UseTls)
                builder = builder.WithTlsOptions(_ => { });

            return _client!.ConnectAsync(builder.Build(), ct);
        }
    }
}
