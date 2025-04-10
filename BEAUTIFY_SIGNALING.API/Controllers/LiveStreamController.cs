using BEAUTIFY_PACKAGES.BEAUTIFY_PACKAGES.CONTRACT.Abstractions.Shared;
using BEAUTIFY_SIGNALING.SERVICES.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace BEAUTIFY_SIGNALING.API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class LiveStreamController: ControllerBase
{
    private readonly ILiveStreamServices _liveStreamServices;
    private readonly HttpContext _httpContext;
    
    public LiveStreamController(ILiveStreamServices liveStreamServices, HttpContext httpContext)
    {
        _liveStreamServices = liveStreamServices;
        _httpContext = httpContext;
    }

    [HttpGet("Rooms")]
    public async Task<IResult> GetAllRooms(Guid? clinicId)

    {
        var role = _httpContext.User.FindFirst(c => c.Type == "RoleName")?.Value;
        var result = await _liveStreamServices.GetAllLiveStream(clinicId, role);
        return result.IsFailure ? HandlerFailure(result) : Results.Ok(result);
    }
    
    [HttpGet("Services")]
    public async Task<IResult> GetAllServices(Guid clinicId, Guid userId, Guid roomId)
    {
        var result = await _liveStreamServices.GetAllServices(clinicId, userId, roomId);
        return result.IsFailure ? HandlerFailure(result) : Results.Ok(result);
    }
    
    public static IResult HandlerFailure(Result result) =>
        result switch
        {
            { IsSuccess: true } => throw new InvalidOperationException(),
            IValidationResult validationResult =>
                Results.BadRequest(
                    CreateProblemDetails(
                        "Validation Error", StatusCodes.Status422UnprocessableEntity,
                        result.Error,
                        validationResult.Errors)),
            _ =>
                Results.BadRequest(
                    CreateProblemDetails(
                        "Bab Request", StatusCodes.Status400BadRequest,
                        result.Error))
        };
    
    private static ProblemDetails CreateProblemDetails(string title, int status, Error error, Error[]? errors = null)
        => new()
        {
            Title = title,
            Type = error.Code,
            Detail = error.Message,
            Status = status,
            Extensions = { { nameof(errors), errors } }
        };
}

