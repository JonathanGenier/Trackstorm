using System.Net;
using System.Net.Sockets;
using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Two real UDP peers exercising native weapon rays, feedback and sustained resource ownership.</summary>
public sealed partial class MachineGunIntegrationChecks : Node
{
    private readonly List<GameNetworkingSocketsTransport> _gateways = new();
    private readonly List<NetworkVehicleArena> _arenas = new();
    private readonly List<SubViewport> _views = new();
    private int _frames;
    private int _boundary;
    private int _stage;
    private int _scenario;
    private bool _held;
    private bool _press;
    private bool _done;
    private bool _movingTarget;
    private bool _tracking;
    private float _playRange = 10;
    private int _shots;
    private int _hits;
    private double _nearHitRate;
    private Label? _playStatus;
    private int _remaining;
    private float _health;
    private float _nearLoss;
    private readonly List<string> _evidence = new();

    public override void _Ready()
    {
        Engine.MaxFps = 60;
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        string endpoint = $"127.0.0.1:{((IPEndPoint)socket.Client.LocalEndPoint!).Port}";
        socket.Close();
        for (int i = 0; i < 2; i++)
        {
            var gateway = new GameNetworkingSocketsTransport();
            ulong server = 0;
            if (i == 0) { gateway.Listen(TransportEndpoint.DirectIp(endpoint)); }
            else { server = gateway.Connect(TransportEndpoint.DirectIp(endpoint)); }
            if (OS.GetCmdlineUserArgs().Contains("--machine-gun-impaired")) { gateway.ConfigureSimulation(new NetworkSimulation(30, 5, 2, 0, 0)); }
            _gateways.Add(gateway);
            var view = new SubViewport { Size = new Vector2I(1280, 720), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            if (i == 0) { var display = new SubViewportContainer(); AddChild(display); display.AddChild(view); }
            else { AddChild(view); }
            _views.Add(view);
            var arena = new NetworkVehicleArena { PrototypeMapForVerification = true };
            arena.Initialize(gateway, i == 0 ? 88ul : 0, server);
            view.AddChild(arena);
            view.AddChild(new Hud.CombatHud { Vehicle = () => arena.Driver.LocalState, Slot = () => arena.Driver.LocalItem,
                Match = () => arena.Driver.Match, Player = () => arena.Driver.LocalVehicleId });
            var floor = new StaticBody3D { Position = new(0, 200, 0), CollisionLayer = 1 };
            floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(600, 1, 600) } });
            floor.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new(600, 1, 600) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.23f, 0.25f, 0.28f) } });
            arena.AddChild(floor);
            var camera = new Camera3D { Position = new(6, 206, 9) };
            arena.AddChild(camera);
            camera.LookAt(new Vector3(0, 201, -4)); camera.MakeCurrent();
            _arenas.Add(arena);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_done) { if (++_frames > _boundary + 20) { GetTree().Quit(); } return; }
        try
        {
            _frames++;
            if (_stage > 0) { PositionVehicles(); }
            for (int i = 0; i < _arenas.Count; i++)
            {
                bool shooter = i == (_scenario == 4 ? 1 : 0);
                _arenas[i].Advance(new InputFrame(0, 0, 0, 0, shooter && _held ? InputButtons.UseItem : 0, shooter && _press ? InputButtons.UseItem : 0, 0));
                Check(_arenas[i].Driver.Failure.Length == 0, _arenas[i].Driver.Failure);
            }
            _press = false;
            if (_playStatus is not null)
            {
                var authority = _arenas[0].Driver.Host!;
                var gun = authority.Items.Slots.FirstOrDefault(s => s.Vehicle == 1)?.Ammo;
                _playStatus.Text = $"{(_held ? "FIRING" : "RELEASED")} · {gun?.Remaining ?? 0} rounds · Range {_playRange} m · Target HP {authority.World.GetVehicle(2).Damage.CurrentHP:0.0}\n{(_movingTarget ? "MOVING TARGET" : "STATIONARY")} · {(_tracking ? "TRACKING" : "FIXED AIM")}";
            }
            Check(_stage == 8 || _frames - _boundary < 1800, $"machine gun stage {_stage} timeout");
            var host = _arenas[0].Driver.Host!;
            ulong shooterId = _scenario == 4 ? 2ul : 1ul;
            ulong targetId = _scenario == 4 ? 1ul : 2ul;
            switch (_stage)
            {
                case 0 when _arenas.All(a => a.Driver.Latest?.Vehicles.Count == 2):
                    Check(host.TryConfigure(0, new Dictionary<string, double> { ["match.countdown_ticks"] = 1, ["match.minimum_players"] = 1, ["damage.max_hp"] = 10000 }, out _), "fixture health and countdown");
                    var ray = _arenas[0].Driver.RaycastWeapon!;
                    _arenas[0].Driver.RaycastWeapon = (owner, start, end) =>
                    {
                        Check(Math.Abs(N.Vector3.Distance(start, end) - 225) < 0.001, "225 m native hard ray length");
                        var hit = ray(owner, start, end);
                        if (_stage == 3) { _shots++; if (hit is { Vehicle: > 0 }) { _hits++; } }
                        return hit;
                    };
                    Next("Two native UDP peers admitted; elevated isolated fixture separates weapon rays from map obstacles.");
                    break;
                case 1 when OS.GetCmdlineUserArgs().Contains("--machine-gun-playtest"):
                    BeginPlaytest();
                    break;
                case 1 when _frames - _boundary > 30:
                    foreach (ulong id in new ulong[] { 1, 2 }) { host.Items.RemovePlayer(id); }
                    Check(host.Items.Grant(host.World, shooterId, HeldItem.MachineGun), "machine gun grant");
                    Check(host.Items.Grant(host.World, shooterId, HeldItem.Wrench), "second slot grant");
                    Next($"Scenario {_scenario}: fresh magazine and independent Wrench acquired.");
                    break;
                case 2 when _frames - _boundary > 30:
                    _health = host.World.GetVehicle(targetId).Damage.CurrentHP;
                    _shots = 0; _hits = 0;
                    _held = true; _press = true;
                    Next("Held input starts through the production driver.");
                    break;
                case 3 when _frames - _boundary > (_scenario == 1 ? 360 : 120):
                    _held = false;
                    float loss = _health - host.World.GetVehicle(targetId).Damage.CurrentHP;
                    var slot = host.Items.Slots.Single(s => s.Vehicle == shooterId);
                    int expectedRounds = _scenario == 1 ? 320 : 640;
                    Check(Math.Abs(slot.Ammo!.Remaining - expectedRounds) < 30, $"sustained round budget {slot.Ammo.Remaining}");
                    if (_scenario is 0 or 4) { Check(loss > 200 && loss < 400, $"close-range pressure {loss}"); _nearLoss = loss; }
                    if (_scenario == 0)
                    {
                        _nearHitRate = _hits / (double)_shots;
                        foreach (var arena in _arenas)
                        {
                            int impacts = arena.FindChildren("BulletImpactSparks", "GPUParticles3D", true, false).Count;
                            Check(impacts > 0 && impacts <= 128, "bounded hit particles on both peers");
                            GD.Print($"Native impact feedback: {impacts} live spark emitters on peer {arena.Driver.LocalVehicleId}.");
                        }
                    }
                    if (_scenario == 1) { Check(loss > 0 && loss < _nearLoss * 0.5f, $"native falloff {loss}"); Check(_hits / (double)_shots < _nearHitRate * 0.8, "far hit reliability below close range"); }
                    GD.Print($"Native pattern scenario {_scenario}: {_hits}/{_shots} vehicle hits.");
                    if (_scenario is 2 or 3) { Check(loss == 0, $"range/cover rejects damage {loss}"); }
                    Capture($"firing-{_scenario}.png");
                    Next($"Scenario {_scenario}: {loss:0.00} HP loss; {slot.Ammo.Remaining} rounds remain.");
                    break;
                case 4 when _frames - _boundary > 45:
                    _remaining = host.Items.Slots.Single(s => s.Vehicle == shooterId).Ammo!.Remaining;
                    Next("Released input and allowed delayed commands to settle.");
                    break;
                case 5 when _frames - _boundary > 60:
                    Check(host.Items.Slots.Single(s => s.Vehicle == shooterId).Ammo!.Remaining == _remaining, "release preserves exact rounds");
                    Check(_arenas[1].Driver.ItemState!.Slots.Single(s => s.Vehicle == shooterId).Ammo!.Remaining == _remaining, "remote ammo converges");
                    Check(host.Items.Slots.Single(s => s.Vehicle == shooterId).SecondItem == HeldItem.Wrench, "second slot isolated");
                    if (_scenario < 4) { _scenario++; _stage = 1; _boundary = _frames; break; }
                    _held = true; _press = true;
                    Next("Remote reuses its partial magazine until exhaustion.");
                    break;
                case 6 when host.Items.Slots.Single(s => s.Vehicle == 2).Item == HeldItem.None:
                    _held = false;
                    Check(Math.Abs((_frames - _boundary) - _remaining * 60.0 / 80) < 35, "remaining firing duration matches discrete ammo");
                    Check(host.Items.Slots.Single(s => s.Vehicle == 2).SecondItem == HeldItem.Wrench, "full depletion preserves Wrench");
                    Next("Remote magazine exhausted on schedule through the authoritative held-item lifecycle.");
                    break;
                case 7 when _frames - _boundary > 60:
                    Check(_arenas[1].Driver.LocalItem is { Item: HeldItem.None, Ammo: null, SecondItem: HeldItem.Wrench }, "remote exhaustion HUD boundary");
                    Check(host.World.State.Match!.Players.All(p => p.CircusScore == 0), "no item-damage Circus scoring");
                    Capture("exhausted.png");
                    GD.Print("Machine gun integration passed: " + string.Join("\n", _evidence));
                    _done = true; _boundary = _frames;
                    foreach (var arena in _arenas) { arena.QueueFree(); }
                    foreach (var gateway in _gateways) { gateway.Dispose(); }
                    break;
            }
        }
        catch (Exception error)
        {
            GD.PrintErr(error);
            foreach (var gateway in _gateways) { gateway.Dispose(); }
            GetTree().Quit(1);
        }
    }

    private void PositionVehicles()
    {
        var host = _arenas[0].Driver.Host!;
        var world = host.World.State;
        float distance = _stage == 8 ? _playRange : _scenario switch { 1 => 150, 2 => 228, _ => 8 };
        float lateral = _stage == 8 && _movingTarget ? 3 * MathF.Sin(_frames / 90f) : 0;
        host.World.Restore(new(world.Tick, world.LastInput, world.Vehicles.Select(v =>
        {
            bool shooter = v.VehicleId == (_scenario == 4 ? 2ul : 1ul);
            // Stationary controlled geometry isolates range/HP from driver accuracy; ordinary movement is untouched.
            var orientation = shooter && _stage == 8 && _tracking ? N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, -MathF.Atan2(lateral, distance)) : N.Quaternion.Identity;
            var pose = new VehiclePhysicsState(new N.Vector3(shooter ? 0 : lateral, 201.65f, shooter ? 0 : -distance), orientation, N.Vector3.Zero, N.Vector3.Zero);
            _arenas[0].Bodies[v.VehicleId].Apply(pose);
            return new VehicleSnapshot(v.VehicleId, v.LifeId, new VehicleState(world.Tick, pose, true, false, 0, 0), v.Damage, pose);
        }), world.Match));
        foreach (var arena in _arenas)
        {
            var wall = arena.GetNodeOrNull<StaticBody3D>("WeaponCover");
            if (_scenario == 3 && wall is null)
            {
                wall = new StaticBody3D { Name = "WeaponCover", Position = new Vector3(0, 202, -3), CollisionLayer = 1 };
                wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(8, 4, 0.5f) } });
                wall.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(8, 4, 0.5f) } });
                arena.AddChild(wall);
            }
            else if (_scenario != 3 && wall is not null) { wall.CollisionLayer = 0; wall.QueueFree(); }
        }
    }

    private void BeginPlaytest()
    {
        _stage = 8; _scenario = 0;
        var layer = new CanvasLayer(); AddChild(layer);
        var panel = new VBoxContainer { Position = new Vector2(900, 160), CustomMinimumSize = new Vector2(350, 0) }; layer.AddChild(panel);
        _playStatus = new Label { Text = "Machine gun runtime playtest" }; panel.AddChild(_playStatus);
        void Button(string text, Action action)
        {
            var button = new Button { Text = text }; panel.AddChild(button); button.Pressed += action;
        }
        void Refill()
        {
            _held = false;
            var host = _arenas[0].Driver.Host!;
            host.Items.RemovePlayer(1); host.Items.Grant(host.World, 1, HeldItem.MachineGun); host.Items.Grant(host.World, 1, HeldItem.Wrench);
        }
        Button("Fire / release", () => { _held = !_held; _press = _held; });
        Button("Moving / stationary target", () => _movingTarget = !_movingTarget);
        Button("Tracking / fixed aim", () => _tracking = !_tracking);
        foreach (float range in new[] { 5f, 75f, 150f, 228f }) { Button($"Range {range} m", () => _playRange = range); }
        Button("Refill magazine", Refill);
        Button("Finish playtest", () => { _done = true; _boundary = _frames; foreach (var arena in _arenas) { arena.QueueFree(); } foreach (var gateway in _gateways) { gateway.Dispose(); } GetTree().Quit(); });
        Refill();
    }

    private void Capture(string name)
    {
        if (DisplayServer.GetName() == "headless") { return; }
        string path = ProjectSettings.GlobalizePath("res://.godot/machine-gun-checks");
        System.IO.Directory.CreateDirectory(path);
        using var frame = _views[0].GetTexture().GetImage();
        frame.SavePng(System.IO.Path.Combine(path, name));
    }
    private void Next(string message) { GD.Print(message); _evidence.Add(message); _stage++; _boundary = _frames; }
    private static void Check(bool condition, string message) { if (!condition) { throw new InvalidOperationException(message); } }
}
