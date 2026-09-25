using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Sessions;
using Trackstorm.Core.Simulation;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Production application loading and physical driving through ordinary authored pickup boxes.</summary>
public sealed partial class PickupDriveChecks : Node
{
    private readonly List<DevelopmentSession> _sessions = [];
    private int _frames;
    private int _boundary;
    private int _stage;
    private float _nearest = float.MaxValue;
    private int _trial;
    private ulong _tokenBefore;
    private ulong _randomBefore;
    private readonly List<SubViewport> _views = [];
    private string _output = "";
    private bool Motion => OS.GetCmdlineUserArgs().Contains("--pickup-motion");
    private int _motionTrial;
    private int _motionTicks;
    private int _successes;
    private float _segmentNearest;
    private float _crossingSpeed;
    private readonly List<string> _motionTrace = [];
    private readonly Dictionary<string, (int Attempts, int Successes, float MinSpeed, float MaxSpeed)> _counts = [];
    private int MotionDriver => _motionTrial / 90;
    private float MotionSpeed => new[] { 8f, 40f, 65f }[(_motionTrial / 30) % 3];
    private float MotionOffset => new[] { 0f, 1.5f, 2.8f }[(_motionTrial / 10) % 3];
    private bool OldMap => OS.GetCmdlineUserArgs().Contains("--pickup-old-map");
    private string MarkerId => OldMap ? "item-03" : "item-triple-01-2";
    private int DriverIndex => _trial / 9;
    private int Case => _trial % 9;
    private HeldItem Selected => Case < 5 ? ItemRegistry.All[Case].Identity : Case == 5 ? HeldItem.Wrench : Case == 6 ? HeldItem.ProxyMine : HeldItem.Missile;

    /// <inheritdoc/>
    public override void _Ready()
    {
        Engine.MaxFps = 60;
        _output = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--pickup-output=", StringComparison.Ordinal))?[16..]
            ?? ProjectSettings.GlobalizePath($"res://.godot/pickup-drive-checks/{(OldMap ? "old" : "oval")}");
        System.IO.Directory.CreateDirectory(_output);
        using var port = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
        string endpoint = $"127.0.0.1:{((System.Net.IPEndPoint)port.Client.LocalEndPoint!).Port}";
        port.Close();
        for (int i = 0; i < 2; i++)
        {
            var view = new SubViewport { Size = new(1280, 720), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            _views.Add(view);
            if (i == 0) { var display = new SubViewportContainer(); AddChild(display); display.AddChild(view); }
            else { AddChild(view); }
            var session = new DevelopmentSession();
            view.AddChild(session);
            session.Open(i == 0, endpoint, $"Pickup driver {i}");
            if (i == 0 && OS.GetCmdlineUserArgs().Contains("--pickup-impaired") && session.Gateway is GameNetworkingSocketsTransport gateway)
                gateway.ConfigureSimulation(new(30, 5, 2, 0, 0));
            _sessions.Add(session);
            var hud = new Hud.CombatHud { Vehicle = () => session.Arena?.LocalState, Slot = () => session.Arena?.Driver.LocalItem };
            view.AddChild(hud);
        }
    }

    /// <inheritdoc/>
    public override void _PhysicsProcess(double delta)
    {
        try
        {
            _frames++;
            var motionArena = _sessions[0].Arena;
            var before = _stage == 6 ? motionArena!.Driver.Host!.World.GetVehicle(_sessions[MotionDriver].Arena!.Driver.LocalVehicleId) : null;
            for (int i = 0; i < _sessions.Count; i++)
                _sessions[i].Advance(new InputFrame((ulong)_frames, 0, _stage == 3 && i == DriverIndex ? ushort.MaxValue : (ushort)0, 0, 0, 0, 0));
            if (_stage == 6) { AdvanceMotion(before!); return; }
            if (_frames - _boundary > 1800) throw new InvalidOperationException($"Pickup drive stage {_stage} timed out.");
            var host = _sessions[0];
            if (_stage == 0 && _sessions.All(s => s.Lobby?.State?.Players.Count == 2))
            {
                _sessions[1].Lobby!.Request(LobbyCommand.Ready, true);
                _stage++;
            }
            else if (_stage == 1 && host.Lobby!.State!.CanStart)
            {
                if (OldMap) Require(host.Lobby.SelectMap(MatchMap.OldMap), "Select production Old Map.");
                if (!host.ForceDeveloperStart()) throw new InvalidOperationException("Application start rejected.");
                _stage++;
            }
            else if (_stage == 2 && _sessions.All(s => s.Arena?.Driver.Match?.Phase == Core.Matches.MatchPhase.Active))
            {
                if (Motion) PrepareMotion(); else Prepare();
            }
            else if (_stage == 3)
            {
                var arena = host.Arena!;
                var marker = arena.MapConfiguration.Items.Single(m => m.Id == MarkerId);
                ulong player = _sessions[DriverIndex].Arena!.Driver.LocalVehicleId;
                _nearest = Math.Min(_nearest, N.Vector3.Distance(arena.Driver.Host!.World.GetVehicle(player).Movement.Physics.Position, marker.Position));
                if (_frames - _boundary > 150)
                {
                    var authority = arena.Driver.Host!;
                    var slot = authority.Items.Slots.Single(s => s.Vehicle == player);
                    var spawn = authority.Spawns!.States.Single(s => s.Id == marker.Id);
                    Require(_nearest < 3, "The native car physically crossed the box radius.");
                    if (Case == 7)
                        Require(slot.Full && authority.Items.TokenHighWater == _tokenBefore && authority.Spawns.RandomState == _randomBefore && spawn.Available, "Full inventory preserves box, tokens and RNG.");
                    else
                        Require(spawn.ClaimedBy == player && spawn.Item == Selected && spawn.Token > _tokenBefore && (slot.Item == Selected || slot.SecondItem == Selected), "Normal driving awards the selected registered item.");
                    Require(_sessions.All(s => s.Arena!.Driver.ItemState!.Slots.Contains(slot)), "Host and remote hold identical two-slot state.");
                    GD.Print($"Driver {DriverIndex}, case {Case}, closest {_nearest:F3} m, inventory {slot.Item}/{slot.SecondItem}: {(Case == 7 ? "full rejection" : Selected)} passed.");
                    if (DisplayServer.GetName() != "headless") _views[DriverIndex].GetTexture().GetImage().SavePng(System.IO.Path.Combine(_output, $"driver-{DriverIndex}-case-{Case}.png"));
                    if (Case == 7) Require(_sessions[DriverIndex].Arena!.Driver.RequestItemUse(), "Consume first-slot Wrench through normal input command before retry.");
                    _trial++;
                    _boundary = _frames;
                    _stage = 4;
                }
            }
            else if (_stage == 4 && _frames - _boundary > 90)
            {
                if (_trial < 18) Prepare();
                else { GD.Print("Pickup drive passed: both application peers acquired all five items, two sequential slots, full rejection and use/retry."); GetTree().Quit(); _stage = 5; }
            }
        }
        catch (Exception exception) { GD.PrintErr(exception); GetTree().Quit(1); }
    }

    private void PrepareMotion()
    {
        var arena = _sessions[0].Arena!;
        var authority = arena.Driver.Host!;
        Require(arena.MapConfiguration.Items.Count == (OldMap ? 8 : 27), "Preserve the complete production marker contract.");
        ulong player = _sessions[MotionDriver].Arena!.Driver.LocalVehicleId;
        authority.Items.RemovePlayer(player);
        Require(arena.Driver.TryConfigure(new Dictionary<string, double> { ["spawns.cooldown_ticks"] = 1 }, out string error), error);
        var marker = arena.MapConfiguration.Items.Single(m => m.Id == MarkerId);
        var world = authority.World;
        float phase = (_motionTrial % 10) * MotionSpeed / 600;
        world.Restore(new SimulationState(world.State.Tick, world.State.LastInput, world.State.Vehicles.Select(v =>
        {
            var position = v.VehicleId == player ? marker.Position + new N.Vector3(-6 - phase, 0.9f, MotionOffset) : world.Arena.Spawn((int)v.VehicleId - 1).Position;
            var pose = new VehiclePhysicsState(position, N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, -MathF.PI / 2), v.VehicleId == player ? N.Vector3.UnitX * MotionSpeed : N.Vector3.Zero, N.Vector3.Zero);
            return new VehicleSnapshot(v.VehicleId, v.LifeId, new VehicleState(world.State.Tick, pose, true, false, 0, 0), v.Damage, pose);
        }).ToArray(), world.State.Match));
        _tokenBefore = authority.Items.TokenHighWater;
        _segmentNearest = float.MaxValue;
        _motionTrace.Clear();
        _motionTicks = 0;
        _stage = 6;
        _boundary = _frames;
    }

    private void AdvanceMotion(VehicleSnapshot before)
    {
        var arena = _sessions[0].Arena!;
        var host = arena.Driver.Host!;
        var after = host.World.GetVehicle(before.VehicleId);
        var marker = arena.MapConfiguration.Items.Single(m => m.Id == MarkerId);
        var a = before.Movement.Physics.Position;
        var b = after.Movement.Physics.Position;
        var d = b - a;
        float t = d.LengthSquared() > 0 ? Math.Clamp(N.Vector3.Dot(marker.Position - a, d) / d.LengthSquared(), 0, 1) : 0;
        float closest = N.Vector3.Distance(a + d * t, marker.Position);
        if (closest < _segmentNearest) { _segmentNearest = closest; _crossingSpeed = d.Length() * 60; }
        if (closest < 3.5f) _motionTrace.Add($"tick {host.World.State.Tick}: before={N.Vector3.Distance(a, marker.Position):F4}, after={N.Vector3.Distance(b, marker.Position):F4}, segment={closest:F4}, speed={d.Length() * 60:F2}");
        if (++_motionTicks < Math.Ceiling(14 / MotionSpeed * 60) + 12) return;
        bool success = host.Items.Slots.Any(s => s.Vehicle == before.VehicleId && (s.Token > _tokenBefore || s.SecondToken > _tokenBefore));
        if (success && _sessions.Any(s => !s.Arena!.Driver.ItemState!.Slots.Contains(host.Items.Slots.Single(v => v.Vehicle == before.VehicleId)))) return;
        string key = $"driver={MotionDriver} speed={MotionSpeed} offset={MotionOffset}";
        var count = _counts.GetValueOrDefault(key);
        _counts[key] = (count.Attempts + 1, count.Successes + (success ? 1 : 0), count.Attempts == 0 ? _crossingSpeed : Math.Min(count.MinSpeed, _crossingSpeed), Math.Max(count.MaxSpeed, _crossingSpeed));
        Require(_segmentNearest <= 3, "Only geometrically valid crossings count in the reliability sample.");
        if (success) _successes++;
        else GD.Print($"MISSED {key} phase={_motionTrial % 10} sweptClosest={_segmentNearest:F4}\n" + string.Join("\n", _motionTrace));
        _motionTrial++;
        if (_motionTrial % 10 == 0) GD.Print($"{key}: {_counts[key].Successes}/{_counts[key].Attempts}");
        if (_motionTrial < 180) { PrepareMotion(); return; }
        var lines = _counts.Select(p => $"{p.Key}: {p.Value.Successes}/{p.Value.Attempts}; measured crossing speed {p.Value.MinSpeed:F2}–{p.Value.MaxSpeed:F2} m/s").ToArray();
        System.IO.File.WriteAllLines(System.IO.Path.Combine(_output, "motion-counts.txt"), lines);
        GD.Print(string.Join("\n", lines));
        GD.Print($"Pickup motion attempts: {_successes}/180.");
        Require(_successes == 180, "Every valid native crossing must acquire an item.");
        GetTree().Quit();
        _stage = 5;
    }

    private void Prepare()
    {
        var arena = _sessions[0].Arena!;
        var authority = arena.Driver.Host!;
        ulong player = _sessions[DriverIndex].Arena!.Driver.LocalVehicleId;
        if (Case <= 5) authority.Items.RemovePlayer(player); // Isolate each pool trial; never grant an item directly.
        var edits = ItemRegistry.All.ToDictionary(i => $"spawns.{i.Key}_weight", i => i.Identity == Selected ? 1d : 0d);
        edits["spawns.cooldown_ticks"] = 60;
        Require(arena.Driver.TryConfigure(edits, out string error), error);
        var marker = arena.MapConfiguration.Items.Single(m => m.Id == MarkerId);
        var world = authority.World;
        var states = world.State.Vehicles.Select(v =>
        {
            var position = v.VehicleId == player ? marker.Position + new N.Vector3(-12, 0.8f, 0) : world.Arena.Spawn((int)v.VehicleId - 1).Position;
            var pose = new VehiclePhysicsState(position, N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, -MathF.PI / 2), v.VehicleId == player ? N.Vector3.UnitX * 8 : N.Vector3.Zero, N.Vector3.Zero);
            return new VehicleSnapshot(v.VehicleId, v.LifeId, new VehicleState(world.State.Tick, pose, false, false, 0, 0), v.Damage, pose);
        }).ToArray();
        world.Restore(new SimulationState(world.State.Tick, world.State.LastInput, states, world.State.Match));
        _tokenBefore = authority.Items.TokenHighWater;
        _randomBefore = authority.Spawns!.RandomState;
        _nearest = float.MaxValue;
        _boundary = _frames;
        _stage = 3;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}



