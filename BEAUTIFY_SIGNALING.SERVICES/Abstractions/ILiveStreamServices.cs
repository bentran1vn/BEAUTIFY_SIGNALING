using BEAUTIFY_PACKAGES.BEAUTIFY_PACKAGES.CONTRACT.Abstractions.Shared;
using BEAUTIFY_SIGNALING.SERVICES.Services.LiveStreams;

namespace BEAUTIFY_SIGNALING.SERVICES.Abstractions;

public interface ILiveStreamServices
{
    Task<Result<PagedResult<ResponseModel.GetAllLiveStream>>> GetAllLiveStream(Guid? clinicId, string? role, int pageSize, int pageIndex);
    Task<Result<ResponseModel.GetLiveStreamDetail>> GetLiveStreamId(Guid roomId, string? role, int? type, int pageSize, int pageIndex);
    Task<Result<List<ResponseModel.GetAllService>>> GetAllServices(Guid clinicId, Guid userId,  Guid roomId);
}