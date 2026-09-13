using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Input;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Transport.Tests;

/// <summary>Real UDP replication with controlled native impairment and a deterministic flat-ground collision seam.</summary>
[TestFixture]
[Category("Native")]
[NonParallelizable]
internal sealed class NativeVehicleReplicationTests
{
    /// <summary>Exercises production transport/driver prediction and reconciliation under native network conditions.</summary>
    /// <param name="players">Total local instances including the host.</param>
    /// <param name="latency">Per-direction native outbound delay in milliseconds.</param>
    /// <param name="jitter">Average outbound jitter in milliseconds.</param>
    /// <param name="loss">Outbound packet loss percentage.</param>
    [TestCase(2, 0, 0, 0f)]
    [TestCase(2, 30, 0, 0f)]
    [TestCase(2, 50, 10, 0f)]
    [TestCase(2, 0, 0, 2f)]
    [TestCase(8, 30, 10, 2f)]
    public void VehicleLoopConvergesAcrossNetworkConditions(int players, int latency, int jitter, float loss)
    {
        var gateways = new List<GameNetworkingSocketsTransport>();
        try
        {
            var hostGateway = new GameNetworkingSocketsTransport();
            gateways.Add(hostGateway);
            using var reservation = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            int port = ((IPEndPoint)reservation.Client.LocalEndPoint!).Port;
            reservation.Close();
            string endpoint = $"127.0.0.1:{port}";
            hostGateway.Listen(endpoint);
            hostGateway.ConfigureSimulation(new NetworkSimulation(latency, jitter, loss, 0, 0));
            var host = new VehicleNetworkDriver(hostGateway, 99);
            var clients = new List<VehicleNetworkDriver>();
            var errors = new List<float>();
            foreach (int index in Enumerable.Range(1, players - 1))
            {
                var gateway = new GameNetworkingSocketsTransport();
                gateways.Add(gateway);
                var client = new VehicleNetworkDriver(gateway, 0, gateway.Connect(endpoint));
                client.LocalCorrected += _ => errors.Add(client.Prediction!.PredictionError);
                clients.Add(client);
            }

            var clock = Stopwatch.StartNew();
            int ticks = 0;
            byte[]? stale = null;
            bool injected = false;
            bool immediate = false;
            while (ticks < 720)
            {
                if (clock.Elapsed.TotalSeconds < ticks / 60.0)
                {
                    Thread.Sleep(1);
                    continue;
                }

                InputFrame input = ticks < 300
                    ? new InputFrame(0, (short)(Math.Sin(ticks / 60.0) * 18000), 65535, 0, ticks is > 100 and < 240 ? InputButtons.Drift : 0, 0, 0)
                    : new InputFrame(0, 0, 0, 65535, 0, 0, 0);
                // Stop completely rather than continuing into reverse during the convergence window.
                if (ticks >= 450)
                {
                    input = default;
                }

                host.Advance(default, Observe);
                foreach (VehicleNetworkDriver client in clients)
                {
                    uint? before = client.Prediction?.History.LastAcknowledged;
                    ulong? tickBefore = client.LocalState?.Movement.Tick;
                    client.Advance(input, Observe);
                    if (ticks < 100 && before.HasValue && before == client.Prediction!.History.LastAcknowledged && client.LocalState!.Movement.Tick > tickBefore)
                    {
                        immediate = true;
                    }

                    Assert.That(client.Failure, Is.Empty);
                }

                if (ticks == 150)
                {
                    stale = VehicleNetworkCodec.EncodeSnapshot(host.Latest!);
                }

                if (ticks == 300 && stale is not null)
                {
                    foreach (ulong peer in hostGateway.Connections.Keys)
                    {
                        hostGateway.Send(new TransportMessage(peer, stale, TransportDelivery.Unreliable));
                    }

                    injected = true;
                }

                ticks++;
            }

            Assert.That(immediate, Is.True, "Local prediction must advance without a fresh acknowledgement.");
            Assert.That(injected, Is.True);
            Assert.That(host.Host!.World.State.Vehicles.Count, Is.EqualTo(players));
            foreach (VehicleNetworkDriver client in clients)
            {
                Assert.That(client.ReceivedSnapshots, Is.GreaterThan(100));
                Assert.That(client.RejectedPackets, Is.GreaterThan(0), "Injected stale snapshot must be rejected.");
                Assert.That(client.SnapshotAge, Is.LessThan(0.5));
                Assert.That(client.Latest!.Vehicles.Count, Is.EqualTo(players));
                Assert.That(client.Prediction!.History.Pending.Count, Is.LessThan(60));
                float divergence = Vector3.Distance(client.LocalState!.Movement.Physics.Position, host.Host.World.GetVehicle(client.LocalVehicleId).Movement.Physics.Position);
                Assert.That(divergence, Is.LessThan(1), "Settled client should converge to host authority.");
            }

            errors.Sort();
            float p99 = errors[(int)((errors.Count - 1) * 0.99)];
            Assert.That(p99, Is.LessThan(1), "Routine corrections should stay below visible hard-snap scale.");
            TestContext.WriteLine($"players={players}; outbound delay={latency}ms; jitter={jitter}ms; loss={loss}%; corrections={errors.Count}; p99={p99:F4}m; max={errors.Max():F4}m; snapshots/client={clients.Min(client => client.ReceivedSnapshots)}; stale packets rejected");
        }
        finally
        {
            if (gateways.Count > 0)
            {
                gateways[0].ConfigureSimulation(new());
            }

            foreach (GameNetworkingSocketsTransport gateway in gateways.AsEnumerable().Reverse())
            {
                gateway.Dispose();
            }
        }
    }

    private static VehicleObservation Observe(VehicleSnapshot state)
    {
        VehiclePhysicsState p = state.Movement.Physics;
        Vector3 velocity = p.LinearVelocity;
        velocity.Y = 0;
        Vector3 position = p.Position + (velocity / 60);
        position.Y = 1;
        float angularSpeed = p.AngularVelocity.Length();
        Quaternion rotation = angularSpeed > 0.00001f ? Quaternion.Normalize(Quaternion.CreateFromAxisAngle(p.AngularVelocity / angularSpeed, angularSpeed / 60) * p.Orientation) : p.Orientation;
        return new VehicleObservation(new VehiclePhysicsState(position, rotation, velocity, p.AngularVelocity), Vector3.UnitY);
    }
}
