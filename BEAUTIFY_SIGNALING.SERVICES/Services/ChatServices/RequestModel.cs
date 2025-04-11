namespace BEAUTIFY_SIGNALING.SERVICES.Services.ChatServices;

public class RequestModel
{
    public record SendMessageRequestModel(
        Guid EntityId,
        string Content,
        bool IsClinic
    );
}