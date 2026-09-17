using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Trackstorm.Core.Sessions;

namespace Trackstorm.LeaseService;

/// <summary>Authenticated coordination endpoint. No gameplay state or EOS SDK dependency is hosted here.</summary>
internal static class Program
{
    /// <summary>Builds the same authenticated HTTP host used by the executable and integration tests.</summary>
    /// <param name="args">Operator configuration.</param>
    /// <returns>The configured host.</returns>
    internal static WebApplication Create(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 2048);
        string Required(string key) => builder.Configuration[key] is { Length: > 0 } value ? value : throw new InvalidOperationException($"Missing configuration: {key}");
        string issuer = Required("Lease:Issuer");
        string audience = Required("Lease:Audience");
        string jwks = Required("Lease:Jwks");
        string deployment = Required("Lease:Deployment");
        if (!Uri.TryCreate(jwks, UriKind.Absolute, out var keys) || keys.Scheme != "https")
        {
            throw new InvalidOperationException("Lease:Jwks must use HTTPS.");
        }

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(jwks, new JwksRetriever(issuer), new HttpDocumentRetriever { RequireHttps = true });
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                RequireExpirationTime = true,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
                RequireSignedTokens = true,
                ValidateIssuerSigningKey = true,
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            };
        });
        builder.Services.AddAuthorization(options => options.AddPolicy("lease", policy => policy.RequireAuthenticatedUser().RequireClaim("pfdid", deployment).RequireClaim("sub")));
        string ledger = Required("Lease:Ledger");
        builder.Services.AddSingleton(_ => new LeaseStore(ledger));
        var app = builder.Build();
        _ = app.Services.GetRequiredService<LeaseStore>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/health", () => Results.Ok());
        app.MapPost("/lease/{operation}", (string operation, LeaseRequest request, HttpContext context, LeaseStore store) =>
        {
            if (!context.Request.IsHttps && context.Connection.RemoteIpAddress is { } address && !System.Net.IPAddress.IsLoopback(address))
            {
                return Results.StatusCode(400);
            }

            context.Response.Headers.CacheControl = "no-store";
            try
            {
                var result = operation == "read" ? store.Read(request.Session) : store.Execute(operation, request, context.User.FindFirst("sub")!.Value);
                return result is null ? Results.Conflict() : Results.Ok(result);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return Results.StatusCode(503);
            }
        }).RequireAuthorization("lease");
        return app;
    }

    private static void Main(string[] args) => Create(args).Run();
}
