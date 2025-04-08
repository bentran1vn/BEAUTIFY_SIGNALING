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
    private readonly IRepositoryBase<UserClinic, Guid> _userClinicRepository;
    private readonly IRepositoryBase<ClinicService, Guid> _clinicServiceRepository;
    private readonly IRepositoryBase<Clinic, Guid> _clinicRepository;

    public LiveStreamServices(IRepositoryBase<LivestreamRoom, Guid> liveStreamRepository, IRepositoryBase<UserClinic, Guid> userClinicRepository, IRepositoryBase<ClinicService, Guid> clinicServiceRepository, IRepositoryBase<Clinic, Guid> clinicRepository)
    {
        _liveStreamRepository = liveStreamRepository;
        _userClinicRepository = userClinicRepository;
        _clinicServiceRepository = clinicServiceRepository;
        _clinicRepository = clinicRepository;
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
            new ResponseModel.GetAllLiveStream(x.Id, x.Name, x.Description, x.Image,
                x.CreatedOnUtc, (Guid)x.ClinicId!, x.Clinic.Name)).ToList();
        
        return Result.Success(result);
    }
    public async Task<Result<List<ResponseModel.GetAllService>>> GetAllServices(Guid clinicId, Guid staffId, Guid roomId)
    {
        var clinics = await _clinicRepository.FindAll(x => x.ParentId.Equals(clinicId) || x.Id.Equals(clinicId)).ToListAsync();

        var clinicIds = clinics.Select(x => x.Id);
        
        var query = _userClinicRepository.FindAll(
            x => x.ClinicId.Equals(clinicId) && x.UserId.Equals(staffId));

        query.Include(x => x.User)
            .ThenInclude(y => y!.Role);

        var staff = await query.FirstOrDefaultAsync(new CancellationToken());

        if (staff == null)
            return Result.Failure<List<ResponseModel.GetAllService>>(new Error("404", "Staff not exist !"));
        
        if(!(bool)staff.User?.Role?.Name.Equals("Clinic Admin"))
            return Result.Failure<List<ResponseModel.GetAllService>>(new Error("401", "Staff Unauthorized !"));

        var servicesQuery = _clinicServiceRepository.FindAll(x => clinicIds.Contains(x.ClinicId));

        servicesQuery = servicesQuery
            .Include(x => x.Services)
            .ThenInclude(x => x.ServiceMedias)
            .Include(x => x.Services)
            .ThenInclude(x => x.Promotions);

        servicesQuery = servicesQuery
            .GroupBy(x => x.ServiceId)
            .Select(g => g.FirstOrDefault()!);

        var services = await servicesQuery.ToListAsync(new CancellationToken());

        var result = services.Select(x => new ResponseModel.GetAllService(
            x.ServiceId, x.Services.Name, x.Services.Description, x.Services.ServiceMedias?.Select(y => y.ImageUrl).ToList() ?? [],
            x.Services.MaxPrice, x.Services.MinPrice,
            x.Services?.Promotions?.FirstOrDefault(
                y => 
                    y.ServiceId.Equals(x.ServiceId) &&
                    y.LivestreamRoomId.Equals(roomId) &&
                    y.IsActivated &&
                    !y.IsDeleted)?.DiscountPercent ?? 0.0 ,
            new ResponseModel.Category(
                x.Services!.Category!.Id, x.Services.Category.Name,
                x.Services.Category.Description
                )
            )).ToList();

        return Result.Success(result);
    }
}