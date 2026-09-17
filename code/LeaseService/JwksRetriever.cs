using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Trackstorm.LeaseService;

/// <summary>Loads signing keys only from the operator-configured HTTPS endpoint, never token-supplied URLs.</summary>
internal sealed class JwksRetriever(string issuer) : IConfigurationRetriever<OpenIdConnectConfiguration>
{
    /// <inheritdoc />
    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(string address, IDocumentRetriever retriever, CancellationToken cancel)
    {
        string json = await retriever.GetDocumentAsync(address, cancel);
        var result = new OpenIdConnectConfiguration { Issuer = issuer };
        foreach (var key in new JsonWebKeySet(json).GetSigningKeys())
        {
            result.SigningKeys.Add(key);
        }

        return result;
    }
}
