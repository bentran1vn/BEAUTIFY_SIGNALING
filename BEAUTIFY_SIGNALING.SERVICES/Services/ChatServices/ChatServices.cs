using BEAUTIFY_PACKAGES.BEAUTIFY_PACKAGES.CONTRACT.Abstractions.Shared;
using BEAUTIFY_PACKAGES.BEAUTIFY_PACKAGES.DOMAIN.Abstractions.Repositories;
using BEAUTIFY_SIGNALING.REPOSITORY;
using BEAUTIFY_SIGNALING.REPOSITORY.Entities;
using BEAUTIFY_SIGNALING.SERVICES.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace BEAUTIFY_SIGNALING.SERVICES.Services.ChatServices;

public class ChatServices : IChatServices
{
    private readonly IRepositoryBase<UserConversation, Guid> _userConversationRepository;
    private readonly IRepositoryBase<Message, Guid> _messageRepository;
    private readonly IRepositoryBase<Conversation, Guid> _conversationRepository;
    private readonly IRepositoryBase<User, Guid> _userRepository;
    private readonly IRepositoryBase<Clinic, Guid> _clinicRepository;
    private readonly ApplicationDbContext _dbContext;

    public ChatServices(IRepositoryBase<Message, Guid> messageRepository, IRepositoryBase<UserConversation, Guid> userConversationnRepository, ApplicationDbContext dbContext, IRepositoryBase<Conversation, Guid> conversationRepository, IRepositoryBase<User, Guid> userRepository, IRepositoryBase<Clinic, Guid> clinicRepository)
    {
        _messageRepository = messageRepository;
        _userConversationRepository = userConversationnRepository;
        _dbContext = dbContext;
        _conversationRepository = conversationRepository;
        _userRepository = userRepository;
        _clinicRepository = clinicRepository;
    }

    public async Task<Result<List<ResponseModel.MessageResponseModel>>> GetAllMessageOfConversation(Guid conversationId)
    {
        var query = _messageRepository.FindAll(x => x.IsDeleted == false);
        query = query.Where(x => x.ConversationId == conversationId);
        query = query.OrderByDescending(x => x.CreatedOnUtc);
        
        var messages = await query.ToListAsync();
        
        var userConversations = await _userConversationRepository.FindAll(x => x.IsDeleted == false)
            .Where(x => x.ConversationId == conversationId)
            .FirstOrDefaultAsync();
        
        if (userConversations == null)
        {
            return Result.Failure<List<ResponseModel.MessageResponseModel>>(new Error("404", "Conversation not found"));
        }
        
        var user = await _userRepository.FindByIdAsync((Guid)userConversations.UserId!);
        var clinic = await _clinicRepository.FindByIdAsync((Guid)userConversations.ClinicId!);

        var result = messages.Select(x =>
        {
            // var sender = x.IsClinic ? clinic : user;
            var senderId = x.IsClinic ? clinic?.Id : user?.Id;
            var senderName = x.IsClinic ? clinic?.Name : user?.FullName;
            var senderProfilePicture = x.IsClinic ? clinic?.ProfilePictureUrl : user?.ProfilePicture;

            return new ResponseModel.MessageResponseModel(x.Id,
                (Guid)x.ConversationId, (Guid)senderId, x.IsClinic, senderName, senderProfilePicture, x.Content,
                x.CreatedOnUtc);
        }).ToList();
        
        return Result.Success(result);
    }

    public async Task<Result> SendMessage(Guid senderId, Guid receiverId, string content, bool isClinic)
    {
        var query = _userConversationRepository.FindAll(x => x.IsDeleted == false);
        if (isClinic)
        {
            query = query.Where(x => x.ClinicId == senderId && x.UserId == receiverId);
        } 
        else
        {
            query = query.Where(x => x.UserId == senderId && x.ClinicId == receiverId);
        }
        var userConversation = await query.FirstOrDefaultAsync();
        if (userConversation != null)
        {
            var newMessage = new Message
            {
                ConversationId = userConversation.ConversationId,
                SenderId = senderId,
                Content = content,
                IsClinic = isClinic,
            };
            _messageRepository.Add(newMessage);
            await _dbContext.SaveChangesAsync();
        }
        else
        {
            var newConversation = new Conversation
            {
                Id = Guid.NewGuid(),
                Type = ""
            };
            _conversationRepository.Add(newConversation);
            await _dbContext.SaveChangesAsync();
            var newUserConversation = new UserConversation
            {
                Id = Guid.NewGuid(),
                UserId = isClinic ? receiverId : senderId,
                ClinicId = isClinic ? senderId : receiverId,
                ConversationId = newConversation.Id,
            };
            _userConversationRepository.Add(newUserConversation);
            await _dbContext.SaveChangesAsync();
            var newMessage = new Message
            {
                ConversationId = newConversation.Id,
                SenderId = senderId,
                Content = content,
                IsClinic = isClinic,
            };
            _messageRepository.Add(newMessage);
            await _dbContext.SaveChangesAsync();
        }
        
        return Result.Success("Message sent successfully");
    }

    public async Task<Result<List<ResponseModel.ConversationResponseModel>>> GetAllConversationOfEntity(Guid entityId, bool isClinic)
    {
        var query = _userConversationRepository.FindAll(x => x.IsDeleted == false);
        if (isClinic)
        {
            query = query.Where(x => x.ClinicId == entityId);
            query = query.Include(x => x.Clinic);
        }
        else
        {
            query = query.Where(x => x.UserId == entityId);
            query = query.Include(x => x.Clinic);
        }
        
        var userConversations = await query.Select(x => new ResponseModel.ConversationResponseModel(
            x.ConversationId,
            isClinic 
                ? (x.Clinic == null ? Guid.Empty : x.Clinic.Id) 
                : (x.User == null ? Guid.Empty : x.User.Id),
            isClinic 
                ? (x.Clinic == null ? "FriendName" : x.Clinic.Name) 
                : (x.User == null ? "FriendName" : x.User.FullName),
    
            // Determine the profile picture URL based on whether it's a clinic or user
            isClinic 
                ? (x.Clinic == null ? "" : x.Clinic.ProfilePictureUrl)
                : (x.User == null ? "" : x.User.ProfilePicture)
            
        )).ToListAsync();
        
        return Result.Success(userConversations);
    }
}