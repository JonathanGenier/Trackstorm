using Godot;
using Trackstorm.Core.Networking.Transport;

namespace Trackstorm.Client.Networking;

/// <summary>Owns native polling and cleanup on Godot's main thread, independent of simulation ticks.</summary>
public sealed partial class NetworkTransportNode : Node
{
    private ITransportGateway? _gateway;

    /// <summary>Gets the transport-neutral gateway after this node enters the scene tree.</summary>
    public ITransportGateway Gateway => _gateway ?? throw new InvalidOperationException("The transport node is not ready.");

    /// <inheritdoc/>
    public override void _Ready()
    {
        _gateway = new GameNetworkingSocketsTransport();
        _gateway.ConnectionChanged += LogConnection;
    }

    /// <inheritdoc/>
    public override void _Process(double delta) => _gateway?.Poll();

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        _gateway?.Dispose();
        _gateway = null;
    }

    private static void LogConnection(TransportConnectionChange change)
    {
        GD.Print($"Transport peer {change.RemotePeerId}: {change.State}; {change.Reason}; {change.Detail}");
    }
}
