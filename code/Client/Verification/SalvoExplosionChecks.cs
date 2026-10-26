using System.Net;
using System.Net.Sockets;
using Godot;
using Trackstorm.Client.Items;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Production Salvo collisions and repeated volleys over impaired UDP, plus explicit bounded VFX fixtures.</summary>
public sealed partial class SalvoExplosionChecks : Node
{
    private readonly List<NetworkVehicleArena> _arenas = [];
    private readonly List<GameNetworkingSocketsTransport> _gateways = [];
    private readonly List<SubViewport> _views = [];
    private readonly List<Camera3D> _cameras = [];
    private readonly List<ItemEvent>[] _impacts = [[], [], []];
    private readonly List<string> _evidence = [];
    private readonly Dictionary<ulong, N.Vector3> _positions = new() { [1] = new(-10, 101.2f, 20), [2] = new(0, 101.2f, 0), [3] = new(15, 101.2f, 20) };
    private readonly string _output = ProjectSettings.GlobalizePath("res://.godot/ts-213/polish1/explosions");
    private List<double>? _times;
    private Core.Networking.Replication.HostVehicleSession Host => _arenas[0].Driver.Host!;

    public override void _Process(double delta) => _times?.Add(delta * 1000);
    public override void _Ready() => CallDeferred(MethodName.Run);

    private async void Run()
    {
        try
        {
            Engine.MaxFps = 60;
            System.IO.Directory.CreateDirectory(_output);
            using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            string endpoint = $"127.0.0.1:{((IPEndPoint)socket.Client.LocalEndPoint!).Port}"; socket.Close();
            for (int i = 0; i < 3; i++)
            {
                var gateway = new GameNetworkingSocketsTransport();
                ulong server = 0;
                if (i == 0) { gateway.Listen(TransportEndpoint.DirectIp(endpoint)); }
                else { server = gateway.Connect(TransportEndpoint.DirectIp(endpoint)); }
                _gateways.Add(gateway);
                var view = new SubViewport { Size = new(1280, 720), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
                if (i == 0) { var display = new SubViewportContainer(); AddChild(display); display.AddChild(view); }
                else { AddChild(view); }
                _views.Add(view);
                var arena = new NetworkVehicleArena { PrototypeMapForVerification = true };
                arena.Initialize(gateway, i == 0 ? 213ul : 0, server); view.AddChild(arena); _arenas.Add(arena);
                AddBox(arena, new(0, 99.5f, 0), new(400, 1, 400));
                var camera = new Camera3D { Current = true, Fov = 58 }; arena.AddChild(camera); _cameras.Add(camera);
                int peer = i;
                arena.Driver.ItemsReceived += p => _impacts[peer].AddRange(p.Events.Where(e => e.Item == HeldItem.Salvo && e.Impact));
            }
            await Until(() => _arenas.All(a => a.Driver.Latest?.Vehicles.Count == 3), "Three native UDP worlds joined", 1200);
            Check(Host.TryConfigure(0, new Dictionary<string, double> { ["match.countdown_ticks"] = 1, ["match.minimum_players"] = 1 }, out _), "Active fixture configuration");
            await Frames(120);
            Check(Host.Items.Configuration.SalvoBlastRadius == .9144f, "Production default blast radius is three feet");
            await TerrainContacts();
            await MarkerDimensions();
            Focus(VehicleBody.ToGodot(_positions[2]), new(7, 5, 9));
            await SurfaceDamage();
            foreach (var gateway in _gateways) { gateway.ConfigureSimulation(new NetworkSimulation(45, 8, 2, 0, 0)); }
            await Volleys();
            await TerrainVolleys();
            await RockDamage();
            await CosmeticChecks();
            System.IO.File.WriteAllLines(System.IO.Path.Combine(_output, "evidence.txt"), _evidence);
            GD.Print($"Salvo explosion integration passed: {_evidence.Count} checks; native impacts, shield coverage, repeated volleys, impaired UDP, three bursts and smoke budget.");
            foreach (var arena in _arenas) { arena.QueueFree(); }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            foreach (var gateway in _gateways) { gateway.Dispose(); }
            _gateways.Clear(); GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private async Task SurfaceDamage()
    {
        Check(Host.Items.Grant(Host.World, 2, HeldItem.Shield), "Target acquires ordinary rear armor");
        await Frames(200);
        N.Vector3 front = _positions[2] + new N.Vector3(0, .3f, -2);
        float hp = Host.World.GetVehicle(2).Damage.CurrentHP;
        Seed(front);
        await Until(() => _impacts.All(e => e.Count == 1), "One native chassis impact reaches all three peers");
        Check(Host.World.GetVehicle(2).Damage.CurrentHP < hp && Host.Items.Shields.Single().HP == 1000, "Front-exposed vehicle takes damage; rear armor remains intact");
        await Capture("01-front-impact");
        float after = Host.World.GetVehicle(2).Damage.CurrentHP;
        await Frames(50);
        Check(Host.World.GetVehicle(2).Damage.CurrentHP == after, "Three visual bursts apply no later damage");
        N.Vector3 rear = _positions[2] + new N.Vector3(0, .25f, 3.25f);
        Seed(rear, rear);
        await Until(() => _impacts.All(e => e.Count == 3), "Two overlapping native rear-panel impacts replicate once each");
        Check(Host.Items.Shields.Single().HP == 500 && Host.World.GetVehicle(2).Damage.CurrentHP == after, "Overlapping impacts spend shield HP twice without vehicle leakage");
        Check(_impacts.Skip(1).All(e => e.SequenceEqual(_impacts[0])), "All peers agree on impact tokens, owners and positions");
        await Capture("02-rear-overlap"); await Capture("02-remote-overlap", 1);
        Host.Items.RemovePlayer(2); await Frames(30);
    }

    private async Task Volleys()
    {
        _positions[2] = new(0, 101.2f, 20);
        foreach (var arena in _arenas) { AddBox(arena, new(0, 110, -10), new(70, 22, .5f)); }
        Focus(new(0, 103, -9), new(19, 10, 24));
        int expected = _impacts[0].Count;
        for (int wave = 0; wave < 3; wave++)
        {
            foreach (ulong owner in new ulong[] { 1, 3 })
            {
                Host.Items.RemovePlayer(owner);
                Check(Host.Items.Grant(Host.World, owner, HeldItem.Salvo), "Repeated normal Salvo grant");
            }
            await Until(() => new[] { 0, 2 }.All(i => _arenas[i].Driver.LocalItem is { } slot &&
                _arenas[i].Driver.SalvoReady?.Invoke(slot) == true && Host.Items.SalvoReady(Host.Items.Slots.Single(s => s.Vehicle == slot.Vehicle))), "Both catapults ready", 900);
            Check(_arenas[0].Driver.RequestItemUse() && _arenas[2].Driver.RequestItemUse(), "Host and remote simultaneously commit five-shot volleys");
            expected += 10;
            await Until(() => _impacts.All(e => e.Count == expected), "Ten native impacts converge under 45 ms delay, 8 ms jitter and 2% loss", 1000);
            Check(_impacts.All(e => e.Select(x => x.Token).Distinct().Count() == expected) && _impacts.Skip(1).All(e => e.SequenceEqual(_impacts[0])), "No missing or duplicate cross-client explosions");
            await Capture("03-volley-smoke-" + wave);
            await Frames(40);
        }
        foreach (var arena in _arenas)
        {
            var effects = Descendants(arena).OfType<SalvoExplosion>().Where(e => !e.IsQueuedForDeletion()).ToArray();
            Check(effects.Length <= ItemPresentation.MaximumSalvoExplosions && effects.Select(e => e.Seed).Distinct().Count() == effects.Length,
                "Production smoke accumulation has bounded, unique effect owners");
        }
    }

    private async Task CosmeticChecks()
    {
        // Explicit presentation fixtures, separate from native collision evidence above.
        var presentation = new ItemPresentation(); _arenas[0].AddChild(presentation);
        var point = new Vector3(0, 102, 8);
        Focus(point, new(26, 15, 32));
        ulong revision = 20;
        ItemPublication Event(int count, ulong token) => new(revision++, Host.Snapshot(), [], [], Enumerable.Range(0, count)
            .Select(i => new ItemEvent(token + (ulong)i, 1, HeldItem.Salvo, VehicleBody.ToCore(point + new Vector3(i % 8 * 1.6f, 0, i / 8 * 1.6f)), true)));
        var publication = Event(1, 10000);
        presentation.Apply(publication); presentation.Apply(publication);
        Check(Descendants(presentation).OfType<SalvoExplosion>().Count() == 1, "Duplicate reliable publication cannot duplicate the effect");
        var effect = Descendants(presentation).OfType<SalvoExplosion>().Single();
        effect.SetProcess(false);
        var replica = new SalvoExplosion { Seed = effect.Seed, Position = point }; _arenas[1].AddChild(replica); replica.SetProcess(false);
        for (int frame = 0; frame <= 36; frame++)
        {
            if (frame > 0) { effect.Advance(1f / 60); replica.Advance(1f / 60); }
            var a = Descendants(effect).OfType<MultiMeshInstance3D>().First().Multimesh;
            var b = Descendants(replica).OfType<MultiMeshInstance3D>().First().Multimesh;
            Check(a.GetInstanceTransform(25) == b.GetInstanceTransform(25) && a.GetInstanceColor(25) == b.GetInstanceColor(25), "Token-seeded burst geometry agrees at equal age", false);
            if (frame % 2 == 0) { await Capture($"04-burst-{frame:D2}"); }
            await Frames(1);
        }
        Check(effect.BurstsStarted == 3, "Three rapid visual bursts complete within 0.44 seconds");
        presentation.Apply(new(3, Host.Snapshot(), [], [], []));
        presentation.Apply(new(4, Host.Snapshot(), [], [], publication.Events));
        Check(Descendants(presentation).OfType<SalvoExplosion>().Single() == effect && effect.Age > .5f,
            "Rollback of a recently detonated round neither duplicates nor restarts its active effect");
        replica.QueueFree();
        foreach (float age in new[] { 2f, 10, 19.5f })
        {
            effect.Advance(age - effect.Age); await Capture("05-smoke-" + age.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
            Check(GodotObject.IsInstanceValid(effect) && !effect.IsQueuedForDeletion(), $"Smoke remains at age {age} seconds");
        }
        effect.Advance(.6f); await Frames(2);
        Check(!GodotObject.IsInstanceValid(effect), "Smoke expires at twenty seconds");
        presentation.Apply(new(3, Host.Snapshot(), [], [], []));
        Check(!Descendants(presentation).OfType<SalvoExplosion>().Any(), "Older accepted recovery checkpoint replays no historical explosion");
        revision = 4;
        var resumed = Event(1, 10001);
        presentation.Apply(resumed); presentation.Apply(resumed);
        var resumedEffect = Descendants(presentation).OfType<SalvoExplosion>().Single();
        Check(resumedEffect.Seed == 10001, "First post-recovery explosion appears exactly once after revision rebase");
        resumedEffect.QueueFree(); await Frames(2);
        await Until(() => _arenas.All(a => !Descendants(a).OfType<SalvoExplosion>().Any()), "Earlier native smoke fully clears", 1500);
        // Matched one-view presentation sample, separate from actual native volleys
        // above and the deliberate overload below. Three worlds still simulate.
        foreach (var view in _views.Skip(1)) { view.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled; }
        Focus(point, new(26, 15, 32));
        var singleBaseline = new List<double>(); _times = singleBaseline; await Frames(120); _times = null;
        var ordinary = new List<double>(); _times = ordinary;
        for (int shot = 0; shot < 10; shot++)
        { presentation.Apply(Event(2, (ulong)(14000 + shot * 2))); await Frames(75); }
        _times = null;
        await Capture("06-single-view-repeated-smoke");
        Timing("one rendered view baseline, three simulated worlds", singleBaseline);
        Timing("one rendered view, 20 cosmetic impacts at two five-shot volleys per cycle, two cycles", ordinary);
        foreach (var effectOwner in Descendants(presentation).OfType<SalvoExplosion>()) { effectOwner.QueueFree(); }
        await Frames(3);
        foreach (var view in _views.Skip(1)) { view.RenderTargetUpdateMode = SubViewport.UpdateMode.Always; }
        var baseline = new List<double>(); _times = baseline; await Frames(120); _times = null;
        Focus(point + new Vector3(6, 0, 8), new(28, 18, 32));
        var stress = new List<double>(); _times = stress;
        for (int wave = 0; wave < 20; wave++)
        {
            presentation.Apply(Event(16, (ulong)(11000 + wave * 16)));
            await Frames(30);
            Check(Descendants(presentation).OfType<SalvoExplosion>().Count(e => !e.IsQueuedForDeletion()) <= ItemPresentation.MaximumSalvoExplosions, "Sustained smoke budget", false);
        }
        _times = null;
        await Capture("06-smoke-saturation");
        Timing("three-world baseline", baseline); Timing("320 cosmetic impacts / ten seconds, capped 128 effects / 2560 smoke puffs", stress);
        presentation.QueueFree(); await Frames(3);
        Check(!GodotObject.IsInstanceValid(presentation), "Arena-owned effects free together on teardown");
    }

    private void Seed(params N.Vector3[] targets)
    {
        var items = Host.Items; ulong token = items.TokenHighWater;
        var rounds = targets.Select(target =>
        {
            var arc = new SalvoFlight(target + N.Vector3.UnitY * 4, target, 1, 2, 1, Host.World.GetVehicle(1).LifeId);
            return new MissileState(++token, 1, arc.At(1), arc.VelocityAt(1), SalvoFlight.LifetimeTicks - 1) { Arc = arc };
        }).ToArray();
        items.Restore(new(items.Revision + 1, Host.Snapshot(), items.Slots, rounds, [], Host.Spawns?.States, items.Patches,
            items.OilContacts, Host.Spawns?.Balances, items.Mines, items.Shields, items.DiscardRevision, items.EmpAttachments), items.Revision + 1, token);
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var state = Host.World.State;
            // Fixed fixture poses isolate the collision/damage boundary; native sweeps remain production.
            Host.World.Restore(new(state.Tick, state.LastInput, state.Vehicles.Select(v =>
            {
                var pose = new VehiclePhysicsState(_positions[v.VehicleId], N.Quaternion.Identity, N.Vector3.Zero, N.Vector3.Zero);
                if (_arenas[0].Bodies.TryGetValue(v.VehicleId, out var body)) { body.Apply(pose); }
                return new VehicleSnapshot(v.VehicleId, v.LifeId, new VehicleState(state.Tick, pose, true, false, 0, 0), v.Damage, pose,
                    lifecycle: v.Lifecycle, respawnAtTick: v.RespawnAtTick);
            }), state.Match));
            foreach (var arena in _arenas)
            {
                if (_terrainDirection is { } direction) { arena.Driver.DesiredAim = direction; }
                arena.Advance(default); Check(arena.Driver.Failure.Length == 0, arena.Driver.Failure, false);
            }
        }
    }

    private async Task Until(Func<bool> condition, string message, int maximum = 600)
    { for (int i = 0; i < maximum && !condition(); i++) { await Frames(1); } Check(condition(), message); }
    private void Focus(Vector3 target, Vector3 offset)
    { foreach (var camera in _cameras) { camera.GlobalPosition = target + offset; camera.LookAt(target); } }
    private async Task Capture(string name, int peer = 0)
    {
        if (DisplayServer.GetName() == "headless") { return; }
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = _views[peer].GetTexture().GetImage(); image.SavePng(System.IO.Path.Combine(_output, name + ".png"));
    }
    private static void AddBox(Node arena, Vector3 position, Vector3 size)
    {
        var body = new StaticBody3D { Position = position, CollisionLayer = 1 };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new(.22f, .25f, .28f) } });
        arena.AddChild(body);
    }
    private void Check(bool valid, string message, bool record = true)
    { if (!valid) { throw new InvalidOperationException(message); } if (record) { _evidence.Add("VERIFIED: " + message); GD.Print(message); } }
    private void Timing(string name, List<double> samples)
    { samples.Sort(); _evidence.Add($"OBSERVED: {name}: median {samples[samples.Count / 2]:F2} ms; p95 {samples[(int)(samples.Count * .95)]:F2} ms. Process delta at 60 FPS cap, not GPU time."); }
    private static IEnumerable<Node> Descendants(Node node)
    { foreach (var child in node.GetChildren()) { yield return child; foreach (var descendant in Descendants(child)) { yield return descendant; } } }
    public override void _ExitTree() { foreach (var gateway in _gateways) { gateway.Dispose(); } }
}
