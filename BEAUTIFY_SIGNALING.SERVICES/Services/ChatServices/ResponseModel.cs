namespace BEAUTIFY_SIGNALING.SERVICES.Services.ChatServices;

public class ResponseModel
{
    public record ConversationResponseModel(
        Guid ConversationId, Guid EntityId ,string? FriendName, string? FriendImageUrl
    );
}