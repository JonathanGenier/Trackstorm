namespace Trackstorm.Core.Networking.Transport;

/// <summary>Features which callers may request from the selected transport.</summary>
[Flags]
public enum TransportCapabilities
{
    /// <summary>No optional features.</summary>
    None = 0,
    /// <summary>Native round-trip samples are available.</summary>
    Ping = 1,
    /// <summary>Native delivery-quality samples are available.</summary>
    ConnectionQuality = 2,
    /// <summary>Native artificial network conditions can be configured.</summary>
    NetworkSimulation = 4,
}
