namespace Trackstorm.Client.Online;

/// <summary>Authenticated routing observation with no lease secret or simulation permission.</summary>
/// <param name="RoutingId">Opaque read-only locator.</param>
/// <param name="Holder">Current authenticated gameplay host subject.</param>
/// <param name="Epoch">Current fenced authority epoch.</param>
/// <param name="RemainingSeconds">Service-observed remaining lease duration.</param>
internal sealed record LeaseRoute(string RoutingId, string Holder, ulong Epoch, double RemainingSeconds);
