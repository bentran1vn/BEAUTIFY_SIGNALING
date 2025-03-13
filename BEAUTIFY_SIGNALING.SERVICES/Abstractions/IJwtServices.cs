using System.Security.Claims;

namespace BEAUTIFY_SIGNALING.SERVICES.Abstractions;

public interface IJwtServices
{
    public ClaimsPrincipal? VerifyForgetToken(string token);
}