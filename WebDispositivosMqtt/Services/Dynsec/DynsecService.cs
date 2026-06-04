using System.Text.Json;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;
using WebDispositivosMqtt.Services.Mqtt;

namespace WebDispositivosMqtt.Services.Dynsec;

public class DynsecService : IDynsecService
{
    private const string DynsecTopic = "$CONTROL/dynamic-security/v1";
    private const string DynsecTopicResponse = "$CONTROL/dynamic-security/v1/response";
    private readonly MqttOptions _options;
    private readonly ILogger<DynsecService> _logger;

    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public DynsecService(IOptions<MqttOptions> options, ILogger<DynsecService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> EnsureDeviceAsync(string macAddress, string plainPassword)
    {
        var payload = new
        {
            commands = new object[]
            {
                new
                {
                    command = "createClient",
                    username = macAddress,
                    password = plainPassword,
                    roles = new[] { new { rolename = "role-device", priority = -1 } }
                }
            }
        };

        await _semaphore.WaitAsync();
        try
        {
            return await PublishAndQueryAsync(payload, ParseConfirmResponse);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<bool> SetDevicePasswordAsync(string macAddress, string plainPassword)
    {
        var createPayload = new
        {
            commands = new object[]
            {
                new
                {
                    command = "createClient",
                    username = macAddress,
                    password = plainPassword,
                    roles = new[] { new { rolename = "role-device", priority = -1 } }
                }
            }
        };

        await _semaphore.WaitAsync();
        try
        {
            bool alreadyExists = await PublishAndQueryAsync(createPayload, ParseCreateOrExistsResponse);

            if (alreadyExists)
            {
                var modifyPayload = new
                {
                    commands = new object[]
                    {
                        new { command = "modifyClient", username = macAddress, password = plainPassword }
                    }
                };
                await PublishAndQueryAsync(modifyPayload, ParseConfirmResponse);
            }

            return true;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task DisableDeviceAsync(string macAddress)
    {
        var payload = new
        {
            commands = new[] { new { command = "disableClient", username = macAddress } }
        };
        await _semaphore.WaitAsync();
        try
        {
            await PublishAsync(payload);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task EnableDeviceAsync(string macAddress)
    {
        var payload = new
        {
            commands = new[] { new { command = "enableClient", username = macAddress } }
        };
        
        await _semaphore.WaitAsync();
        try
        {
            await PublishAsync(payload);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<DynsecClientInfo> GetClientStatusAsync(string macAddress)
    {
        var payload = new
        {
            commands = new[] { new { command = "getClient", username = macAddress } }
        };

        await _semaphore.WaitAsync();
        try
        {
            return await PublishAndQueryAsync(payload, ParseClientInfoResponse);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private static bool ParseCreateOrExistsResponse(JsonDocument doc)
    {
        if (!doc.RootElement.TryGetProperty("responses", out var responses))
            throw new InvalidOperationException("DynSec respuesta sin campo 'responses'");

        foreach (var response in responses.EnumerateArray())
        {
            if (response.TryGetProperty("error", out var error))
            {
                var msg = error.GetString() ?? "unknown";
                if (msg.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                    return true;
                throw new InvalidOperationException($"DynSec error: {msg}");
            }
            return false;
        }
        return false;
    }

    private static bool ParseConfirmResponse(JsonDocument doc)
    {
        if (!doc.RootElement.TryGetProperty("responses", out var responses))
            throw new InvalidOperationException("DynSec respuesta sin campo 'responses'");

        foreach (var response in responses.EnumerateArray())
        {
            if (response.TryGetProperty("error", out var error))
            {
                var msg = error.GetString() ?? "unknown";
                if (msg.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                    return true;
                throw new InvalidOperationException($"DynSec error: {msg}");
            }
            return true;
        }
        return true;
    }

    private static DynsecClientInfo ParseClientInfoResponse(JsonDocument doc)
    {
        if (!doc.RootElement.TryGetProperty("responses", out var responses))
            throw new InvalidOperationException("DynSec respuesta sin campo 'responses'");

        foreach (var response in responses.EnumerateArray())
        {
            if (response.TryGetProperty("error", out var error))
            {
                var msg = error.GetString() ?? "unknown";
                return msg.Contains("not found", StringComparison.OrdinalIgnoreCase)
                    ? new DynsecClientInfo(DynsecClientStatus.NotFound, [])
                    : new DynsecClientInfo(DynsecClientStatus.Error, [], msg);
            }

            var client = response.GetProperty("data").GetProperty("client");
            var disabled = client.TryGetProperty("disabled", out var d) && d.GetBoolean();
            var roles = client.TryGetProperty("roles", out var r)
                ? r.EnumerateArray()
                    .Select(x => x.TryGetProperty("rolename", out var rn) ? rn.GetString() ?? "" : "")
                    .Where(x => x.Length > 0)
                    .ToArray()
                : [];

            return new DynsecClientInfo(
                disabled ? DynsecClientStatus.Disabled : DynsecClientStatus.Enabled,
                roles);
        }

        throw new InvalidOperationException("DynSec respuesta vacía");
    }

    private async Task PublishAsync(object payload)
    {
        var factory = new MqttClientFactory();
        using var client = factory.CreateMqttClient();

        var options = BuildOptionsWithUniqueClientId();
        var connectResult = await client.ConnectAsync(options);

        if (!client.IsConnected)
            throw new InvalidOperationException($"Dynsec no conectado. ResultCode={connectResult.ResultCode}");

        await client.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(DynsecTopic)
            .WithPayload(JsonSerializer.Serialize(payload))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
            .Build());

        await client.DisconnectAsync();
        _logger.LogInformation("[Dynsec] Comando publicado correctamente");
    }

    private async Task<T> PublishAndQueryAsync<T>(object payload, Func<JsonDocument, T> parse)
    {
        var factory = new MqttClientFactory();
        using var client = factory.CreateMqttClient();

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        client.ApplicationMessageReceivedAsync += e =>
        {
            try
            {
                using var doc = JsonDocument.Parse(e.ApplicationMessage.ConvertPayloadToString());
                tcs.TrySetResult(parse(doc));
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
            return Task.CompletedTask;
        };

        var options = BuildOptionsWithUniqueClientId();
        await client.ConnectAsync(options);
        await client.SubscribeAsync(DynsecTopicResponse);

        await client.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(DynsecTopic)
            .WithPayload(JsonSerializer.Serialize(payload))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
            .Build());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        cts.Token.Register(() => tcs.TrySetException(new TimeoutException("DynSec no respondio")));

        try
        {
            return await tcs.Task;
        }
        finally
        {
            await client.DisconnectAsync();
        }
    }

    private MqttClientOptions BuildOptionsWithUniqueClientId()
    {
        var clientId = $"{_options.Dynsec.ClientId}-{Environment.ProcessId}-{Guid.NewGuid():N}";
        var builder = new MqttClientOptionsBuilder()
            .WithClientId(clientId)
            .WithTcpServer(_options.Host, _options.Port)
            .WithCredentials(_options.Dynsec.Username, _options.Dynsec.Password);

        if (_options.UseTls)
            builder = builder.WithTlsOptions(_ => { });

        return builder.Build();
    }
}
