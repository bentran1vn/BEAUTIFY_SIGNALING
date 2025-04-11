using System.Collections.Concurrent;
using BEAUTIFY_PACKAGES.BEAUTIFY_PACKAGES.DOMAIN.Abstractions.Repositories;
using BEAUTIFY_SIGNALING.REPOSITORY;
using BEAUTIFY_SIGNALING.REPOSITORY.Entities;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace BEAUTIFY_SIGNALING.SERVICES.Hub;

public class ChatHub : Microsoft.AspNetCore.SignalR.Hub
{
    private readonly IRepositoryBase<Conversation, Guid> _conversationRepository;
    private readonly IRepositoryBase<Message, Guid> _messageRepository;
    private readonly IRepositoryBase<UserConversation, Guid> _userConversationRepository;
    private readonly ApplicationDbContext _dbContext;
    private readonly IRepositoryBase<Clinic, Guid> _clinicRepository;
    private readonly IRepositoryBase<User, Guid> _userRepository;
    private static readonly ConcurrentDictionary<string, Guid> UserConnections = new();
    private static readonly ConcurrentDictionary<string, Guid> ClinicConnections = new();
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(IRepositoryBase<Conversation, Guid> conversationRepository, IRepositoryBase<Message, Guid> messageRepository, IRepositoryBase<UserConversation, Guid> userConversationRepository, IRepositoryBase<User, Guid> userRepository, IRepositoryBase<Clinic, Guid> clinicRepository, ILogger<ChatHub> logger, ApplicationDbContext dbContext)
    {
        _conversationRepository = conversationRepository;
        _messageRepository = messageRepository;
        _userConversationRepository = userConversationRepository;
        _userRepository = userRepository;
        _clinicRepository = clinicRepository;
        _logger = logger;
        _dbContext = dbContext;
    }

    public override async Task OnConnectedAsync()
    {
        try
        {
            string connectionId = Context.ConnectionId;
            var clinicId = Context.GetHttpContext()?.Request.Query["clinicId"];
            var userId = Context.GetHttpContext()?.Request.Query["userId"];
            var type = Context.GetHttpContext()?.Request.Query["type"];
            //0 = user, 1 = clinic
        
            if (string.IsNullOrEmpty(type))
            {
                throw new ArgumentException("Invalid connection parameters.");
            }
        
            if (int.Parse(type!) == 0)
            {
                if (string.IsNullOrEmpty(userId))
                {
                    throw new ArgumentException("Missing userId.");
                }
            
                var user = await _userRepository.FindByIdAsync(Guid.Parse(userId!));
                if (user != null)
                {
                    UserConnections[connectionId] = user.Id;
                }
                _logger.LogInformation("User connected: {UserId} at {Time}", userId, DateTime.UtcNow);
            }
            else 
            {
                if (string.IsNullOrEmpty(clinicId))
                {
                    throw new ArgumentException("Missing clinicId.");
                }
            
                var clinic = await _clinicRepository.FindByIdAsync(Guid.Parse(clinicId!));
                if (clinic != null)
                {
                    ClinicConnections[connectionId] = clinic.Id;
                }
            }
            
            await base.OnConnectedAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred at {Time}: {Message}", DateTime.UtcNow, ex.Message);
        }
    }
    
    public async Task SendMessage(Guid senderId, Guid receiverId, bool isClinic, string content)
    {
        try
        {
            if (isClinic)
            {
                var clinic = await _clinicRepository.FindByIdAsync(senderId);
                var user = await _userRepository.FindByIdAsync(receiverId);

                if (user == null)
                {
                    await Clients.Caller.SendAsync("ReceiveMessage", "System", "User Not Found");
                    return;
                }

                if (clinic == null)
                {
                    await Clients.Caller.SendAsync("ReceiveMessage", "System", "Clinic Not Found");
                    return;
                }
            }
            else
            {
                var user = await _userRepository.FindByIdAsync(senderId);
                var clinic = await _clinicRepository.FindByIdAsync(receiverId);

                if (user == null)
                {
                    await Clients.Caller.SendAsync("ReceiveMessage", "System", "User Not Found");
                    return;
                }

                if (clinic == null)
                {
                    await Clients.Caller.SendAsync("ReceiveMessage", "System", "Clinic Not Found");
                    return;
                }
            }

            Guid conversationId;

            var existingConversation = await _userConversationRepository
                .FindSingleAsync(x => x.UserId == (isClinic ? receiverId : senderId) && x.ClinicId == (isClinic ? senderId : receiverId) && !x.IsDeleted);

            if (existingConversation != null)
            {
                conversationId = existingConversation.ConversationId;
            }
            else
            {
                var newConversation = new Conversation
                {
                    Id = Guid.NewGuid(),
                    Type = "Direct"
                };

                _conversationRepository.Add(newConversation);
                await _dbContext.SaveChangesAsync();

                var userConversation = new UserConversation
                {
                    Id = Guid.NewGuid(),
                    UserId = isClinic ? receiverId : senderId,
                    ClinicId = isClinic ? senderId : receiverId,
                    ConversationId = newConversation.Id,
                };

                _userConversationRepository.Add(userConversation);
                await _dbContext.SaveChangesAsync();

                conversationId = newConversation.Id;
            }

            var message = new Message()
            {
                Id = Guid.NewGuid(),
                ConversationId = conversationId,
                Content = content,
                SenderId = senderId,
                IsClinic = isClinic,
            };

            _messageRepository.Add(message);
            await _dbContext.SaveChangesAsync();

            string? connectionId = null;
            if (isClinic)
            {
                connectionId = UserConnections.FirstOrDefault(x => x.Value == receiverId).Key;
            }
            else
            {
                connectionId = ClinicConnections.FirstOrDefault(x => x.Value == receiverId).Key;
            }

            if (!string.IsNullOrEmpty(connectionId))
            {
                var messageDto = new
                {
                    MessageId = message.Id,
                    message.SenderId,
                    message.Content,
                    message.CreatedOnUtc,
                    IsClinic = isClinic
                };

                Console.WriteLine($"Serialized Message: {System.Text.Json.JsonSerializer.Serialize(messageDto)}");

                await Clients.Client(connectionId).SendAsync("ReceiveMessage", senderId, messageDto);
            }
            else
            {
                Console.WriteLine($"Receiver {receiverId} is offline.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while sending a message at {Time}: {Message}", DateTime.UtcNow, ex.Message);
        }
    }

    
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        try
        {
            string connectionId = Context.ConnectionId;
            if (UserConnections.TryGetValue(connectionId, out Guid _))
            {
                UserConnections.Remove(connectionId, out Guid _);
            }
            if (ClinicConnections.TryGetValue(connectionId, out Guid _))
            {
                ClinicConnections.Remove(connectionId, out Guid _);
            }
            await base.OnDisconnectedAsync(exception);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred at {Time}: {Message}", DateTime.UtcNow, ex.Message);
        }
    }
    
}