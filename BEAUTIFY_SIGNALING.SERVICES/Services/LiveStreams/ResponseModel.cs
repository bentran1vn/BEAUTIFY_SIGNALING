using BEAUTIFY_PACKAGES.BEAUTIFY_PACKAGES.CONTRACT.Abstractions.Shared;

namespace BEAUTIFY_SIGNALING.SERVICES.Services.LiveStreams;

public class ResponseModel
{
    public record GetAllLiveStream(
        Guid Id,
        string Name,
        string? Description,
        string? Image,
        DateTimeOffset StartDate,
        Guid ClinicId,
        string ClinicName
    );
    
    public record GetLiveStreamDetail(
        int JoinCount, 
        int MessageCount,
        int ReactionCount,
        int TotalActivities,
        int TotalBooking,
        PagedResult<LivestreamLog> Logs);

    public record LivestreamLog(
        Guid Id, Guid? UserId, string? Email, string? FullName,
        string? Phone, string? ProfilePictureUrl, string LogType,
        string? Message, DateTimeOffset CreatedOnUtc);
    
    public record GetAllService(
        Guid Id,
        string Name,
        string Description,
        List<string> Images,
        decimal MaxPrice,
        decimal MinPrice,
        double DiscountLivePercent,
        Category Category
    );
    
    public record Category(
        Guid Id,
        string Name,
        string? Description
    );
}