using BEAUTIFY_PACKAGES.BEAUTIFY_PACKAGES.CONTRACT.Abstractions.Shared;
using BEAUTIFY_SIGNALING.SERVICES.Abstractions;
using BEAUTIFY_SIGNALING.SERVICES.Services.ChatServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BEAUTIFY_SIGNALING.API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ChatController: ControllerBase
{
    private readonly IChatServices _chatServices;

    public ChatController(IChatServices chatServices)
    {
        _chatServices = chatServices;
    }
    
    [HttpGet("Conversations/{entityId}")]
    public async Task<IResult> GetAllConversation(Guid entityId, bool isClinic)
    {
        var result = await _chatServices.GetAllConversationOfEntity(entityId, isClinic);
        return result.IsFailure ? HandlerFailure(result) : Results.Ok(result);
    }
    
    [HttpGet("Messages/{conversationId}")]
    public async Task<IResult> GetAllMessages(Guid conversationId)
    {
        var result = await _chatServices.GetAllMessageOfConversation(conversationId);
        return result.IsFailure ? HandlerFailure(result) : Results.Ok(result);
    }
    
    [Authorize]
    [HttpPost]
    public async Task<IResult> SendMessage([FromBody] RequestModel.SendMessageRequestModel requestModel)
    {
        var userId = User.FindFirst(c => c.Type == "UserId")?.Value;
        var clinicId = User.FindFirst(c => c.Type == "ClinicId")?.Value;
        if (userId == null)
        {
            return Results.Unauthorized();
        }

        var result = await _chatServices.SendMessage(requestModel.IsClinic ? clinicId != null ? new Guid(clinicId) : Guid.NewGuid() : new Guid(userId) , requestModel.EntityId, requestModel.Content, requestModel.IsClinic);
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