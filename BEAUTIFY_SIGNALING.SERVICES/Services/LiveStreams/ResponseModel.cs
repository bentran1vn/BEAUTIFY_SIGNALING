namespace BEAUTIFY_SIGNALING.SERVICES.Services.LiveStreams;

public class ResponseModel
{
    public record GetAllLiveStream(
        Guid Id,
        string Name,
        DateTimeOffset StartDate,
        Guid ClinicId,
        string ClinicName
    );
}