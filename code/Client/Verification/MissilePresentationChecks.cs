using System.Net;
using System.Net.Sockets;
using Godot;
using Trackstorm.Client.Items;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Transport;

namespace Trackstorm.Client.Verification;

/// <summary>Native production Car/arena and two real UDP peers exercise the complete Missile presentation lifecycle.</summary>
public sealed partial class MissilePresentationChecks : Node
{
    private readonly List<NetworkVehicleArena> _arenas = [];
    private readonly List<GameNetworkingSocketsTransport> _gateways = [];
    private readonly List<SubViewport> _views = [];
    private readonly List<Camera3D> _cameras = [];
    private readonly List<string> _evidence = [];
    private readonly string _output = ProjectSettings.GlobalizePath("res://.godot/ts240-checks");
    private int _frame;
    private Vector3? _flightFocus;
    private List<double>? _renderDurations;

    public override void _Process(double delta) => _renderDurations?.Add(delta * 1000);

    public override void _Ready() => CallDeferred(MethodName.Run);

    private async void Run()
    {
        try
        {
            Engine.MaxFps = 60;
            System.IO.Directory.CreateDirectory(_output);
            using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            string endpoint = $"127.0.0.1:{((IPEndPoint)socket.Client.LocalEndPoint!).Port}";
            socket.Close();
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
                arena.Initialize(gateway, i == 0 ? 240ul : 0, server); view.AddChild(arena);
                _arenas.Add(arena);
                var camera = new Camera3D { Far = 1500, Fov = 48 };
                arena.AddChild(camera); _cameras.Add(camera);
            }
            await Until(() => _arenas.All(a => a.Driver.Latest?.Vehicles.Count == 2), "Two production UDP peers connected", 1200);
            var host = _arenas[0].Driver.Host!;
            await Frames(90);
            host.Items.RemovePlayer(1);
            await Frames(300);
            await InspectBody();
            Require(_arenas[0].Driver.GiveDeveloperItem(HeldItem.Missile), "Production grant");
            await Frames(4);
            Require(!_arenas[0].Driver.RequestItemUse(), "Early tap rejected locally");
            var slot = host.Items.Slots.Single(s => s.Vehicle == 1);
            Require(!host.Items.RequestUse(host.World, 1, slot.Life, slot.Token), "Early tap rejected by authority");
            await Capture("01-compact");
            await Frames(25); await Capture("02-rack");
            await Frames(25); await Capture("03-extension");
            for (int step = 0; step < 5; step++)
            {
                await Frames(6);
                await CaptureMountedBody("polish-live-extension-" + step);
            }
            await Until(MissileReady, "Owner and remote finish deployment");
            await Capture("04-ready");
            await CaptureMountedBody("polish-mounted-ready");
            _arenas[0].GetNode<Camera3D>("ChaseCamera").Current = true;
            await Capture("04a-player-camera");
            _cameras[0].Current = true;
            Require(host.Items.Missiles.Count == 0, "No queued shot");
            for (int i = 0; i < 150; i++)
            {
                await Frame(new InputFrame((ulong)_frame, (short)(i < 75 ? 10000 : -10000), 22000, 0, 0, 0, 0));
            }
            await Capture("05-driving");
            await Airborne();
            Require(MissileReady(), "Ready mechanism remains coherent after driving");
            // Accepted articulation controls the turret only; existing forward launch is retained.
            for (int i = 0; i < 240; i++)
            {
                slot = host.Items.Slots.Single(s => s.Vehicle == 1);
                host.AimItem(0, 240, slot.Life, slot.Token, slot.SelectionRevision, (ulong)i + 1,
                    System.Numerics.Vector3.Normalize(new(MathF.Sin(i * MathF.Tau / 190), .35f, -MathF.Cos(i * MathF.Tau / 190))));
                await Frame();
            }
            await Capture("06-articulation");
            Require(_arenas[0].Driver.RequestItemUse(), "Ready press accepted");
            await Frames(2);
            Require(host.Items.Slots.Single(s => s.Vehicle == 1).Item == HeldItem.None, "Exactly one pickup round consumed");
            Require(!_arenas[0].Driver.RequestItemUse(), "No replacement shot");
            Require(_arenas[0].Driver.GiveDeveloperItem(HeldItem.Oil), "New pickup during stow retained");
            ulong nextToken = host.Items.Slots.Single(s => s.Vehicle == 1).Active.Token;
            await Frames(12); await Capture("07-empty-stow");
            Require(_arenas.All(a => a.Bodies[1].Rack.PresentedItem == HeldItem.Missile), "Both peers retain returning Missile mount");
            var launchers = _arenas.Select(a => a.Bodies[1].Rack.GetParent<Node3D>().FindChild("MissileLauncher", true, false)).OfType<MissileLauncher>().ToArray();
            Require(launchers.Length == 2 && launchers.All(l => !l.Loaded), "Empty mount never reloads");
            bool closed = false;
            for (int i = 0; i < 150; i++)
            {
                await Frame();
                closed |= _arenas[0].Bodies[1].Rack.Progress <= .001f;
            }
            Require(closed, "Rack reaches full closed boundary before new deployment");
            Require(host.Items.Slots.Single(s => s.Vehicle == 1).Active.Token == nextToken, "New pickup capability preserved");
            Require(_arenas.All(a => a.Bodies[1].Rack.PresentedItem == HeldItem.Oil), "Next weapon appears on both peers after complete stow");
            await Capture("08-next-weapon");
            await Replacement(HeldItem.Missile);
            await Replacement(HeldItem.Nitro);
            await StressFlight();
            await Frames(330);
            Require(_arenas.All(a => Descendants(a).OfType<MissileFlightVisual>().Count() == 0), "Impact/expiry removes every projectile VFX owner");
            System.IO.File.WriteAllLines(System.IO.Path.Combine(_output, "presentation-evidence.txt"), _evidence);
            GD.Print("Missile presentation integration passed: " + _evidence.Count(e => e.StartsWith("VERIFIED:", StringComparison.Ordinal)) + " assertions.");
            foreach (var arena in _arenas) { arena.QueueFree(); }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            foreach (var gateway in _gateways) { gateway.Dispose(); }
            GetTree().Quit();
        }
        catch (Exception error) { GD.PrintErr(error); GetTree().Quit(1); }
    }

    private async Task Airborne()
    {
        var world = _arenas[0].Driver.Host!.World;
        var snapshot = world.State;
        world.Restore(new(snapshot.Tick, snapshot.LastInput, snapshot.Vehicles.Select(state =>
        {
            if (state.VehicleId != 1) { return state; }
            var pose = new Trackstorm.Core.Vehicles.VehiclePhysicsState(state.ObservedPhysics.Position + System.Numerics.Vector3.UnitY * 5,
                state.ObservedPhysics.Orientation, System.Numerics.Vector3.UnitY * 4, System.Numerics.Vector3.Zero);
            _arenas[0].Bodies[1].Apply(pose);
            return new Trackstorm.Core.Vehicles.VehicleSnapshot(state.VehicleId, state.LifeId,
                new(snapshot.Tick, pose, false, false, 0, 0), state.Damage, pose);
        }), snapshot.Match));
        await Frames(20); await Capture("05a-airborne");
        Require(!world.GetVehicle(1).Movement.Grounded && MissileReady(), "Native airborne Car retains ready mounted assembly (fixture launch)");
        await Until(() => world.GetVehicle(1).Movement.Grounded, "Native Car lands after fixture launch", 360);
        await Frames(30); await Capture("05b-landed");
        Require(MissileReady(), "Landing preserves mounted capability on both peers");
    }

    private async Task Replacement(HeldItem next)
    {
        var host = _arenas[0].Driver.Host!;
        host.Items.RemovePlayer(1);
        await Frames(150);
        Require(_arenas[0].Driver.GiveDeveloperItem(HeldItem.Missile), $"Repeated Missile grant before {next}");
        await Until(MissileReady, "Repeated Missile ready on both peers");
        Require(_arenas[0].Driver.RequestItemUse(), "Repeated Missile launch accepted");
        await Frames(2);
        Require(_arenas[0].Driver.GiveDeveloperItem(next), $"{next} pickup accepted during empty return");
        await Frames(12);
        Require(_arenas.All(a => Descendants(a.Bodies[1].Rack.GetParent()).OfType<MissileLauncher>().Any(l => !l.Loaded)),
            $"{next} pickup cannot refill or replace the returning empty mount");
        bool closed = false;
        for (int i = 0; i < 190; i++) { await Frame(); closed |= _arenas[0].Bodies[1].Rack.Progress <= .001f; }
        Require(closed && _arenas.All(a => a.Bodies[1].Rack.PresentedItem == next), $"Full closure precedes {next} deployment on both peers");
    }

    private bool MissileReady() => _arenas.All(a => a.Driver.ItemState?.Slots.SingleOrDefault(s => s.Vehicle == 1) is { } inventory &&
        a.Bodies[1].Rack.IsMissileReady(inventory));

    private async Task Frame(InputFrame input = default)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        foreach (var arena in _arenas) { arena.Advance(input); Require(arena.Driver.Failure.Length == 0, arena.Driver.Failure, false); }
        _frame++;
        foreach (var (arena, index) in _arenas.Select((a, i) => (a, i)))
        {
            if (!arena.Bodies.TryGetValue(1, out var body)) { continue; }
            var camera = _cameras[index]; camera.Current = true;
            if (_flightFocus is { } flightFocus)
            {
                camera.GlobalPosition = flightFocus + new Vector3(-14, 8, 17);
                camera.LookAt(flightFocus);
                continue;
            }
            Vector3 focus = body.GlobalPosition + Vector3.Up * 1.4f;
            camera.GlobalPosition = body.GlobalPosition + body.GlobalBasis * new Vector3(-4.7f, 3.5f, 5.5f);
            camera.LookAt(focus);
        }
    }
    private async Task Frames(int count) { for (int i = 0; i < count; i++) { await Frame(); } }
    private async Task Until(Func<bool> predicate, string reason, int maximum = 360)
    {
        for (int i = 0; i < maximum && !predicate(); i++) { await Frame(); }
        Require(predicate(), reason);
    }
    private async Task Capture(string name)
    {
        if (DisplayServer.GetName() == "headless") { return; }
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        _views[0].GetTexture().GetImage().SavePng(System.IO.Path.Combine(_output, name + ".png"));
    }
    private async Task StressFlight()
    {
        // Presentation-only stress uses the exact production flight class, independent of damage.
        var visuals = new List<MissileFlightVisual>();
        Vector3 origin = _arenas[0].Bodies[1].GlobalPosition + new Vector3(0, 4, 0);
        _flightFocus = origin;
        await Frames(30);
        var baselineTimes = new List<double>();
        _renderDurations = baselineTimes;
        await Frames(180);
        _renderDurations = null;
        baselineTimes.Sort();
        _evidence.Add($"OBSERVED: Matching two-arena baseline render-frame delta median {baselineTimes[baselineTimes.Count / 2]:F2} ms, p95 {baselineTimes[(int)(baselineTimes.Count * .95)]:F2} ms (60 FPS cap).");
        for (int i = 0; i < 16; i++)
        {
            var visual = new MissileFlightVisual();
            _arenas[0].AddChild(visual); visuals.Add(visual);
        }
        var frameTimes = new List<double>();
        for (int tick = 0; tick < 240; tick++)
        {
            for (int i = 0; i < visuals.Count; i++)
            {
                float angle = tick / 50f + i * .32f;
                visuals[i].Observe(origin + new Vector3(MathF.Sin(angle) * 7, i * .18f, MathF.Cos(angle) * 7),
                    new Vector3(MathF.Cos(angle) * 8.4f, 0, -MathF.Sin(angle) * 8.4f));
            }
            if (tick == 30) { _renderDurations = frameTimes; }
            await Frame();
            if (tick == 90) { await Capture("09-sixteen-flight-vfx"); }
            if (tick == 120)
            {
                var focus = visuals[0].GlobalPosition;
                _cameras[0].GlobalPosition = focus + new Vector3(-2.8f, 1.7f, 3.2f);
                _cameras[0].LookAt(focus);
                await Capture("10-flight-close");
            }
        }
        _renderDurations = null;
        frameTimes.Sort();
        _evidence.Add($"OBSERVED: 16-effect / two-arena render-frame delta median {frameTimes[frameTimes.Count / 2]:F2} ms, p95 {frameTimes[(int)(frameTimes.Count * .95)]:F2} ms (60 FPS cap; Godot process delta, not GPU timing).");
        Require(visuals.All(v => Descendants(v).OfType<GpuParticles3D>().Single(p => p.Name == "BurningRedConfetti").Amount == 56),
            "Increased red confetti remains bounded at 56 live particles per default Missile");
        Require(visuals.All(v => Descendants(v).OfType<MeshInstance3D>().Count(m => m.Name.ToString().StartsWith("FlameCore", StringComparison.Ordinal)) == 3),
            "Each flight owner carries three continuous flame surfaces");
        MissileVfxSettings.Current = MissileVfxSettings.Current with { Density = 0, FlameWidth = .35f };
        await Frames(4);
        Require(visuals.All(v => Descendants(v).OfType<GpuParticles3D>().Count() == 1), "Local density zero removes trails from all live projectiles");
        MissileVfxSettings.Current = new();
        await Frames(4);
        Require(visuals.All(v => Descendants(v).OfType<GpuParticles3D>().Count() == 4), "Local reset restores all four production effect layers");
        _flightFocus = null;
        int particles = visuals.Sum(v => Descendants(v).OfType<GpuParticles3D>().Sum(p => p.Amount));
        Require(particles <= 16 * 144 * 4, "Sixteen production flight effects respect bounded particle allocation");
        foreach (var visual in visuals) { visual.QueueFree(); }
        await Frames(2);
        Require(visuals.All(v => !GodotObject.IsInstanceValid(v)), "All sixteen VFX owners are freed");
    }
    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (var child in node.GetChildren()) { yield return child; foreach (var nested in Descendants(child)) { yield return nested; } }
    }
    private void Require(bool valid, string reason, bool record = true)
    {
        if (!valid) { throw new InvalidOperationException(reason); }
        if (record) { _evidence.Add("VERIFIED: " + reason); GD.Print(reason); }
    }
}
