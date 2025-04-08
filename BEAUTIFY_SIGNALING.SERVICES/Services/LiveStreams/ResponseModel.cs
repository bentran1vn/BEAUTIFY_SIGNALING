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