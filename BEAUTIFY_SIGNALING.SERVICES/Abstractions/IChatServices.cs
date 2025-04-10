using BEAUTIFY_PACKAGES.BEAUTIFY_PACKAGES.CONTRACT.Abstractions.Shared;
using BEAUTIFY_SIGNALING.REPOSITORY.Entities;
using BEAUTIFY_SIGNALING.SERVICES.Services.ChatServices;

namespace BEAUTIFY_SIGNALING.SERVICES.Abstractions;

public interface IChatServices
{
    public Task<Result<List<Message>>> GetAllMessageOfConversation(Guid conversationId);
    public Task<Result<List<ResponseModel.ConversationResponseModel>>> GetAllConversationOfEntity(Guid entityId, bool isClinic);
}