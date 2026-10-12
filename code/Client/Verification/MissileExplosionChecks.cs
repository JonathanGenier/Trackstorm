using System.Net;
using System.Net.Sockets;
using Godot;
using Trackstorm.Client.Items;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Verification;

/// <summary>Native production impacts across UDP peers plus bounded cosmetic overlap and rendered evidence.</summary>
public sealed partial class MissileExplosionChecks : Node
{
    private readonly List<NetworkVehicleArena> _arenas = [];
    private readonly List<GameNetworkingSocketsTransport> _gateways = [];
    private readonly List<SubViewport> _views = [];
    private readonly List<Camera3D> _cameras = [];
    private readonly List<ItemEvent>[] _impacts = [[], []];
    private readonly List<string> _evidence = [];
    private readonly string _output = ProjectSettings.GlobalizePath("res://.godot/ts241-checks");
    private List<double>? _times;
    private Vector3 _focus;

    public override void _Process(double delta) => _times?.Add(delta * 1000);
    public override void _Ready() => CallDeferred(MethodName.Run);

    private async void Run()
    {
        try
        {
            Engine.MaxFps = 60;
            System.IO.Directory.CreateDirectory(_output);
            using var reservation = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            string endpoint = $"127.0.0.1:{((IPEndPoint)reservation.Client.LocalEndPoint!).Port}";
            reservation.Close();
            for (int i = 0; i < 2; i++)
            {
                var gateway = new GameNetworkingSocketsTransport();
                ulong server = 0;
                if (i == 0) { gateway.Listen(TransportEndpoint.DirectIp(endpoint)); }
                else { server = gateway.Connect(TransportEndpoint.DirectIp(endpoint)); }
                _gateways.Add(gateway);
                var view = new SubViewport { Size = new(1280, 800), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
                if (i == 0) { var display = new SubViewportContainer(); AddChild(display); display.AddChild(view); }
                else { AddChild(view); }
                _views.Add(view);
                var arena = new NetworkVehicleArena();
                arena.Initialize(gateway, i == 0 ? 241ul : 0, server); view.AddChild(arena); _arenas.Add(arena);
                var camera = new Camera3D { Current = true, Fov = 65, Far = 1500 };
                arena.AddChild(camera); _cameras.Add(camera);
                int index = i;
                arena.Driver.ItemsReceived += p => _impacts[index].AddRange(p.Events.Where(e => e.Item == HeldItem.Missile && e.Impact));
            }
            await Until(() => _arenas.All(a => a.Driver.Latest?.Vehicles.Count == 2), "Two native UDP peers connected", 1200);
            await Frames(180);
            var host = _arenas[0].Driver.Host!;
            Require(host.Items.Configuration.ExplosionRadius == VehicleDimensions.Length, "Hosted blast radius equals measured 5.06 m car hull");
            var body = _arenas[0].Bodies[1];
            _focus = body.GlobalPosition + body.GlobalBasis * new Vector3(0, 0, -14);
            foreach (var arena in _arenas)
            {
                var wall = new StaticBody3D { Position = _focus, CollisionLayer = 1, Quaternion = body.Quaternion };
                wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(10, 10, .5f) } });
                wall.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new(10, 10, .5f) },
                    MaterialOverride = new StandardMaterial3D { AlbedoColor = new(.2f, .25f, .3f) } });
                arena.AddChild(wall);
            }
            AimCameras();
            for (int wave = 0; wave < 4; wave++)
            {
                host.Items.RemovePlayer(1);
                await Frames(100);
                Require(_arenas[0].Driver.GiveDeveloperItem(HeldItem.Missile), "Repeated production Missile pickup");
                await Until(() => _arenas[0].Driver.LocalItem is { } slot && body.Rack.IsMissileReady(slot) && host.World.State.Tick >= slot.MissileReadyTick, "Missile ready");
                int before = _impacts[0].Count;
                Require(_arenas[0].Driver.RequestItemUse(), "Production Missile fired");
                await Until(() => _impacts.All(events => events.Count > before), "Native collision impact received on both peers");
                Require(_impacts[0][before] == _impacts[1][before], "Reliable impact token and collision position identical on peers");
                var effects = _arenas.Select(a => Descendants(a).OfType<MissileExplosion>().Single()).ToArray();
                Require(effects.All(e => e.Position.DistanceTo(Vehicles.VehicleBody.ToGodot(_impacts[0][before].Position)) < .001f), "VFX centered on real authoritative collision");
                Require(effects.All(e => e.FragmentCount == 224), "Default firework allocation is bounded on both peers");
                if (wave == 0)
                {
                    await Capture("01-native-wall-impact");
                    await Frames(8); await Capture("02-native-fireworks");
                    await Capture("02-remote-fireworks", 1);
                    _arenas[0].GetNode<Camera3D>("ChaseCamera").Current = true;
                    await Capture("02-player-camera");
                    _cameras[0].Current = true;
                    await Frames(12); await Capture("02b-fire-breakup");
                    await Frames(22); await Capture("02c-outer-fireworks");
                    await Frames(22); await Capture("02d-embers");
                }
                if (wave == 1)
                {
                    // Closely spaced actual gameplay frames expose trajectory/shape changes, not just an attractive still.
                    for (int frame = 0; frame < 24; frame++)
                    {
                        await Capture($"05-burst-motion-{frame:D2}");
                        await Frames(2);
                    }
                }
                await Until(() => _arenas.All(a => !Descendants(a).OfType<MissileExplosion>().Any()), "Repeated impact and smoke owners fully expire");
            }
            _gateways[0].ConfigureSimulation(new NetworkSimulation(45, 8, 2, 0, 0));
            host.Items.RemovePlayer(1);
            await Frames(120);
            Require(_arenas[0].Driver.GiveDeveloperItem(HeldItem.Missile), "Latency shot pickup");
            await Until(() => _arenas[0].Driver.LocalItem is { } held && body.Rack.IsMissileReady(held) && host.World.State.Tick >= held.MissileReadyTick, "Latency shot ready");
            Require(_arenas[0].Driver.RequestItemUse(), "Latency shot fired");
            await Until(() => _impacts.All(events => events.Count == 5), "Reliable impact under latency/jitter/loss");
            Require(_impacts[0].SequenceEqual(_impacts[1]), "No extra or missing peer impacts");
            await Frames(160);
            host.Items.RemovePlayer(1); host.Items.RemovePlayer(2);
            await Frames(120);
            Require(host.Items.Grant(host.World, 1, HeldItem.Missile) && host.Items.Grant(host.World, 2, HeldItem.Missile), "Two-peer volley acquired");
            await Until(() => _arenas.Select((a, i) => a.Driver.LocalItem is { } inventory && a.Bodies[(ulong)i + 1].Rack.IsMissileReady(inventory) && host.World.State.Tick >= inventory.MissileReadyTick).All(ready => ready),
                "Both peer launchers ready");
            Require(_arenas.All(a => a.Driver.RequestItemUse()), "Both native peers fire in the same frame under latency");
            await Until(() => _impacts.All(events => events.Count == 7), "Both volley impacts confirmed on both peers");
            Require(_impacts[0].SequenceEqual(_impacts[1]), "Concurrent peer-fired collision events stay consistent");
            await Until(() => _arenas.All(a => !Descendants(a).OfType<MissileExplosion>().Any()), "Peer volley effects and smoke expire on both native worlds");
            await CosmeticChecks();
            System.IO.File.WriteAllLines(System.IO.Path.Combine(_output, "evidence.txt"), _evidence);
            GD.Print("Missile explosion integration passed: native repeated impacts, UDP consistency, cosmetic bounds/tuning/cleanup.");
            foreach (var arena in _arenas) { arena.QueueFree(); }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            foreach (var gateway in _gateways) { gateway.Dispose(); }
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private async Task CosmeticChecks()
    {
        var panel = new Development.DeveloperOptionsPanel(); AddChild(panel); AddChild(panel.Footer);
        void Edit(string title, double value)
        {
            var label = Descendants(panel).OfType<Label>().Single(l => l.Text == title);
            ((SpinBox)label.GetParent().GetChild(label.GetIndex() + 1)).Value = value;
        }
        Edit("Smoke residue lifetime (s)", 5); Edit("Dense smoke wake length (m)", 8.5);
        Require(MissileVfxSettings.Current.SmokeLifetime == 5 && MissileVfxSettings.Current.SmokeTrailLength == 8.5f,
            "Native flight smoke lifetime and spatial wake editors are operative");
        var flightSection = Descendants(panel).OfType<Development.ConfigsAccordion>().Single(s => Descendants(s).OfType<Label>().Any(l => l.Text == "Smoke residue lifetime (s)"));
        flightSection.Body.GetChildren().OfType<Button>().Single().EmitSignal(Button.SignalName.Pressed);
        Require(MissileVfxSettings.Current == new MissileVfxSettings(), "Flight smoke category reset restores defaults");
        Edit("Explosion intensity", 1.5); Edit("Central fire scale (within blast)", 1.3);
        Edit("Cosmetic reach (car lengths)", 2.5); Edit("Firework density (0 disables)", 2);
        Edit("Firework duration (s)", 2.4);
        Edit("Impact smoke duration (s)", 4.5); Edit("Impact smoke opacity", .4);
        Require(MissileExplosionSettings.Current == new MissileExplosionSettings { Intensity = 1.5f, FireScale = 1.3f,
            ReachCarLengths = 2.5f, Density = 2, Duration = 2.4f, SmokeDuration = 4.5f, SmokeOpacity = .4f }, "All seven native explosion editors change independent visual settings");
        var section = Descendants(panel).OfType<Development.ConfigsAccordion>().Single(s => Descendants(s).OfType<Label>().Any(l => l.Text == "Explosion intensity"));
        section.Body.GetChildren().OfType<Button>().Single().EmitSignal(Button.SignalName.Pressed);
        Require(MissileExplosionSettings.Current == new MissileExplosionSettings(), "Explosion category reset restores all visual defaults");
        panel.Footer.QueueFree(); panel.QueueFree();
        CheckSparkMotion();
        await CheckSmokeResidue();
        // These are explicit presentation fixtures, not claimed native gameplay collisions.
        var presentation = new ItemPresentation(); _arenas[0].AddChild(presentation);
        var host = _arenas[0].Driver.Host!;
        presentation.Apply(new ItemPublication(99, host.Snapshot(), [], [], [new ItemEvent(999, 1, HeldItem.Missile, System.Numerics.Vector3.Zero, false)]));
        presentation.Apply(new ItemPublication(100, host.Snapshot(), [], [], []));
        Require(!Descendants(presentation).OfType<MissileExplosion>().Any(), "Launch and silent projectile removal never create impact VFX");
        ItemPublication Fixture(int count, ulong offset) => new(100 + offset, host.Snapshot(), [], [],
            Enumerable.Range(0, count).Select(i => new ItemEvent(offset + (ulong)i + 1, 1, HeldItem.Missile,
                Vehicles.VehicleBody.ToCore(_focus + new Vector3((i % 4 - 1.5f) * 2, 0, (i / 4 - 1.5f) * 2)), true)).ToArray());
        presentation.Apply(Fixture(1, 700));
        await Frames(65); await Capture("06-impact-smoke-aftermath");
        Require(Descendants(presentation).OfType<MissileExplosion>().Any(), "Impact smoke remains after the initial fiery flash");
        await Until(() => !Descendants(presentation).OfType<MissileExplosion>().Any(), "Single smoke aftermath dissipates");
        var baseline = new List<double>(); _times = baseline; await Frames(120); _times = null;
        presentation.Apply(Fixture(16, 0));
        Require(Descendants(presentation).OfType<MissileExplosion>().Count() == 16, "Sixteen simultaneous production effects");
        var times = new List<double>(); _times = times;
        await Frames(8); await Capture("03-sixteen-overlapping");
        await Until(() => !Descendants(presentation).OfType<MissileExplosion>().Any(), "Overlap smoke fully dissipates"); _times = null;
        Require(!Descendants(presentation).OfType<MissileExplosion>().Any(), "Sixteen overlapping effects clean up");
        Timing("Two-arena baseline", baseline); Timing("Sixteen-overlap render delta", times);
        for (int repeat = 0; repeat < 8; repeat++)
        {
            presentation.Apply(Fixture(16, (ulong)(100 + repeat * 16)));
            await Frames(4);
            Require(Descendants(presentation).OfType<MissileExplosion>().Count(e => !e.IsQueuedForDeletion()) <= ItemPresentation.MaximumMissileExplosions,
                "Sustained burst pool respects 32-effect cap");
        }
        await Until(() => !Descendants(presentation).OfType<MissileExplosion>().Any(), "128 rapid fixture impacts leave no effect owners");
        MissileExplosionSettings.Current = new() { Density = 2, ReachCarLengths = 2.5f, Duration = 2.4f, Intensity = 1.5f, FireScale = 1.3f };
        presentation.Apply(Fixture(1, 500));
        var effect = Descendants(presentation).OfType<MissileExplosion>().Single();
        Require(effect.FragmentCount == 448 && Math.Abs(effect.CosmeticReach - VehicleDimensions.Length * 2.5f) < .001f,
            "Independent maximum density and 2.5-car cosmetic reach consumed");
        await Frames(12); await Capture("04-tuned-effect");
        await Frames(110);
        Require(GodotObject.IsInstanceValid(effect), "Longer cosmetic duration is operative");
        await Until(() => !GodotObject.IsInstanceValid(effect), "Tuned effect and smoke duration expire");
        MissileExplosionSettings.Current = new() { Density = 0 };
        presentation.Apply(Fixture(1, 600));
        Require(Descendants(presentation).OfType<MissileExplosion>().Single().FragmentCount == 0, "Density zero disables decorative fragments");
        Require(host.Items.Configuration.ExplosionRadius == VehicleDimensions.Length, "All visual edits leave actual damage radius unchanged");
        MissileExplosionSettings.Current = new();
        presentation.QueueFree(); await Frames(2);
        Require(!GodotObject.IsInstanceValid(presentation), "Presentation teardown frees remaining effects");
    }

    private async Task CheckSmokeResidue()
    {
        var presentation = new ItemPresentation(); _arenas[0].AddChild(presentation);
        var world = _arenas[0].Driver.Host!.Snapshot();
        var origin = _focus + new Vector3(-12, 7, 10);
        var velocity = new Vector3(120, 0, 0);
        var missile = new MissileState(800, 1, Vehicles.VehicleBody.ToCore(origin), Vehicles.VehicleBody.ToCore(velocity), 300);
        presentation.Apply(new ItemPublication(800, world, [], [missile], []));
        for (int frame = 0; frame < 20; frame++)
        {
            presentation.ApplyProjectileMotion([missile with { Position = Vehicles.VehicleBody.ToCore(origin + new Vector3(frame * 2f, 0, 0)) }]);
            await Frames(1);
            if (frame == 10)
            {
                var sampledFlight = Descendants(presentation).OfType<MissileFlightVisual>().Single();
                var point = sampledFlight.GlobalPosition;
                // Hold the scripted representation for one capture; otherwise screenshot overhead
                // extrapolates a 120 m/s missile outside this gameplay-distance framing.
                sampledFlight.SetProcess(false);
                _cameras[0].GlobalPosition = point + new Vector3(-4, 5, 12);
                _cameras[0].LookAt(point + Vector3.Left * 4);
                await Capture("07-flight-smoke-wake");
                sampledFlight.SetProcess(true);
            }
        }
        var flight = Descendants(presentation).OfType<MissileFlightVisual>().Single();
        var wake = Descendants(flight).OfType<GpuParticles3D>().Single(p => p.Name == "CrimsonSmoke");
        Require(Math.Abs(wake.Lifetime * ((ParticleProcessMaterial)wake.ProcessMaterial).InitialVelocityMax - 7.6) < .01,
            "Continuous dense plume calibrated to 7.6 m / 25 ft independently of render-frame travel");
        presentation.Apply(new ItemPublication(801, world, [], [], []));
        await Frames(2);
        Require(!GodotObject.IsInstanceValid(flight), "Projectile owner retires while stopped smoke survives");
        var residue = Descendants(presentation).OfType<MissileSmokeResidue>().Single();
        Require(residue.GetChildren().OfType<GpuParticles3D>().All(p => !p.Emitting) &&
            !residue.GetChildren().OfType<GpuParticles3D>().Single(p => p.Name == "SmokeResidue").LocalCoords,
            "Retired smoke stops emission; lingering wisps remain world-space");
        Require(Math.Abs(residue.Lifetime - 4.6f) < .001, "Residue has 4.5-second particle lifetime plus cleanup margin");
        await Frames(50); await Capture("08-flight-smoke-residue");
        Require(GodotObject.IsInstanceValid(residue), "Smoke persists after projectile removal");
        await Until(() => !GodotObject.IsInstanceValid(residue), "Stopped trail smoke fully expires");
        AimCameras();
        for (int i = 0; i < 40; i++)
        {
            presentation.Apply(new ItemPublication((ulong)(900 + i * 2), world, [], [missile with { Id = (ulong)(900 + i) }], []));
            presentation.Apply(new ItemPublication((ulong)(901 + i * 2), world, [], [], []));
        }
        Require(Descendants(presentation).OfType<MissileSmokeResidue>().Count(s => !s.IsQueuedForDeletion()) == ItemPresentation.MaximumMissileSmokeResidues,
            "Forty rapid retirements respect 32 retained smoke owners");
        presentation.QueueFree(); await Frames(2);
        Require(!GodotObject.IsInstanceValid(presentation), "Arena presentation teardown frees retained smoke");
    }

    private void CheckSparkMotion()
    {
        var first = new MissileExplosion { Seed = 241, Visible = false };
        var second = new MissileExplosion { Seed = 241, Visible = false };
        _arenas[0].AddChild(first); _arenas[1].AddChild(second);
        first.SetProcess(false); second.SetProcess(false);
        var batch = first.GetChildren().OfType<MultiMeshInstance3D>().Single().Multimesh;
        var other = second.GetChildren().OfType<MultiMeshInstance3D>().Single().Multimesh;
        var previous = new Vector3[first.FragmentCount];
        float farthest = 0;
        bool mixedLifetimes = false;
        for (int step = 0; step < 150; step++)
        {
            first._Process(.01); second._Process(.01);
            int visible = 0;
            for (int i = 0; i < first.FragmentCount; i++)
            {
                var transform = batch.GetInstanceTransform(i);
                var color = batch.GetInstanceColor(i);
                Require(transform == other.GetInstanceTransform(i) && color == other.GetInstanceColor(i), "Token-seeded spark motion matches peers", false);
                Require(transform.Origin.Length() + transform.Basis.Y.Length() * .5f < first.CosmeticReach + .05f,
                    "Entire spark stays within cosmetic reach", false);
                var movement = transform.Origin - previous[i];
                if (step > 5 && color.A > .01f && movement.LengthSquared() > .000001f)
                {
                    Require(transform.Basis.Y.Normalized().Dot(movement.Normalized()) > .98f,
                        "Spark long axis follows its trajectory instead of world vertical", false);
                }
                previous[i] = transform.Origin;
                if (color.A > .01f) { visible++; farthest = Math.Max(farthest, transform.Origin.Length()); }
            }
            if (step > 90 && visible > 0 && visible < first.FragmentCount) { mixedLifetimes = true; }
        }
        Require(mixedLifetimes, "Fragments burn out independently rather than freezing/fading together");
        Require(farthest > VehicleDimensions.Length * 2 && farthest < VehicleDimensions.Length * 2.5f,
            $"Visible outer spark reach {farthest:F2} m lies between two and two-and-a-half production car lengths");
        Require(true, "150 native motion samples: deterministic peers, bounded reach and velocity-aligned spark axes");
        first.QueueFree(); second.QueueFree();
    }

    private void AimCameras()
    {
        for (int i = 0; i < _cameras.Count; i++)
        {
            _cameras[i].GlobalPosition = _focus + new Vector3(-14, 7, 20);
            _cameras[i].LookAt(_focus);
        }
    }
    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            foreach (var arena in _arenas) { arena.Advance(default); Require(arena.Driver.Failure.Length == 0, arena.Driver.Failure, false); }
        }
    }
    private async Task Until(Func<bool> condition, string reason, int maximum = 600)
    {
        for (int i = 0; i < maximum && !condition(); i++) { await Frames(1); }
        Require(condition(), reason);
    }
    private async Task Capture(string name, int view = 0)
    {
        if (DisplayServer.GetName() == "headless") { return; }
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        _views[view].GetTexture().GetImage().SavePng(System.IO.Path.Combine(_output, name + ".png"));
    }
    private void Timing(string name, List<double> samples)
    {
        samples.Sort();
        _evidence.Add($"OBSERVED: {name}: median {samples[samples.Count / 2]:F2} ms, p95 {samples[(int)(samples.Count * .95)]:F2} ms; 60 FPS cap, process delta, not GPU timing.");
    }
    private void Require(bool valid, string reason, bool record = true)
    {
        if (!valid) { throw new InvalidOperationException(reason); }
        if (record) { _evidence.Add("VERIFIED: " + reason); GD.Print(reason); }
    }
    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (var child in node.GetChildren()) { yield return child; foreach (var nested in Descendants(child)) { yield return nested; } }
    }
}
