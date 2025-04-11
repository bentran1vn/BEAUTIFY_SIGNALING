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
    private readonly ApplicationDbContext _dbContext;

    public ChatServices(IRepositoryBase<Message, Guid> messageRepository, IRepositoryBase<UserConversation, Guid> userConversationnRepository, ApplicationDbContext dbContext, IRepositoryBase<Conversation, Guid> conversationRepository)
    {
        _messageRepository = messageRepository;
        _userConversationRepository = userConversationnRepository;
        _dbContext = dbContext;
        _conversationRepository = conversationRepository;
    }

    public async Task<Result<List<Message>>> GetAllMessageOfConversation(Guid conversationId)
    {
        var query = _messageRepository.FindAll(x => x.IsDeleted == false);
        query = query.Where(x => x.ConversationId == conversationId);
        query = query.OrderByDescending(x => x.CreatedOnUtc);
        
        var messages = await query.ToListAsync();
        
        return Result.Success(messages);
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