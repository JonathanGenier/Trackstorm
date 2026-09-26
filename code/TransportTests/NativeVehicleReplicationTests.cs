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
    /// <param name="reorder">Outbound reordering percentage.</param>
    [TestCase(2, 0, 0, 0f, 0f)]
    [TestCase(2, 30, 0, 0f, 0f)]
    [TestCase(2, 25, 5, 0f, 0f)]
    [TestCase(2, 50, 10, 0f, 0f)]
    [TestCase(2, 0, 0, 2f, 0f)]
    [TestCase(8, 30, 10, 2f, 0f)]
    [TestCase(2, 30, 10, 2f, 10f)]
    [TestCase(8, 30, 10, 2f, 10f)]
    public void VehicleLoopConvergesAcrossNetworkConditions(int players, int latency, int jitter, float loss, float reorder)
        => RunVehicleLoop(players, latency, jitter, loss, reorder, 720);

    /// <summary>Known application loss is measured separately, without doubling the native-loss profile.</summary>
    [Test]
    public void EightPlayerControlledSnapshotLossConverges()
        => RunVehicleLoop(8, 30, 10, 0, 10, 720, controlledLoss: true);

    /// <summary>Opt-in ten-minute real-time eight-player UDP soak; resource samples include the test process.</summary>
    [Test, Explicit("Sustained native soak; run separately from the ordinary fast gate.")]
    public void TenMinuteEightPlayerImpairmentSoak() => RunVehicleLoop(8, 30, 10, 2, 10, 36000);

    private static void RunVehicleLoop(int players, int latency, int jitter, float loss, float reorder, int durationTicks, bool controlledLoss = false)
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
            hostGateway.Listen(TransportEndpoint.DirectIp(endpoint));
            hostGateway.ConfigureSimulation(new NetworkSimulation(latency, jitter, loss, reorder, 25));
            var host = new VehicleNetworkDriver(hostGateway, 99);
            var clients = new List<VehicleNetworkDriver>();
            var observed = new List<ObservedImpairmentGateway>();
            var errors = new List<float>();
            var startup = new List<float>();
            var steady = new List<float>();
            int ticks = 0;
            int limitedFrames = 0;
            int limitedDuringProbe = 0;
            int peakPending = 0;
            double peakAge = 0;
            int diagnosticCount = 0;
            double scheduleLag = 0;
            foreach (int index in Enumerable.Range(1, players - 1))
            {
                var gateway = new GameNetworkingSocketsTransport();
                gateways.Add(gateway);
                var seam = new ObservedImpairmentGateway(gateway) { DropSnapshots = controlledLoss };
                observed.Add(seam);
                var client = new VehicleNetworkDriver(seam, 0, gateway.Connect(TransportEndpoint.DirectIp(endpoint)));
                int delivered = 0;
                int rejected = 0;
                int accepted = 0;
                ulong? latestTick = null;
                seam.BeforeReceive = () =>
                {
                    if (seam.DeliveredStale > delivered)
                    {
                        Assert.That(client.RejectedPackets, Is.EqualTo(rejected + 1), "Each actually delivered probe must be rejected by the driver.");
                        Assert.That(client.ReceivedSnapshots, Is.EqualTo(accepted));
                        Assert.That(client.Latest?.Tick, Is.EqualTo(latestTick), "A delivered stale probe cannot replace authority.");
                    }
                    delivered = seam.DeliveredStale;
                    rejected = client.RejectedPackets;
                    accepted = client.ReceivedSnapshots;
                    latestTick = client.Latest?.Tick;
                };
                client.LocalCorrected += _ =>
                {
                    float error = client.Prediction!.PredictionError;
                    errors.Add(error);
                    (ticks < 120 ? startup : steady).Add(error);
                    if (error > 0.1f && diagnosticCount++ < 100)
                        TestContext.Progress.WriteLine($"CORRECTION tick={ticks}; vehicle={client.LocalVehicleId}; error={error:F4}; scheduleLag={scheduleLag:F4}; age={client.SnapshotAge:F4}; pending={client.Inputs!.Pending.Count}; clientAck={client.Inputs.LastAcknowledged}; authorityAck={host.Host!.Snapshot().Vehicles.Single(vehicle => vehicle.State.VehicleId == client.LocalVehicleId).AcknowledgedInput}; hostTick={host.Latest?.Tick}; snapshotTick={client.Latest?.Tick}");
                };
                clients.Add(client);
            }

            var clock = Stopwatch.StartNew();
            byte[]? stale = null;
            bool injected = false;
            bool immediate = false;
            while (ticks < durationTicks)
            {
                if (clock.Elapsed.TotalSeconds < ticks / 60.0)
                {
                    Thread.Sleep(1);
                    continue;
                }

                int phaseTick = ticks % 720;
                scheduleLag = Math.Max(0, clock.Elapsed.TotalSeconds - ticks / 60.0);
                InputFrame input = phaseTick < 300
                    ? new InputFrame(0, (short)(Math.Sin(phaseTick / 60.0) * 18000), 65535, 0, phaseTick is > 100 and < 240 ? InputButtons.Drift : 0, 0, 0)
                    : new InputFrame(0, 0, 0, 65535, 0, 0, 0);
                // Stop completely rather than continuing into reverse during the convergence window.
                if (phaseTick >= 450)
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
                    if (client.Prediction?.IsPredictionLimited == true)
                    {
                        limitedFrames++;
                        if (phaseTick is >= 300 and < 390) { limitedDuringProbe++; }
                        if (diagnosticCount++ < 100)
                            TestContext.Progress.WriteLine($"HOLD tick={ticks}; vehicle={client.LocalVehicleId}; scheduleLag={scheduleLag:F4}; age={client.SnapshotAge:F4}; pending={client.Inputs!.Pending.Count}; clientAck={client.Inputs.LastAcknowledged}; authorityAck={host.Host!.Snapshot().Vehicles.Single(vehicle => vehicle.State.VehicleId == client.LocalVehicleId).AcknowledgedInput}; hostTick={host.Latest?.Tick}; snapshotTick={client.Latest?.Tick}");
                    }
                    peakPending = Math.Max(peakPending, client.Inputs?.Pending.Count ?? 0);
                    peakAge = Math.Max(peakAge, client.SnapshotAge ?? 0);
                    Assert.That(client.History?.Snapshots.Count ?? 0, Is.LessThanOrEqualTo(SnapshotHistory.Capacity));
                }

                if (phaseTick == 150)
                {
                    stale = VehicleNetworkCodec.EncodeSnapshot(host.Latest!);
                }

                if (phaseTick == 300)
                {
                    // A delayed publication can legitimately contain the captured boundary while fresh.
                    // Arm the observer only once every client has moved beyond that boundary.
                    ulong staleTick = VehicleNetworkCodec.DecodeSnapshot(stale!).Tick;
                    Assert.That(clients.All(client => client.Latest?.Tick > staleTick), Is.True);
                    foreach (var seam in observed) { seam.StaleProbe = stale; }
                }

                if (phaseTick is >= 300 and < 360 && stale is not null)
                {
                    foreach (ulong peer in hostGateway.Connections.Keys)
                    {
                        hostGateway.Send(new TransportMessage(peer, stale, TransportDelivery.Unreliable));
                    }

                    injected = true;
                }

                ticks++;
                if (ticks % 3600 == 0)
                {
                    using var process = Process.GetCurrentProcess();
                    TestContext.Progress.WriteLine($"SOAK seconds={clock.Elapsed.TotalSeconds:F2}; tick={ticks}; managed={GC.GetTotalMemory(false)}; private={process.PrivateMemorySize64}; working={process.WorkingSet64}; handles={process.HandleCount}; threads={process.Threads.Count}; peakPending={peakPending}; peakSnapshotAge={peakAge:F4}; received={clients.Sum(client => client.ReceivedSnapshots)}; staleDelivered={observed.Sum(seam => seam.DeliveredStale)}; controlledDrops={observed.Sum(seam => seam.DroppedSnapshots)}; gc2={GC.CollectionCount(2)}");
                    peakPending = 0;
                    peakAge = 0;
                }
            }

            // Keep every final diagnostic and convergence check available even when the hold gate fails.
            Assert.Multiple(() =>
            {
                TestContext.WriteLine($"prediction-limited client ticks={limitedFrames}; during probe window plus 0.5s drain={limitedDuringProbe}; elsewhere={limitedFrames - limitedDuringProbe}");
                Assert.That(immediate, Is.True, "Local prediction must advance without a fresh acknowledgement.");
                Assert.That(limitedFrames, Is.Zero, "Supported WAN conditions must not exhaust the speculative horizon.");
                Assert.That(injected, Is.True);
                foreach (var seam in observed)
                {
                    Assert.That(seam.DeliveredStale, Is.GreaterThan(0), "Bounded repeated probes must establish actual arrival; an absent probe is not a rejection failure.");
                    if (controlledLoss) { Assert.That(seam.DroppedSnapshots, Is.GreaterThan(0)); }
                    TestContext.WriteLine($"delivered stale probes={seam.DeliveredStale}; controlled dropped snapshots={seam.DroppedSnapshots}; native loss is configured, not individually observable");
                }
                Assert.That(host.Host!.World.State.Vehicles.Count, Is.EqualTo(players));
                foreach (VehicleNetworkDriver client in clients)
                {
                    Assert.That(client.ReceivedSnapshots, Is.GreaterThan(100));
                    Assert.That(client.RejectedPackets, Is.GreaterThan(0), "Injected stale snapshot must be rejected.");
                    Assert.That(client.SnapshotAge, Is.Not.Null.And.LessThan(0.5));
                    Assert.That(client.Latest!.Vehicles.Count, Is.EqualTo(players));
                    Assert.That(client.Prediction!.History.Pending.Count, Is.LessThan(60));
                    float divergence = Vector3.Distance(client.LocalState!.Movement.Physics.Position, host.Host.World.GetVehicle(client.LocalVehicleId).Movement.Physics.Position);
                    Assert.That(divergence, Is.LessThan(1), "Settled client should converge to host authority.");
                }

                errors.Sort();
                float p99 = errors[(int)((errors.Count - 1) * 0.99)];
                steady.Sort();
                float steadyP99 = steady[(int)((steady.Count - 1) * 0.99)];
                Assert.That(steadyP99, Is.LessThan(0.1), "Repeated decimetre corrections are a quality regression.");
                Assert.That(steady.Max(), Is.LessThan(0.5), "A percentile must not hide an isolated ordinary-driving correction.");
                Assert.That(startup.Max(), Is.LessThan(0.75), "Startup is independently gated, not hidden in a long settled window.");
                TestContext.WriteLine($"startup max={startup.Max():F4}m; steady p99={steadyP99:F4}m; steady max={steady.Max():F4}m; steady >1cm={steady.Count(error => error > 0.01f)}; max pending at completion={clients.Max(client => client.Inputs!.Pending.Count)}");
                TestContext.WriteLine($"players={players}; outbound delay={latency}ms; jitter={jitter}ms; loss={loss}%; corrections={errors.Count}; p99={p99:F4}m; max={errors.Max():F4}m; snapshots/client={clients.Min(client => client.ReceivedSnapshots)}; stale packets rejected");
            });
            foreach (var client in clients) { client.Dispose(); }
            host.Dispose();
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
