using Trackstorm.Core.Networking.Transport;

namespace Trackstorm.Client.Development;

/// <summary>Explicit local capability gate; unsupported transports are never called or reported as configured.</summary>
internal static class NetworkSimulationControl
{
    /// <summary>Reports whether the current transport implements network simulation.</summary>
    /// <returns>Whether the current provider supports simulation.</returns>
    /// <param name="gateway">Current provider-neutral transport.</param>
    internal static bool Supported(ITransportGateway? gateway) => gateway?.Capabilities.HasFlag(TransportCapabilities.NetworkSimulation) == true;

    /// <summary>Applies supported simulation controls only for the current host.</summary>
    /// <returns>Whether the operation was accepted.</returns>
    /// <param name="gateway">Current provider-neutral transport.</param>
    /// <param name="host">Whether the caller currently owns authority.</param>
    /// <param name="simulation">Validated process-local impairment settings.</param>
    internal static bool TryApply(ITransportGateway? gateway, bool host, NetworkSimulation simulation)
    {
        if (!host || !Supported(gateway))
        {
            return false;
        }

        try
        {
            gateway!.ConfigureSimulation(simulation);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
