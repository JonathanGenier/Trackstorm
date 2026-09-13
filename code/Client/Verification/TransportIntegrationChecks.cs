using System.Diagnostics;
using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Networking.Transport;

namespace Trackstorm.Client.Verification;

/// <summary>Exercises production transport nodes through Godot polling, removal, recreation and native shutdown.</summary>
public sealed partial class TransportIntegrationChecks : Node
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private NetworkTransportNode _host = null!;
    private NetworkTransportNode _client = null!;
    private ITransportGateway? _previous;
    private ulong _peer;
    private int _cycles;
    private bool _sent;

    /// <inheritdoc/>
    public override void _Ready() => StartCycle();

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        try
        {
            if (_clock.Elapsed.TotalSeconds > 20)
            {
                throw new InvalidOperationException("Godot transport check timed out.");
            }

            if (!_sent && _client.Gateway.ConnectionState == TransportConnectionState.Connected && _host.Gateway.ConnectionState == TransportConnectionState.Connected)
            {
                _client.Gateway.Send(new(_peer, new byte[] { 12, 13 }));
                _sent = true;
            }

            if (!_host.Gateway.TryReceive(out TransportMessage message))
            {
                return;
            }

            if (!message.Payload.Span.SequenceEqual(new byte[] { 12, 13 }) || message.Delivery != TransportDelivery.Reliable)
            {
                throw new InvalidOperationException("Godot transport payload mismatch.");
            }

            _previous = _host.Gateway;
            _host.Free();
            _client.Free();
            if (_previous.Connections.Count != 0 || _previous.IsListening)
            {
                throw new InvalidOperationException("Scene exit retained transport resources.");
            }

            if (++_cycles == 3)
            {
                GD.Print("Transport Godot integration passed: 3 node creation/poll/message/cleanup cycles.");
                SetProcess(false);
                GetTree().Quit();
                return;
            }

            StartCycle();
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString());
            SetProcess(false);
            GetTree().Quit(1);
        }
    }

    private void StartCycle()
    {
        _host = new NetworkTransportNode();
        _client = new NetworkTransportNode();
        AddChild(_host);
        AddChild(_client);
        _host.Gateway.Listen("127.0.0.1:27932");
        _peer = _client.Gateway.Connect("127.0.0.1:27932");
        _sent = false;
    }
}
