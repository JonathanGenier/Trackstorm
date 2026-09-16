using Trackstorm.Core.Networking.Transport;

namespace Trackstorm.Client.Networking;

/// <summary>A fresh presentation projection, never a retained latency cache.</summary>
internal readonly record struct ConnectionDiagnostic(ConnectionDiagnosticState State, TransportStatistics Statistics, string? Transport = null);
