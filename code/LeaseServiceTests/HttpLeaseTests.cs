using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Trackstorm.Core.Sessions;

namespace Trackstorm.LeaseService.Tests;

/// <summary>Exercises the real HTTP middleware with locally signed identity tokens and isolated storage.</summary>
[TestFixture]
internal sealed class HttpLeaseTests
{
    /// <summary>Signature, issuer, audience, lifetime and deployment constrain the authenticated holder.</summary>
    /// <returns>The asynchronous HTTP verification.</returns>
    [Test]
    public async Task AuthenticationAndConditionalWritesUseVerifiedSubject()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "http-lease-" + Guid.NewGuid().ToString("N"));
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "test" };
        await using var app = Program.Create(["--urls=http://127.0.0.1:0", "--Lease:Issuer=https://identity.test", "--Lease:Audience=client", "--Lease:Jwks=https://identity.test/keys", "--Lease:Deployment=development", "--Lease:Ledger=" + Path.Combine(directory, "ledger.json")]);
        var configuration = new OpenIdConnectConfiguration { Issuer = "https://identity.test" };
        configuration.SigningKeys.Add(key);
        app.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme).ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
        try
        {
            await app.StartAsync();
            using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            var request = new LeaseRequest(new string('C', 64), 0, string.Empty);
            using (var anonymous = await http.PostAsJsonAsync("lease/create", request))
            {
                Assert.That(anonymous.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            }

            foreach (var invalid in new[] { Token(issuer: "wrong"), Token(audience: "wrong"), Token(expired: true), Token(deployment: "wrong") })
            {
                http.DefaultRequestHeaders.Authorization = new("Bearer", invalid);
                using var rejected = await http.PostAsJsonAsync("lease/create", request);
                Assert.That(rejected.StatusCode, Is.AnyOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden));
            }

            using var otherRsa = RSA.Create(2048);
            http.DefaultRequestHeaders.Authorization = new("Bearer", Token(signingKey: new RsaSecurityKey(otherRsa) { KeyId = "test" }));
            using (var forged = await http.PostAsJsonAsync("lease/create", request))
            {
                Assert.That(forged.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            }

            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token());
            using var created = await http.PostAsJsonAsync("lease/create", request);
            Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(created.Headers.CacheControl!.NoStore, Is.True);
            var grant = (await created.Content.ReadFromJsonAsync<AuthorityLease>())!;
            Assert.That(grant.Holder, Is.EqualTo("host"));
            Assert.That(grant.Epoch, Is.EqualTo(1));
            http.DefaultRequestHeaders.Authorization = new("Bearer", Token(subject: "client"));
            using var impostor = await http.PostAsJsonAsync("lease/renew", new LeaseRequest(grant.Session, grant.Epoch, grant.Token));
            Assert.That(impostor.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            using var malformed = await http.PostAsJsonAsync("lease/read", new { Session = (string?)null });
            Assert.That(malformed.IsSuccessStatusCode, Is.False);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
            Directory.Delete(directory, true);
        }

        string Token(string issuer = "https://identity.test", string audience = "client", string deployment = "development", string subject = "host", bool expired = false, SecurityKey? signingKey = null)
        {
            var token = new JwtSecurityToken(issuer, audience, [new Claim("sub", subject), new Claim("pfdid", deployment)], DateTime.UtcNow.AddMinutes(-2), DateTime.UtcNow.AddMinutes(expired ? -1 : 2), new SigningCredentials(signingKey ?? key, SecurityAlgorithms.RsaSha256));
            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
