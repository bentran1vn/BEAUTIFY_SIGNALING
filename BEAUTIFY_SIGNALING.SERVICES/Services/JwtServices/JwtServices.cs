using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BEAUTIFY_SIGNALING.SERVICES.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace BEAUTIFY_SIGNALING.SERVICES.Services.JwtServices;

public class JwtServices: IJwtServices
{
    private readonly JwtOptions _jwtOption = new();

    public JwtServices(IConfiguration configuration)
    {
        configuration.GetSection(nameof(JwtOptions)).Bind(_jwtOption);
    }
    
    public ClaimsPrincipal? VerifyForgetToken(string token)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_jwtOption.SecretKey);

        var validationParameters = new TokenValidationParameters()
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ValidateIssuer = true,
            ValidIssuer = _jwtOption.Issuer, // Match the issuer
            ValidateAudience = true,
            ValidAudience = _jwtOption.Audience, // Match the audience
            ClockSkew = TimeSpan.Zero // No tolerance for expiration
        };

        try
        {
            var principal = tokenHandler.ValidateToken(token, validationParameters, out SecurityToken validatedToken);
            return principal; // Return the claims if token is valid
        }
        catch (SecurityTokenException ex)
        {
            Console.WriteLine($"Token validation failed: {ex.Message}");
            return null;
        }
    }
}