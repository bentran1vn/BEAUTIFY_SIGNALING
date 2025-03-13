using BEAUTIFY_PACKAGES.BEAUTIFY_PACKAGES.CONTRACT.Abstractions.Shared;
using BEAUTIFY_PACKAGES.BEAUTIFY_PACKAGES.DOMAIN.Abstractions.Repositories;
using BEAUTIFY_SIGNALING.REPOSITORY.Entities;
using BEAUTIFY_SIGNALING.SERVICES.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace BEAUTIFY_SIGNALING.SERVICES.Services.LiveStreams;

public class LiveStreamServices : ILiveStreamServices
{
    private readonly IRepositoryBase<LivestreamRoom, Guid> _liveStreamRepository;

    public LiveStreamServices(IRepositoryBase<LivestreamRoom, Guid> liveStreamRepository)
    {
        _liveStreamRepository = liveStreamRepository;
    }

    public async Task<Result<List<ResponseModel.GetAllLiveStream>>> GetAllLiveStream(Guid? clinicId)
    {
        var query = _liveStreamRepository.FindAll(x => x.IsDeleted == false);

        query = query.Include(x => x.Clinic);

        if (clinicId.HasValue)
        {
            query = query.Where(x => x.ClinicId == clinicId);
        }

        var liveStreamList = await query.ToListAsync();

        var result = liveStreamList.Select(x => 
            new ResponseModel.GetAllLiveStream(x.Id, x.Name, x.CreatedOnUtc, (Guid)x.ClinicId!, x.Clinic.Name)).ToList();
        
        return Result.Success(result);
    }
}