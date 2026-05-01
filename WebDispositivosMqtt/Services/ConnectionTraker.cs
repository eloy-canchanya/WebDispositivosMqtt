using System.Collections.Concurrent;

namespace WebDispositivosMqtt.Services
{
    public class ConnectionTracker
    {
        private readonly ConcurrentDictionary<string, byte> _connections = new();

        public int Add(string connectionId)
        {
            _connections.TryAdd(connectionId, 0);
            return _connections.Count;
        }

        public int Remove(string connectionId)
        {
            _connections.TryRemove(connectionId, out _);
            return _connections.Count;
        }

        public int Count => _connections.Count;
    }
}