using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Client.Online;

/// <summary>HTTPS-only lease adapter. Tokens are copied on the EOS owner thread and never persisted or logged.</summary>
internal sealed class HttpLeaseTransport : ILeaseTransport
{
    private readonly HttpClient _http;
    private readonly Func<string?> _token;

    /// <summary>Creates a bounded HTTPS connection.</summary>
    /// <param name="endpoint">Trusted service base URL.</param>
    /// <param name="token">Owner-thread token source.</param>
    internal HttpLeaseTransport(Uri endpoint, Func<string?> token)
    {
        if (endpoint.Scheme != "https" || !string.IsNullOrEmpty(endpoint.UserInfo) || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
        {
            throw new ArgumentException("Authority lease endpoint must be HTTPS.");
        }

        _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = endpoint, Timeout = TimeSpan.FromSeconds(3), MaxResponseContentBufferSize = 4096 };
        _token = token;
    }

    /// <inheritdoc />
    public Task<AuthorityLease?> Send(string operation, LeaseRequest request)
    {
        string? token = _token();
        return token is null ? Task.FromResult<AuthorityLease?>(null) : SendAsync<AuthorityLease>(operation, request, token);
    }

    /// <inheritdoc />
    public Task<LeaseRoute?> Resolve(string routingId)
    {
        string? token = _token();
        return token is null ? Task.FromResult<LeaseRoute?>(null) : SendAsync<LeaseRoute>("route", new(routingId, 0, string.Empty), token);
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    private async Task<T?> SendAsync<T>(string operation, LeaseRequest request, string token)
        where T : class
    {
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "lease/" + operation) { Content = JsonContent.Create(request) };
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _http.SendAsync(message).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<T>().ConfigureAwait(false) : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or ObjectDisposedException)
        {
            return null;
        }
    }
}
