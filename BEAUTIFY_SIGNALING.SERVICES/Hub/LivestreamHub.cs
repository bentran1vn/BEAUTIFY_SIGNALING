using System.Collections.Concurrent;
using System.Text;
using BEAUTIFY_PACKAGES.BEAUTIFY_PACKAGES.DOMAIN.Abstractions.Repositories;
using BEAUTIFY_SIGNALING.REPOSITORY;
using BEAUTIFY_SIGNALING.REPOSITORY.Entities;
using BEAUTIFY_SIGNALING.SERVICES.Abstractions;
using BEAUTIFY_SIGNALING.SERVICES.LiveStream;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
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
    private readonly string _janusUrl;
    private const int VirtualViewBoost = 10;
    private readonly IJwtServices _jwtServices;
    private readonly IRepositoryBase<LivestreamRoom, Guid> _livestreamRoomRepository;
    private readonly ApplicationDbContext _dbContext;
    
    private static readonly ConcurrentDictionary<Guid, (long sessionId, long handleId, long janusRoomId)> Rooms = new();
    private static readonly ConcurrentDictionary<Guid, HashSet<string>> RoomListeners = new();
    private static readonly ConcurrentDictionary<string, HashSet<Guid>> UserRooms = new();
    
    public LivestreamHub(JanusWebSocketManager janusWsManager, ILogger<LivestreamHub> logger, HttpClient httpClient, IConfiguration configuration, IJwtServices jwtServices, IRepositoryBase<LivestreamRoom, Guid> livestreamRoomRepository, ApplicationDbContext dbContext)
    {
        _janusWsManager = janusWsManager;
        _logger = logger;
        _httpClient = httpClient;
        _jwtServices = jwtServices;
        _livestreamRoomRepository = livestreamRoomRepository;
        _dbContext = dbContext;
        _janusUrl = configuration.GetValue<string>("JanusUrl")!;
    }
    
    public override async Task OnConnectedAsync()
    {
        // var token = Context.GetHttpContext()?.Request.Query["token"];
        // if (string.IsNullOrEmpty(token))
        // {
        //     _logger.LogWarning("🚫 Connection rejected due to missing token");
        //     Context.Abort();
        //     return;
        // }
        // var principal = _jwtServices.VerifyForgetToken(token!);
        // var clinicId = principal?.FindFirst("ClinicId")?.Value;
        // if (string.IsNullOrEmpty(clinicId))
        // {
        //     _logger.LogWarning("🚫 Connection rejected due to missing clinicId in Token");
        //     Context.Abort();
        //     return;
        // }
        //
        // _logger.LogInformation($"✅ Clinic {clinicId} connected with connection ID: {Context.ConnectionId}");
        await base.OnConnectedAsync();
    }
    
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (UserRooms.TryGetValue(Context.ConnectionId, out var rooms))
        {
            foreach (var roomGuid in rooms)
            {
                if (RoomListeners.TryGetValue(roomGuid, out var listeners))
                {
                    listeners.Remove(Context.ConnectionId);
                    await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomGuid.ToString());

                    // Notify host and listeners about new view count
                    await UpdateViewerCount(roomGuid);
                }
            }

            UserRooms.TryRemove(Context.ConnectionId, out _);
        }

        await base.OnDisconnectedAsync(exception);
    }
    
    private async Task UpdateViewerCount(Guid roomGuid)
    {
        var realCount = RoomListeners.GetValueOrDefault(roomGuid)?.Count ?? 0;
        var virtualCount = realCount + VirtualViewBoost;

        // Send to host → Real view count
        await Clients.Group(roomGuid + HostSuffix).SendAsync("ListenerCountUpdated", realCount);

        // Send to listeners → Virtual view count (marketing boost)
        await Clients.Group(roomGuid + ListenerSuffix).SendAsync("ListenerCountUpdated", virtualCount);
    }
    
    public async Task<long?> CreateSessionViaHttp()
    {
        var request = new JObject
        {
            ["janus"] = "create",
            ["transaction"] = Guid.NewGuid().ToString()
        };

        var content = new StringContent(request.ToString(), Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(_janusUrl, content);
        
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
    
    public async Task HostCreateRoom()
    {
        var roomGuid = Guid.NewGuid();
        // var token = Context.GetHttpContext()?.Request.Query["token"]!;
        // var principal = _jwtServices.VerifyForgetToken(token!);
        // var clinicId = principal?.FindFirst("ClinicId")?.Value!;
        var clinicId = "3AB003E6-B3F0-4FB6-8912-8DFB83132B08";
        
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
        
        var janusUrl = $"{_janusUrl}/{sessionId}";
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
        
        Rooms[roomGuid] = (sessionId, handleId, janusRoomId);
        RoomListeners[roomGuid] = new HashSet<string>();

        // Track the host connection
        if (!UserRooms.ContainsKey(Context.ConnectionId))
            UserRooms[Context.ConnectionId] = new HashSet<Guid>();

        UserRooms[Context.ConnectionId].Add(roomGuid);

        await Groups.AddToGroupAsync(Context.ConnectionId, roomGuid + HostSuffix);

        var liveStreamRoom = new LivestreamRoom()
        {
            Id = roomGuid,
            Name = $"Room {roomGuid}",
            ClinicId = new Guid(clinicId),
            Type = "Selling",
            Date = DateOnly.FromDateTime(DateTime.UtcNow),
            StartDate = TimeOnly.FromDateTime(DateTime.UtcNow),
        };
        
        _livestreamRoomRepository.Add(liveStreamRoom);
        await _dbContext.SaveChangesAsync();
        
        var hostCreateRoomResponse = new
        {
            RoomGuid = roomGuid,
            JanusRoomId = janusRoomId,
            SessionId = sessionId,
            HandleId = handleId
        };
        
        _logger.LogInformation("HostCreateRoom: {HostCreateRoomResponse}", hostCreateRoomResponse);
        
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
                         var janusUrl = $"{_janusUrl}/{sessionId}";
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
        RoomListeners[roomGuid].Add(Context.ConnectionId);

        if (!UserRooms.ContainsKey(Context.ConnectionId))
            UserRooms[Context.ConnectionId] = new HashSet<Guid>();

        UserRooms[Context.ConnectionId].Add(roomGuid);

        await Groups.AddToGroupAsync(Context.ConnectionId, roomGuid + ListenerSuffix);

        // Notify all about viewer update
        await UpdateViewerCount(roomGuid);
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
                var janusUrl = $"{_janusUrl}/{janusInfo.sessionId}";
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
    
    public async Task SendMessage(Guid roomGuid, string message)
    {
        var userId = Context.GetHttpContext()?.Request.Query["userId"];

        if (string.IsNullOrEmpty(userId))
        {
            await Clients.Caller.SendAsync("JanusError", "Unauthorized");
            return;
        }

        // Broadcast message to the room
        await Clients.Group(roomGuid + HostSuffix).SendAsync("ReceiveMessage", new
        {
            UserId = userId,
            Message = message
        });

        await Clients.Group(roomGuid + ListenerSuffix).SendAsync("ReceiveMessage", new
        {
            UserId = userId,
            Message = message
        });
    }

    public async Task SendSignaling(Guid roomGuid, string message)
    {
        var userId = Context.GetHttpContext()?.Request.Query["userId"];
        if (string.IsNullOrEmpty(userId)) return;

        await Clients.Group(roomGuid.ToString()).SendAsync("ReceiveSignaling", new
        {
            UserId = userId,
            Message = message
        });
    }
    
}