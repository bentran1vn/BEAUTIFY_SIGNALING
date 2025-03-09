using System.Collections.Concurrent;
using System.Net.WebSockets;
using Newtonsoft.Json.Linq;
using Websocket.Client;

namespace BEAUTIFY_SIGNALING.SERVICES.LiveStream;

public class JanusWebSocketManager : IDisposable
{
    private readonly WebsocketClient _wsClient;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JObject>> _transactions;
    private long? _sessionId;
    private readonly object _lock = new();

    public JanusWebSocketManager(string janusWebSocketUrl)
    {
        _transactions = new ConcurrentDictionary<string, TaskCompletionSource<JObject>>();
        var factory = new Func<ClientWebSocket>(() =>
        {
            var clientWebSocket = new ClientWebSocket();
            clientWebSocket.Options.AddSubProtocol("janus-protocol");
            return clientWebSocket;
        });

        _wsClient = new WebsocketClient(new Uri(janusWebSocketUrl), factory);
        _wsClient.MessageReceived.Subscribe(OnMessageReceived);
        _wsClient.Start().Wait();
    }

    private void OnMessageReceived(ResponseMessage message)
    {
        var response = JObject.Parse(message.Text);
        var transaction = response["transaction"]?.ToString();
        if (transaction != null && _transactions.TryRemove(transaction, out var tcs))
        {
            tcs.SetResult(response);
        }
    }

    public async Task<JObject> SendAsync(JObject request)
    {
        var transactionId = Guid.NewGuid().ToString();
        request["transaction"] = transactionId;

        var tcs = new TaskCompletionSource<JObject>();
        _transactions[transactionId] = tcs;

        await _wsClient.SendInstant(request.ToString());
        return await tcs.Task;
    }
    
    public async Task<long?> CreateSessionAsync()
    {
        var response = await SendAsync(new JObject { ["janus"] = "create" });

        if (response["janus"]?.ToString() == "success")
            return response["data"]["id"].Value<long>();

        return null;
    }

    public async Task<long?> AttachPluginAsync(long sessionId, string pluginName)
    {
        var response = await SendAsync(new JObject
        {
            ["janus"] = "attach",
            ["plugin"] = pluginName,
            ["session_id"] = sessionId
        });

        if (response["janus"]?.ToString() == "success")
            return response["data"]["id"].ToObject<long>();

        return null;
    }

    public void Dispose()
    {
        _wsClient?.Stop(WebSocketCloseStatus.NormalClosure, "Disposed");
        _wsClient?.Dispose();
    }
}