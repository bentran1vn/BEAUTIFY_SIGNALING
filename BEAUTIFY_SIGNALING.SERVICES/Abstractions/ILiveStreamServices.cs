using BEAUTIFY_PACKAGES.BEAUTIFY_PACKAGES.CONTRACT.Abstractions.Shared;
using BEAUTIFY_SIGNALING.SERVICES.Services.LiveStreams;

namespace BEAUTIFY_SIGNALING.SERVICES.Abstractions;

public interface ILiveStreamServices
{
    Task<Result<List<ResponseModel.GetAllLiveStream>>> GetAllLiveStream(Guid? clinicId, string? role);
}