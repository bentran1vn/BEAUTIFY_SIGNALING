using System.Collections.Concurrent;
using System.Text;
using BEAUTIFY_PACKAGES.BEAUTIFY_PACKAGES.DOMAIN.Abstractions.Repositories;
using BEAUTIFY_SIGNALING.REPOSITORY;
using BEAUTIFY_SIGNALING.REPOSITORY.Entities;
using BEAUTIFY_SIGNALING.SERVICES.Abstractions;
using BEAUTIFY_SIGNALING.SERVICES.LiveStream;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace BEAUTIFY_SIGNALING.SERVICES.Hub;

public class LivestreamHub(
    JanusWebSocketManager janusWsManager,
    ILogger<LivestreamHub> logger,
    HttpClient httpClient,
    IConfiguration configuration,
    IRepositoryBase<LivestreamRoom, Guid> livestreamRoomRepository,
    ApplicationDbContext dbContext,
    IRepositoryBase<Promotion, Guid> promotionRepository,
    IRepositoryBase<Service, Guid> servicesRepository,
    IRepositoryBase<UserClinic, Guid> userClinicRepository,
    IRepositoryBase<Order, Guid> orderRepository,
    IRepositoryBase<Clinic, Guid> clinicRepository,
    IRepositoryBase<LiveStreamDetail, Guid> livestreamDetailRepository)
    : Microsoft.AspNetCore.SignalR.Hub
{
    private const string HostSuffix = "_host";
    private const string ListenerSuffix = "_listener";
    private const string JanusVideoRoomPlugin = "janus.plugin.videoroom";
    private readonly string _janusUrl = configuration.GetValue<string>("JanusUrl")!;
    private const int VirtualViewBoost = 10;

    private static readonly ConcurrentDictionary<Guid, (long sessionId, long handleId, long janusRoomId)> Rooms = new();
    private static readonly ConcurrentDictionary<Guid, HashSet<string>> RoomListeners = new();
    private static readonly ConcurrentDictionary<string, HashSet<Guid>> UserRooms = new();
    private static readonly ConcurrentDictionary<Guid, ConcurrentQueue<Activity>> RoomLogs = new();
    
    
    /// <summary>
    /// Activity model for logging
    /// </summary>
    private class Activity
    {
        public string? UserId { get; set; }
        public int ActivityType { get; set; }
        // 0: Join Stream
        // 1: Send Message
        // 2: Reaction
        public DateTimeOffset Timestamp { get; set; }
    }
    public override async Task OnConnectedAsync()
    {
        // var token = Context.GetHttpContext()?.Request.Query["userId"];
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

        var response = await httpClient.PostAsync(_janusUrl, content);
        
        logger.LogInformation("HTTP. Session response: {response}", response.Content.ReadAsStringAsync().Result);

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
                    logger.LogError("🚨 Failed to extract session ID from Janus response.");
                }
            }
        }

        logger.LogError("🚨 Failed to create session over HTTP. Status: {StatusCode}", response.StatusCode);
        return null;
    }
    public class RoomData
    {
        public required string Name { get; set; }
        public required string Description { get; set; }
        public required string Image { get; set; }
    }
    public async Task HostCreateRoom(RoomData data)
    {
        var roomGuid = Guid.NewGuid();
        var clinicId = Context.GetHttpContext()?.Request.Query["clinicId"];
        var userId = Context.GetHttpContext()?.Request.Query["userId"];
        if (string.IsNullOrEmpty(clinicId) || string.IsNullOrEmpty(userId))
        {
            logger.LogWarning("🚫 Connection rejected due to missing token");
            Context.Abort();
            return;
        }
        
        var clinic = await clinicRepository.FindByIdAsync(new Guid(clinicId!));

        if (clinic == null || clinic.AdditionLivestreams <= 0)
        {
            logger.LogWarning("🚫 Connection rejected due to clinic not found or no available livestreams");
            await Clients.Caller.SendAsync("SystemError", "No Quota for Livestream, buy more quota !");
            return;
        }
        
        var query = userClinicRepository
            .FindAll(x => x.UserId.Equals(new Guid(userId!)) &&
                x.ClinicId.Equals(new Guid(clinicId!)) && x.IsDeleted == false);
        
        var isAuth = await query.Include(x => x.User)
            .ThenInclude(y => y.Role)
            .FirstOrDefaultAsync();
        
        if (isAuth == null)
        {
            logger.LogWarning("🚫 Unauthorized access attempt by {UserId}", userId);
            Context.Abort();
            return;
        }
        
        if(isAuth.User?.Role?.Name != "Clinic Admin")
        {
            logger.LogWarning("🚫 Unauthorized access attempt by {UserId}", userId);
            Context.Abort();
            return;
        }
        
        logger.LogInformation("Creating room {RoomGuid}", roomGuid);
        
        var sessionId = await CreateSessionViaHttp()
                        ?? throw new Exception("Unable to create Janus session.");
        
        logger.LogInformation("sessionId: {sessionId}", sessionId);

        var handleId = await janusWsManager.AttachPluginAsync(sessionId, JanusVideoRoomPlugin)
                        ?? throw new Exception("Unable to attach plugin.");
        
        logger.LogInformation("handleId: {handleId}", handleId);

        long janusRoomId = new Random().Next(100000, 999999);

        var createRoomResponse = await janusWsManager.SendAsync(new JObject
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
        
        logger.LogInformation("createRoomResponse: {createRoomResponse}", createRoomResponse);
        
        var createRoomStatus = createRoomResponse["janus"]?.ToString();
        logger.LogInformation("createRoomResponse: {janusStatus}", createRoomStatus);
        
        if (createRoomStatus == null || !createRoomStatus.Equals("success"))
        {
            await Clients.Caller.SendAsync("JanusError", "Unable to create room.");
            return;
        }
        
        // Step 2: Immediately join the room as a publisher (host)
        var joinRoomResponse = await janusWsManager.SendAsync(new JObject
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
        logger.LogInformation("joinRoomResponse: {janusStatus}", joinRoomResponse);

        if (joinRoomResponseStatus == null || !joinRoomResponseStatus.Equals("success"))
        {
            await Clients.Caller.SendAsync("JanusError", "Unable to join room as host.");
            return;
        }
        
        var janusUrl = $"{_janusUrl}/{sessionId}";
        logger.LogInformation("🔄 Fallback to HTTP GET: {JanusUrl}", janusUrl);

        var httpResponse = await httpClient.GetAsync(janusUrl);
                    
        logger.LogWarning("Verify Join Room: {httpResponse}.", httpResponse.Content.ReadAsStringAsync().Result);
                    
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
            Name = data.Name,
            ClinicId = new Guid(clinicId),
            Type = "Selling",
            Description = data.Description,
            Image = data.Image,
            Date = DateOnly.FromDateTime(DateTime.UtcNow),
            StartDate = TimeOnly.FromDateTime(DateTime.UtcNow),
            Status = "live",
        };
        
        livestreamRoomRepository.Add(liveStreamRoom);
        await dbContext.SaveChangesAsync();
        
        var hostCreateRoomResponse = new
        {
            RoomGuid = roomGuid,
            JanusRoomId = janusRoomId,
            SessionId = sessionId,
            HandleId = handleId
        };
        
        clinic.AdditionLivestreams -= 1;
        
        await dbContext.SaveChangesAsync();
        
        
        logger.LogInformation("HostCreateRoom: {HostCreateRoomResponse}", hostCreateRoomResponse);
        
        await Clients.Caller.SendAsync("RoomCreatedAndJoined", hostCreateRoomResponse);
    }
    public async Task EndLivestream(Guid roomGuid)
    {
        if (!Rooms.TryGetValue(roomGuid, out var janusInfo))
        {
            logger.LogError("🚨 Room {RoomGuid} not found", roomGuid);
            await Clients.Caller.SendAsync("JanusError", "Room not found.");
            return;
        }

        // Ensure that only the host can end the livestream
        if (!UserRooms.TryGetValue(Context.ConnectionId, out var rooms) || !rooms.Contains(roomGuid))
        {
            logger.LogWarning("🚨 Unauthorized end livestream attempt by {ConnectionId}", Context.ConnectionId);
            await Clients.Caller.SendAsync("JanusError", "Unauthorized action.");
            return;
        }

        logger.LogInformation("🚨 Ending livestream for Room {RoomGuid}", roomGuid);

        // Step 1: Destroy Janus session
        var destroySessionRequest = new JObject
        {
            ["janus"] = "message",
            ["session_id"] = janusInfo.sessionId,
            ["handle_id"] = janusInfo.handleId,
            ["transaction"] = Guid.NewGuid().ToString(),
            ["body"] = new JObject
            {
                ["request"] = "destroy",
                ["room"] = janusInfo.janusRoomId
            }
        };

        var response = await janusWsManager.SendAsync(destroySessionRequest);
        if (response?["janus"]?.ToString() != "success")
        {
            logger.LogError("🚨 Failed to destroy Janus session for room {RoomGuid}", roomGuid);
            await Clients.Caller.SendAsync("JanusError", "Failed to destroy Janus session.");
            return;
        }

        logger.LogInformation("✅ Janus session destroyed for room {RoomGuid}", roomGuid);

        // Step 2: Remove from memory
        Rooms.TryRemove(roomGuid, out _);
        RoomListeners.TryRemove(roomGuid, out _);

        // Step 3: Remove from user groups
        if (UserRooms.TryGetValue(Context.ConnectionId, out var userRooms))
        {
            userRooms.Remove(roomGuid);
            if (userRooms.Count == 0)
                UserRooms.TryRemove(Context.ConnectionId, out _);
        }
        
        var joinCount = 0;
        var messageCount = 0;
        var reactionCount = 0;
        var completedBooking = 0;

        var check = RoomLogs.TryGetValue(roomGuid, out var totalLogs);
        
        if (check && totalLogs != null)
        {
            foreach (var activity in totalLogs)
            {
                switch (activity.ActivityType)
                {
                    case 0: // Join Stream
                        joinCount++;
                        break;
                    case 1: // Send Message
                        messageCount++;
                        break;
                    case 2: // Reaction
                        reactionCount++;
                        break;
                }
            }
        }
        
        var completedBookingCount = await orderRepository
            .FindAll(x => x.LivestreamRoomId.Equals(roomGuid) && x.Status == "Completed")
            .CountAsync();

        var detail = new LiveStreamDetail()
        {
            JoinCount = joinCount,
            MessageCount = messageCount,
            ReactionCount = reactionCount,
            TotalActivities = joinCount + messageCount + reactionCount,
            TotalBooking = completedBookingCount,
            LivestreamRoomId = roomGuid,
        };
        
        // Remove all listeners from the group
        await Clients.Group(roomGuid + HostSuffix).SendAsync("LivestreamEnded", detail);
        
        await Clients.Group(roomGuid + ListenerSuffix).SendAsync("LivestreamEnded");

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomGuid + HostSuffix);

        // Step 4: Update the database to mark the livestream as ended
        var room = await livestreamRoomRepository.FindByIdAsync(roomGuid);
        if (room != null)
        {
            room.Status = "unlive";
            room.EndDate = TimeOnly.FromDateTime(DateTime.UtcNow);
            await dbContext.SaveChangesAsync();
        }

        var promotions = await promotionRepository.FindAll(
            x => x.LivestreamRoomId.Equals(roomGuid) &&
                 x.IsActivated && !x.IsDeleted).ToListAsync();

        foreach (var promotion in promotions)
        {
            promotion.IsActivated = false;
            promotion.EndDate = DateTimeOffset.UtcNow;
        }
        
        livestreamDetailRepository.Add(detail);
        await dbContext.SaveChangesAsync();
        
        promotionRepository.UpdateRange(promotions);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("✅ Livestream for Room {RoomGuid} has ended", roomGuid);
    }
    private async Task RegisterRoomLog(Guid roomGuid, string userId, int activityType)
    {
        if (!RoomLogs.TryGetValue(roomGuid, out var logs))
        {
            logs = new ConcurrentQueue<Activity>();
            RoomLogs[roomGuid] = logs;
        }

        logs.Enqueue(new Activity
        {
            UserId = userId,
            ActivityType = activityType,
            Timestamp = DateTimeOffset.UtcNow
        });
    }
    public async Task JoinAsListener(Guid roomGuid)
    {
        if (!Rooms.TryGetValue(roomGuid, out var janusInfo))
        {
            await Clients.Caller.SendAsync("JanusError", "Room doesn't exist.");
            return;
        }

        var userId = Context.GetHttpContext()?.Request.Query["userId"];
        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning("🚫 Connection rejected due to missing userId");
            Context.Abort();
            return;
        }

        // Register join activity
        await RegisterRoomLog(roomGuid, userId, 0);

        var sessionId = await CreateSessionViaHttp()
                        ?? throw new Exception("Unable to create Janus session.");
        
        var handleId = await janusWsManager.AttachPluginAsync(sessionId, JanusVideoRoomPlugin)
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

        var response = await janusWsManager.SendAsync(feedMessage);
        
        logger.LogInformation("feedMessageResponse {response}", response);
        
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
                    logger.LogInformation("✅ Host ID: {HostId}", hostId);
                    
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
                    
                     var feedResponse = await janusWsManager.SendAsync(joinMessage);
                     
                     logger.LogInformation("feedResponse: {feedResponse}", feedResponse);
                    
                     if (feedResponse?["janus"]?.ToString() == "success" || feedResponse?["janus"]?.ToString() == "ack")
                     {
                         var janusUrl = $"{_janusUrl}/{sessionId}";
                         logger.LogInformation("🔄 Fallback to HTTP GET: {JanusUrl}", janusUrl);

                         var httpResponse = await httpClient.GetAsync(janusUrl);

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
                                 logger.LogError("🚨 No JSEP found in HTTP GET response.");
                             }
                         }
                         else
                         {
                             logger.LogError("🚨 Failed to GET session info over HTTP. StatusCode: {StatusCode}", httpResponse.StatusCode);
                         }
                     }
                    
                }
                else
                {
                    logger.LogWarning("🚨 No host found in participants list.");
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
            logger.LogInformation("✅ StartPublish invoked: RoomGuid {RoomGuid}", roomGuid);
            logger.LogInformation("✅ clientOfferJsep: {clientOfferJsep}", sdp);

            if (!Rooms.TryGetValue(roomGuid, out var janusInfo))
            {
                logger.LogError("🚨 Room {RoomGuid} not found", roomGuid);
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

            var response = await janusWsManager.SendAsync(publishMessage);

            logger.LogInformation("Received response from Janus: {Response}", response?.ToString());

            if (response?["janus"]?.ToString() == "success" || response?["janus"]?.ToString() == "ack")
            {
                var janusUrl = $"{_janusUrl}/{janusInfo.sessionId}";
                logger.LogInformation("🔄 Fallback to HTTP GET: {JanusUrl}", janusUrl);

                var httpResponse = await httpClient.GetAsync(janusUrl);
                    
                logger.LogWarning("httpResponse: {httpResponse}.", httpResponse.Content.ReadAsStringAsync().Result);
                    
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
                        logger.LogError("🚨 No JSEP found in HTTP GET response.");
                    }
                }
                else
                {
                    logger.LogError("🚨 Failed to GET session info over HTTP. StatusCode: {StatusCode}", httpResponse.StatusCode);
                }
            }
            else
            {
                logger.LogError("Janus rejected publish: {Response}", response);
                await Clients.Caller.SendAsync("JanusError", "Publish failed at Janus.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError("🚨 Exception during StartPublish: {ExceptionMessage}", ex.Message);
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
        
        logger.LogInformation("answerMessage: {response}", answerMessage);

        var response = await janusWsManager.SendAsync(answerMessage);

        if (response["janus"].ToString() == "success" || response["janus"].ToString() == "ack")
        {
            logger.LogInformation("SendAnswerToJanusResponse: {response}", response);
            await Clients.Caller.SendAsync("AnswerAccepted");
        }
        else
        {
            await Clients.Caller.SendAsync("JanusError", "Unable to send answer.");
        }
    }
    public async Task KeepAlive(long sessionId)
    {
        var keepAliveResponse = await janusWsManager.SendAsync(new JObject
        {
            ["janus"] = "keepalive",
            ["session_id"] = sessionId,
            ["transaction"] = Guid.NewGuid().ToString(),
        });
        logger.LogInformation("KeepAlive: {keepAliveResponse}", keepAliveResponse);
    }
    public async Task SendMessage(Guid roomGuid, string message)
    {
        var userId = Context.GetHttpContext()?.Request.Query["userId"];
        
        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning("🚫 Connection rejected due to missing token");
            Context.Abort();
            return;
        }

        // Register message activity
        await RegisterRoomLog(roomGuid, userId, 1);

        // Broadcast message to the room
        await Clients.Group(roomGuid + HostSuffix).SendAsync("ReceiveMessage", new
        {
            UserId = new Guid(userId),
            Message = message,
            CreateAt = DateTime.Now
        });

        await Clients.Group(roomGuid + ListenerSuffix).SendAsync("ReceiveMessage", new
        {
            UserId = new Guid(userId),
            Message = message,
            CreateAt = DateTime.Now
        });
    }
    public async Task SendReaction(Guid roomGuid, int id)
    {
        var userId = Context.GetHttpContext()?.Request.Query["userId"];
        
        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning("🚫 Connection rejected due to missing userId");
            Context.Abort();
            return;
        }

        // Register reaction activity
        await RegisterRoomLog(roomGuid, userId, 2);
        
        // Broadcast message to the room
        await Clients.Group(roomGuid + HostSuffix).SendAsync("ReceiveReaction", new
        {
            UserId = Guid.NewGuid().ToString(),
            Id = id,
            CreateAt = DateTime.Now
        });

        await Clients.Group(roomGuid + ListenerSuffix).SendAsync("ReceiveReaction", new
        {
            UserId = Guid.NewGuid().ToString(),
            Id = id,
            CreateAt = DateTime.Now
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
    public async Task SetPromotionService(Guid serviceId, Guid roomId, int percent)
    {
        try
        {
            var service = await servicesRepository.FindByIdAsync(serviceId);

            if (service != null)
            {
                var exist = await promotionRepository
                    .FindSingleAsync(x => x.ServiceId.Equals(serviceId) &&
                                          x.LivestreamRoomId.Equals(roomId) &&
                                          x.IsActivated && !x.IsDeleted);

                if (exist != null)
                {
                    exist.IsActivated = false;
                }
            
                var promotion = new Promotion()
                {
                    Id = Guid.NewGuid(),
                    Name = $"LiveStream-{DateTimeOffset.UtcNow}",
                    StartDate = DateTimeOffset.UtcNow,
                    LivestreamRoomId = roomId,
                    DiscountPercent = percent,
                    IsActivated = true,
                    ServiceId = serviceId,
                };
            
                promotionRepository.Add(promotion);

                await dbContext.SaveChangesAsync();
                
                await Clients.Group(roomId + HostSuffix).SendAsync("UpdateServicePromotion", new
                {
                    Id = serviceId,
                    DiscountLivePercent = promotion.DiscountPercent,
                    CreateAt = DateTimeOffset.UtcNow
                });
            }
        }
        catch (Exception ex)
        {
            logger.LogError("Problem in add promotion for service", ex.Message);
        }
    }
    public async Task DisplayService(Guid serviceId, Guid roomId, bool isDisplay)
    {
        try
        {
            var query = servicesRepository.FindAll(x => x.Id.Equals(serviceId));

            query = query
                .Include(x => x.ServiceMedias)
                .Include(x => x.Promotions);

            var service = await query.FirstOrDefaultAsync();

            if (service != null)
            {
                if (isDisplay)
                {
                    await Clients.Group(roomId + ListenerSuffix).SendAsync("DisplayService", new
                    {
                        Id = serviceId,
                        IsDisplay = isDisplay,
                        Service = new
                        {
                            Id = service.Id,
                            Name = service.Name,
                            Description = service.Description,
                            Images = service.ServiceMedias?.Select(x => x.ImageUrl).ToList() ?? [],
                            MaxPrice = service.MaxPrice,
                            MinPrice = service.MinPrice,
                            DiscountPercent = service.Promotions?.FirstOrDefault(
                                x => 
                                    x.LivestreamRoomId.Equals(roomId) &&
                                    x.ServiceId.Equals(serviceId) &&
                                    x.IsActivated &&
                                    !x.IsDeleted
                            )?.DiscountPercent ?? 0.0,
                            Category = new
                            {
                                Name = service.Category!.Name,
                                Description = service.Category!.Description,
                            }
                        }
                    });
                }
                else
                {
                    await Clients.Group(roomId + ListenerSuffix).SendAsync("DisplayService", new
                    {
                        Id = serviceId,
                        IsDisplay = isDisplay,
                    });
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError("Problem in add promotion for service", ex.Message);
        }
    }
}