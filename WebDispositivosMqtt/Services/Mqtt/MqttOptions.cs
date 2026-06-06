namespace WebDispositivosMqtt.Services.Mqtt
{
    public class MqttOptions
    {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 1883;
        public bool UseTls { get; set; }
        public MqttCredentials Listener { get; set; } = new();
        public MqttCredentials Publisher { get; set; } = new();
        public MqttCredentials Dynsec { get; set; } = new();
        public int KeepAliveSeconds { get; set; } = 30;
        public string[] SubscribeTopics { get; set; } = [];
        public PublishTopicTemplates PublishTopicTemplates { get; set; } = new();
    }

    public class MqttCredentials
    {
        public string ClientId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class PublishTopicTemplates
    {
        public string Commands { get; set; } = string.Empty;
        public string Config { get; set; } = string.Empty;
    }
}
