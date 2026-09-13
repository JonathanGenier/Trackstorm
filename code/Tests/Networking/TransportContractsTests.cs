using Trackstorm.Core.Networking.Transport;

namespace Trackstorm.Core.Tests.Networking;

/// <summary>
/// Verifies the pure-data native transport boundary.
/// </summary>
[TestFixture]
internal sealed class TransportContractsTests
{
    /// <summary>
    /// Verifies that a transport adapter can exchange opaque Core messages without native types.
    /// </summary>
    [Test]
    public void Gateway_ExchangesOpaqueMessagesAndConnectionState()
    {
        var gateway = new RecordingTransportGateway();
        byte[] payload = [1, 3, 5, 7];
        var message = new TransportMessage(42, payload);

        gateway.Send(message);
        bool received = gateway.TryReceive(out TransportMessage result);

        Assert.Multiple(() =>
        {
            Assert.That(gateway.ConnectionState, Is.EqualTo(TransportConnectionState.Connected));
            Assert.That(received, Is.True);
            Assert.That(result.RemotePeerId, Is.EqualTo(42));
            Assert.That(result.Payload.ToArray(), Is.EqualTo(payload));
        });
    }

    private sealed class RecordingTransportGateway : ITransportGateway
    {
        private TransportMessage? _message;

        public event Action<TransportConnectionChange>? ConnectionChanged
        {
            add { }
            remove { }
        }

        public bool IsListening => false;

        public IReadOnlyDictionary<ulong, TransportConnectionState> Connections => new Dictionary<ulong, TransportConnectionState>();

        /// <inheritdoc/>
        public TransportConnectionState ConnectionState => TransportConnectionState.Connected;

        public void Listen(string address) => throw new NotSupportedException();

        public ulong Connect(string address) => throw new NotSupportedException();

        public void Disconnect(ulong peerId) => throw new NotSupportedException();

        public void Poll()
        {
        }

        public void Stop()
        {
        }

        public void Dispose()
        {
        }

        public TransportStatistics GetStatistics(ulong peerId) => default;

        public void ConfigureSimulation(NetworkSimulation simulation) => throw new NotSupportedException();

        /// <inheritdoc/>
        public void Send(TransportMessage message)
        {
            _message = message;
        }

        /// <inheritdoc/>
        public bool TryReceive(out TransportMessage message)
        {
            if (_message is null)
            {
                message = default;
                return false;
            }

            message = _message.Value;
            _message = null;
            return true;
        }
    }
}
