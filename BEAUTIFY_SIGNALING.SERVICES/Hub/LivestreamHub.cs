using System.Collections.Concurrent;
using System.Text;
using BEAUTIFY_SIGNALING.SERVICES.LiveStream;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace BEAUTIFY_SIGNALING.SERVICES.Hub;

public class LivestreamHub : Microsoft.AspNetCore.SignalR.Hub
{
    private readonly JanusWebSocketManager _janusWsManager;
    private const string HostSuffix = "_host";
    private const string ListenerSuffix = "_listener";
    private const string JanusVideoRoomPlugin = "janus.plugin.videoroom";
    private readonly ILogger<LivestreamHub> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _janusUrl = "";

    // (Production: Use DB or distributed cache instead)
    private static readonly ConcurrentDictionary<Guid, (long sessionId, long handleId, long janusRoomId)> Rooms = new();
    private static readonly ConcurrentDictionary<string, (long sessionId, long handleId)> ListenerSessions = new();
    private static readonly ConcurrentDictionary<Guid, HashSet<string>> RoomListeners = new();
    
    
    public LivestreamHub(JanusWebSocketManager janusWsManager, ILogger<LivestreamHub> logger, HttpClient httpClient)
    {
        _janusWsManager = janusWsManager;
        _logger = logger;
        _httpClient = httpClient;
    }
    
    public async Task<long?> CreateSessionViaHttp()
    {
        var url = "_janusUrl";

        var request = new JObject
        {
            ["janus"] = "create",
            ["transaction"] = Guid.NewGuid().ToString()
        };

        var content = new StringContent(request.ToString(), Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(url, content);
        
        _logger.LogInformation("HTTP. Session response: {response}", response.Content.ReadAsStringAsync().Result);

        if (response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync();
            var data = JObject.Parse(responseBody);

            if (data["janus"]?.ToString() == "success")
            {
                // ✅ Fix: Get session ID from "data" → "id"
                var sessionId = data["data"]?["id"]?.Value<long>();

                if (sessionId != null)
                {
                    return sessionId;
                }
                else
                {
                    _logger.LogError("🚨 Failed to extract session ID from Janus response.");
                }
            }
        }

        _logger.LogError("🚨 Failed to create session over HTTP. Status: {StatusCode}", response.StatusCode);
        return null;
    }
    
    public async Task HostCreateRoom(Guid roomGuid)
    {
        _logger.LogInformation("Creating room {RoomGuid}", roomGuid);
        
        var sessionId = await CreateSessionViaHttp()
                        ?? throw new Exception("Unable to create Janus session.");
        
        _logger.LogInformation("sessionId: {sessionId}", sessionId);

        var handleId = await _janusWsManager.AttachPluginAsync(sessionId, JanusVideoRoomPlugin)
                        ?? throw new Exception("Unable to attach plugin.");
        
        _logger.LogInformation("handleId: {handleId}", handleId);

        long janusRoomId = new Random().Next(100000, 999999);

        var createRoomResponse = await _janusWsManager.SendAsync(new JObject
        {
            ["janus"] = "message",
            ["session_id"] = sessionId,
            ["handle_id"] = handleId,
            ["transaction"] = Guid.NewGuid().ToString(),
            ["body"] = new JObject
            {
                ["request"] = "create",
                ["room"] = janusRoomId,
                ["publishers"] = 1,
                ["description"] = $"Room for ${roomGuid}",
            }
        });
        
        _logger.LogInformation("createRoomResponse: {createRoomResponse}", createRoomResponse);
        
        var createRoomStatus = createRoomResponse["janus"]?.ToString();
        _logger.LogInformation("createRoomResponse: {janusStatus}", createRoomStatus);
        
        if (createRoomStatus == null || !createRoomStatus.Equals("success"))
        {
            await Clients.Caller.SendAsync("JanusError", "Unable to create room.");
            return;
        }
        
        // Step 2: Immediately join the room as a publisher (host)
        var joinRoomResponse = await _janusWsManager.SendAsync(new JObject
        {
            ["janus"] = "message",
            ["session_id"] = sessionId,
            ["handle_id"] = handleId,
            ["transaction"] = Guid.NewGuid().ToString(),
            ["body"] = new JObject
            {
                ["request"] = "join",
                ["ptype"] = "publisher",
                ["room"] = janusRoomId,
                ["display"] = "host"
            }
        });
        
        var joinRoomResponseStatus = createRoomResponse["janus"]?.ToString();
        _logger.LogInformation("joinRoomResponse: {janusStatus}", joinRoomResponse);

        if (joinRoomResponseStatus == null || !joinRoomResponseStatus.Equals("success"))
        {
            await Clients.Caller.SendAsync("JanusError", "Unable to join room as host.");
            return;
        }
        
        var janusUrl = $"_janusUrl/{sessionId}";
        _logger.LogInformation("🔄 Fallback to HTTP GET: {JanusUrl}", janusUrl);

        var httpResponse = await _httpClient.GetAsync(janusUrl);
                    
        _logger.LogWarning("Verify Join Room: {httpResponse}.", httpResponse.Content.ReadAsStringAsync().Result);
                    
        // if (httpResponse.IsSuccessStatusCode)
        // {
        //     var content = await httpResponse.Content.ReadAsStringAsync();
        //     var httpData = JObject.Parse(content);
        //
        //     var jsep = httpData["jsep"];
        //     if (jsep != null)
        //     {
        //         await Clients.Caller.SendAsync("PublishStarted", new
        //         {
        //             Jsep = jsep
        //         });
        //         return;
        //     }
        //     else
        //     {
        //         _logger.LogError("🚨 No JSEP found in HTTP GET response.");
        //     }
        // }
        // else
        // {
        //     _logger.LogError("🚨 Failed to GET session info over HTTP. StatusCode: {StatusCode}", httpResponse.StatusCode);
        // }
        

        // Step 3: Store info clearly
        Rooms[roomGuid] = (sessionId, handleId, janusRoomId);
        RoomListeners[roomGuid] = new HashSet<string>();

        await Groups.AddToGroupAsync(Context.ConnectionId, roomGuid + HostSuffix);

        var hostCreateRoomResponse = new
        {
            RoomGuid = roomGuid,
            JanusRoomId = janusRoomId,
            SessionId = sessionId,
            HandleId = handleId
        };
        
        _logger.LogInformation("HostCreateRoom: {HostCreateRoomResponse}", hostCreateRoomResponse);
        
        // Notify host that room is ready and joined
        await Clients.Caller.SendAsync("RoomCreatedAndJoined", hostCreateRoomResponse);
    }
    
    public async Task JoinAsListener(Guid roomGuid)
    {
        if (!Rooms.TryGetValue(roomGuid, out var janusInfo))
        {
            await Clients.Caller.SendAsync("JanusError", "Room doesn't exist.");
            return;
        }

        var sessionId = await CreateSessionViaHttp()
                        ?? throw new Exception("Unable to create Janus session.");
        
        var handleId = await _janusWsManager.AttachPluginAsync(sessionId, JanusVideoRoomPlugin)
                        ?? throw new Exception("Unable to attach plugin.");

        await Groups.AddToGroupAsync(Context.ConnectionId, roomGuid + ListenerSuffix);
        ListenerSessions[Context.ConnectionId] = (sessionId, handleId);

        // Track listener
        RoomListeners[roomGuid].Add(Context.ConnectionId);
        
        // Response Feed
        var feedMessage = new JObject
        {
            ["janus"] = "message",
            ["session_id"] = sessionId,
            ["handle_id"] = handleId,
            ["transaction"] = Guid.NewGuid().ToString(),
            ["body"] = new JObject
            {
                ["request"] = "listparticipants",
                ["room"] = janusInfo.janusRoomId,
            }
        };

        var response = await _janusWsManager.SendAsync(feedMessage);
        
        _logger.LogInformation("feedMessageResponse {response}", response);
        
        if (response?["janus"]?.ToString() == "success")
        {
            var participants = response["plugindata"]?["data"]?["participants"] as JArray;

            if (participants != null)
            {
                // ✅ Find the participant where "publisher" is true
                var host = participants
                    .FirstOrDefault(p => p["publisher"]?.Value<bool>() == true);

                if (host != null)
                {
                    var hostId = host["id"]?.Value<long>();
                    _logger.LogInformation("✅ Host ID: {HostId}", hostId);
                    
                    var joinMessage = new JObject
                    {
                        ["janus"] = "message",
                        ["session_id"] = sessionId,
                        ["handle_id"] = handleId,
                        ["transaction"] = Guid.NewGuid().ToString(),
                        ["body"] = new JObject
                        {
                            ["request"] = "join",
                            ["ptype"] = "subscriber",
                            ["room"] = janusInfo.janusRoomId,
                            ["streams"] = new JArray
                            {
                                new JObject
                                {
                                    ["feed"] = hostId
                                }
                            }
                        }
                    };
                    
                     var feedResponse = await _janusWsManager.SendAsync(joinMessage);
                     
                     _logger.LogInformation("feedResponse: {feedResponse}", feedResponse);
                    
                     if (feedResponse?["janus"]?.ToString() == "success" || feedResponse?["janus"]?.ToString() == "ack")
                     {
                         var janusUrl = $"_janusUrl/{sessionId}";
                         _logger.LogInformation("🔄 Fallback to HTTP GET: {JanusUrl}", janusUrl);

                         var httpResponse = await _httpClient.GetAsync(janusUrl);

                         if (httpResponse.IsSuccessStatusCode)
                         {
                             var content = await httpResponse.Content.ReadAsStringAsync();
                             var httpData = JObject.Parse(content);

                             var jsep = httpData["jsep"];
                             if (jsep != null)
                             {
                                 await Clients.Caller.SendAsync("JoinRoomResponse", new
                                 {
                                     Jsep = jsep["sdp"].ToString(),
                                     RoomId = janusInfo.janusRoomId,
                                     SessionId = sessionId,
                                     HandleId = handleId,
                                 });
                             }
                             else
                             {
                                 _logger.LogError("🚨 No JSEP found in HTTP GET response.");
                             }
                         }
                         else
                         {
                             _logger.LogError("🚨 Failed to GET session info over HTTP. StatusCode: {StatusCode}", httpResponse.StatusCode);
                         }
                     }
                    
                }
                else
                {
                    _logger.LogWarning("🚨 No host found in participants list.");
                }
            }
        }

        // await Clients.Caller.SendAsync("ListenerReady", new {
        //     sessionId,
        //     handleId,
        //     janusRoomId = janusRoom.janusRoomId
        // });

        // (Optional) Notify host about listener count clearly
        await Clients.Group(roomGuid + HostSuffix).SendAsync("ListenerCountUpdated", RoomListeners[roomGuid].Count);
    }
    
    public async Task StartPublish(Guid roomGuid, string type, string sdp)
    {
        try
        {
            _logger.LogInformation("✅ StartPublish invoked: RoomGuid {RoomGuid}", roomGuid);
            _logger.LogInformation("✅ clientOfferJsep: {clientOfferJsep}", sdp);

            if (!Rooms.TryGetValue(roomGuid, out var janusInfo))
            {
                _logger.LogError("🚨 Room {RoomGuid} not found", roomGuid);
                await Clients.Caller.SendAsync("JanusError", "Room or session unavailable.");
                return;
            }

            var pulishTransaction = Guid.NewGuid().ToString();
            
            var publishMessage = new JObject
            {
                ["janus"] = "message",
                ["session_id"] = janusInfo.sessionId,
                ["handle_id"] = janusInfo.handleId,
                ["transaction"] = pulishTransaction,
                ["body"] = new JObject
                {
                    ["request"] = "publish",
                    ["room"] = janusInfo.janusRoomId,
                    ["audio"] = true,
                    ["video"] = true
                },
                ["jsep"] = new JObject
                {
                    ["type"] = type,
                    ["sdp"] = sdp
                }
            };

            var response = await _janusWsManager.SendAsync(publishMessage);

            _logger.LogInformation("Received response from Janus: {Response}", response?.ToString());

            if (response?["janus"]?.ToString() == "success" || response?["janus"]?.ToString() == "ack")
            {
                var janusUrl = $"_janusUrl/{janusInfo.sessionId}";
                _logger.LogInformation("🔄 Fallback to HTTP GET: {JanusUrl}", janusUrl);

                var httpResponse = await _httpClient.GetAsync(janusUrl);
                    
                _logger.LogWarning("httpResponse: {httpResponse}.", httpResponse.Content.ReadAsStringAsync().Result);
                    
                if (httpResponse.IsSuccessStatusCode)
                {
                    var content = await httpResponse.Content.ReadAsStringAsync();
                    var httpData = JObject.Parse(content);

                    var jsep = httpData["jsep"];
                    if (jsep != null)
                    {
                        await Clients.Caller.SendAsync("PublishStarted", new
                        {
                            SessionId = janusInfo.sessionId,
                            Jsep = jsep["sdp"].ToString()
                        });
                    }
                    else
                    {
                        _logger.LogError("🚨 No JSEP found in HTTP GET response.");
                    }
                }
                else
                {
                    _logger.LogError("🚨 Failed to GET session info over HTTP. StatusCode: {StatusCode}", httpResponse.StatusCode);
                }
            }
            else
            {
                _logger.LogError("Janus rejected publish: {Response}", response);
                await Clients.Caller.SendAsync("JanusError", "Publish failed at Janus.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("🚨 Exception during StartPublish: {ExceptionMessage}", ex.Message);
            await Clients.Caller.SendAsync("JanusError", "Internal server error during publishing.");
        }
    }
    
    public async Task SendAnswerToJanus(long janusRoomId, long sessionId, long handleId, string sdpAnswer)
    {
        var answerMessage = new JObject
        {
            ["janus"] = "message",
            ["session_id"] = sessionId,
            ["handle_id"] = handleId,
            ["body"] = new JObject
            {
                ["request"] = "start",
                ["room"] = janusRoomId
            },
            ["jsep"] = new JObject
            {
                ["type"] = "answer",
                ["sdp"] = sdpAnswer
            }
        };
        
        _logger.LogInformation("answerMessage: {response}", answerMessage);

        var response = await _janusWsManager.SendAsync(answerMessage);

        if (response["janus"].ToString() == "success" || response["janus"].ToString() == "ack")
        {
            _logger.LogInformation("SendAnswerToJanusResponse: {response}", response);
            await Clients.Caller.SendAsync("AnswerAccepted");
        }
        else
        {
            await Clients.Caller.SendAsync("JanusError", "Unable to send answer.");
        }
    }

    public async Task KeepAlive(long sessionId)
    {
        var keepAliveResponse = await _janusWsManager.SendAsync(new JObject
        {
            ["janus"] = "keepalive",
            ["session_id"] = sessionId,
            ["transaction"] = Guid.NewGuid().ToString(),
        });
        _logger.LogInformation("KeepAlive: {keepAliveResponse}", keepAliveResponse);
    }
    
    public override async Task OnDisconnectedAsync(Exception ex)
    {
        foreach (var room in RoomListeners)
        {
            if (room.Value.Remove(Context.ConnectionId))
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, room.Key + ListenerSuffix);

                // Notify host of updated listener count
                await Clients.Group(room.Key + HostSuffix).SendAsync("ListenerCountUpdated", room.Value.Count);
            }
        }

        await base.OnDisconnectedAsync(ex);
    }

    // Signaling messages from host to listeners
    public async Task SendSignalingFromHost(string roomName, string message)
    {
        await Clients.Group(roomName + ListenerSuffix)
            .SendAsync("ReceiveSignalingFromHost", message);
    }

    // Signaling messages from listener to host (if needed, e.g., for ICE candidates)
    public async Task SendSignalingToHost(string roomName, string message)
    {
        await Clients.Group(roomName + HostSuffix)
            .SendAsync("ReceiveSignalingFromListener", message);
    }
    
}