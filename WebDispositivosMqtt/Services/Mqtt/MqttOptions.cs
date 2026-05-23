namespace WebDispositivosMqtt.Services.Mqtt
{

    public class MqttOptions
    {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 1883;
        public bool UseTls { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string ClientId { get; set; } = "web-dispositivos-mqtt";
        public string[] SubscribeTopics { get; set; } = [];
        public PublishTopicTemplates PublishTopicTemplates { get; set; } = new PublishTopicTemplates();
    }

    public class PublishTopicTemplates
    {
        public string Commands { get; set; } = string.Empty;
        public string Config { get; set; } = string.Empty;
    }

}
