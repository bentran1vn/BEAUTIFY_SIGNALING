using BEAUTIFY_PACKAGES.BEAUTIFY_PACKAGES.CONTRACT.Abstractions.Shared;
using BEAUTIFY_PACKAGES.BEAUTIFY_PACKAGES.DOMAIN.Abstractions.Repositories;
using BEAUTIFY_SIGNALING.REPOSITORY.Entities;
using BEAUTIFY_SIGNALING.SERVICES.Abstractions;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver.Linq;

namespace BEAUTIFY_SIGNALING.SERVICES.Services.LiveStreams;

public class LiveStreamServices : ILiveStreamServices
{
    private readonly IRepositoryBase<LivestreamRoom, Guid> _liveStreamRepository;

    public LiveStreamServices(IRepositoryBase<LivestreamRoom, Guid> liveStreamRepository)
    {
        _liveStreamRepository = liveStreamRepository;
    }

    public async Task<Result<List<ResponseModel.GetAllLiveStream>>> GetAllLiveStream(Guid? clinicId, string? role)
    {
        var query = _liveStreamRepository.FindAll(x => x.IsDeleted == false);

        query = query.Include(x => x.Clinic);
        
        query = query
            .OrderByDescending(x => x.Date)
            .ThenByDescending(y => y.StartDate);

        if (clinicId.HasValue)
        {
            query = query.Where(x => x.ClinicId == clinicId);
        }
        
        if (role == null || !role.Equals("Clinic Admin"))
        {
            query = query.Where(x => x.EndDate == null && x.Status == "live");
        }
        
        var liveStreamList = await query.ToListAsync();

        var result = liveStreamList.Select(x => 
            new ResponseModel.GetAllLiveStream(x.Id, x.Name, x.CreatedOnUtc, (Guid)x.ClinicId!, x.Clinic.Name)).ToList();
        
        return Result.Success(result);
    }
}