using System.Text;
using BEAUTIFY_PACKAGES.BEAUTIFY_PACKAGES.DOMAIN.Constrants;
using BEAUTIFY_PACKAGES.BEAUTIFY_PACKAGES.INFRASTRUCTURE.DependencyInjection.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace BEAUTIFY_SIGNALING.API.Extensions;
public static class JwtExtensions
{
    public static void AddJwtAuthenticationAPI(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
        }).AddJwtBearer(o =>
        {
            var jwtOption = new JwtOption();
            configuration.GetSection(nameof(JwtOption)).Bind(jwtOption);

            /**
             * Storing the JWT in the AuthenticationProperties allows you to retrieve it from elsewhere within your application.
             * public async Task<IActionResult> SomeAction()
                {
                    // using Microsoft.AspNetCore.Authentication;
                    var accessToken = await HttpContext.GetTokenAsync("access_token");
                    // ...
                }
             */
            o.SaveToken = true; // Save token into AuthenticationProperties

            var Key = Encoding.UTF8.GetBytes(jwtOption.SecretKey);
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, // on production make it true
                ValidateAudience = true, // on production make it true
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtOption.Issuer,
                ValidAudience = jwtOption.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Key),
                ClockSkew = TimeSpan.Zero
            };

            o.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = context =>
                {
                    if (context.Exception.GetType() == typeof(SecurityTokenExpiredException))
                        context.Response.Headers.Add("IS-TOKEN-EXPIRED", "true");
                    return Task.CompletedTask;
                }
            };

            //o.EventsType = typeof(CustomJwtBearerEvents);
        });

        services.AddAuthorizationBuilder()
            .AddPolicy(Constant.Role.CLINIC_STAFF, policy => policy.RequireRole(Constant.Role.CLINIC_STAFF))
            .AddPolicy(Constant.Role.CLINIC_ADMIN, policy => policy.RequireRole(Constant.Role.CLINIC_ADMIN))
            .AddPolicy(Constant.Role.DOCTOR, policy => policy.RequireRole(Constant.Role.DOCTOR))
            .AddPolicy(Constant.Role.CUSTOMER, policy => policy.RequireRole(Constant.Role.CUSTOMER))
            .AddPolicy(Constant.Role.SYSTEM_ADMIN, policy => policy.RequireRole(Constant.Role.SYSTEM_ADMIN))
            .AddPolicy(Constant.Role.SYSTEM_STAFF, policy => policy.RequireRole(Constant.Role.SYSTEM_STAFF));
        // services.AddScoped<CustomJwtBearerEvents>();
    }
}