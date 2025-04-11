namespace BEAUTIFY_SIGNALING.SERVICES.Services.ChatServices;

public class ResponseModel
{
    public record ConversationResponseModel(
        Guid ConversationId, Guid EntityId ,string? FriendName, string? FriendImageUrl
    );
    
    public record MessageResponseModel(
        Guid Id,
        Guid ConversationId,
        Guid SenderId,
        bool IsClinic,
        string SenderName,
        string SenderImageUrl,
        string Content,
        DateTimeOffset CreatedOnUtc
    );
}