using BEAUTIFY_PACKAGES.BEAUTIFY_PACKAGES.CONTRACT.Abstractions.Shared;
using BEAUTIFY_PACKAGES.BEAUTIFY_PACKAGES.DOMAIN.Abstractions.Repositories;
using BEAUTIFY_SIGNALING.REPOSITORY.Entities;
using BEAUTIFY_SIGNALING.SERVICES.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace BEAUTIFY_SIGNALING.SERVICES.Services.ChatServices;

public class ChatServices : IChatServices
{
    private readonly IRepositoryBase<UserConversation, Guid> _userConversationRepository;
    private readonly IRepositoryBase<Message, Guid> _messageRepository;

    public ChatServices(IRepositoryBase<Message, Guid> messageRepository, IRepositoryBase<UserConversation, Guid> userConversationnRepository)
    {
        _messageRepository = messageRepository;
        _userConversationRepository = userConversationnRepository;
    }

    public async Task<Result<List<Message>>> GetAllMessageOfConversation(Guid conversationId)
    {
        var query = _messageRepository.FindAll(x => x.IsDeleted == false);
        query = query.Where(x => x.ConversationId == conversationId);
        query = query.OrderByDescending(x => x.CreatedOnUtc);
        
        var messages = await query.ToListAsync();
        
        return Result.Success(messages);
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