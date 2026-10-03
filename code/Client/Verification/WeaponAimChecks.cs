using System.Net;
using System.Net.Sockets;
using Godot;
using Trackstorm.Client.Input;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;
using Trackstorm.Core.Networking.Transport;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Production Car/input/HUD aiming through three real UDP peers and native collision worlds.</summary>
public sealed partial class WeaponAimChecks : Node
{
    private readonly List<GameNetworkingSocketsTransport> _wires = [];
    private readonly List<NetworkVehicleArena> _arenas = [];
    private readonly List<SubViewport> _views = [];
    private readonly List<string> _evidence = [];
    private PlayerInput _input = null!;
    private string _output = string.Empty;
    private bool _oval;
    private (N.Vector3 First, N.Vector3 Second)? _heldTargets;
    private float Ground => _oval ? 1.65f : 21.65f;
    private ulong Shooter => _arenas[1].Driver.LocalVehicleId;
    private VehicleChaseCamera Camera => _arenas[1].GetNode<VehicleChaseCamera>("ChaseCamera");

    public override void _Ready() => CallDeferred(MethodName.Run);

    /// <summary>Runs synthetic native input and real transport; physical-controller ergonomics remain a human check.</summary>
    public async void Run()
    {
        try
        {
            Engine.MaxFps = 30;
            _oval = OS.GetCmdlineUserArgs().Contains("--aim-oval");
            _output = OS.GetCmdlineUserArgs().FirstOrDefault(value => value.StartsWith("--aim-output="))?[13..] ?? ProjectSettings.GlobalizePath("res://.godot/aim-checks");
            System.IO.Directory.CreateDirectory(_output);
            _input = new PlayerInput(); AddChild(_input); _input.SetPhysicsProcess(false);
            _input.GameplayAvailable = () => true;
            using var reservation = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            string endpoint = $"127.0.0.1:{((IPEndPoint)reservation.Client.LocalEndPoint!).Port}";
            reservation.Close();
            for (int i = 0; i < 3; i++)
            {
                var wire = new GameNetworkingSocketsTransport(); _wires.Add(wire);
                ulong server = 0;
                if (i == 0) { wire.Listen(TransportEndpoint.DirectIp(endpoint)); }
                else { server = wire.Connect(TransportEndpoint.DirectIp(endpoint)); }
                if (OS.GetCmdlineUserArgs().Contains("--aim-impaired")) { wire.ConfigureSimulation(new(30, 5, 2, 0, 0)); }
                var view = new SubViewport { Size = new(1280, 800), OwnWorld3D = true, RenderTargetUpdateMode = i == 1 ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled };
                if (i == 1) { var display = new SubViewportContainer(); AddChild(display); display.AddChild(view); }
                else { AddChild(view); }
                _views.Add(view);
                var arena = new NetworkVehicleArena { PrototypeMapForVerification = !_oval };
                arena.Initialize(wire, i == 0 ? 261ul : 0, server);
                view.AddChild(arena); _arenas.Add(arena);
                if (i == 1)
                {
                    view.AddChild(new Hud.CombatHud
                    {
                        Vehicle = () => arena.LocalState, Slot = () => arena.Driver.LocalItem,
                        Match = () => arena.Driver.Match, Player = () => arena.Driver.LocalVehicleId,
                        AuthoritativeTick = () => arena.Driver.Latest?.Tick ?? 0,
                    });
                }
                var platform = new StaticBody3D { Position = new(0, 20, 0), CollisionLayer = 1 };
                platform.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(500, 1, 500) } });
                platform.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new(500, 1, 500) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new(.26f, .28f, .30f) } });
                if (!_oval) { arena.AddChild(platform); } else { platform.Free(); }
            }
            _arenas[1].CameraInput = _input.Adapter;
            await Until(() => _arenas.All(arena => arena.Driver.Latest?.Vehicles.Count == 3), "Three UDP peers initialized", 1200);
            var host = _arenas[0].Driver.Host!;
            Require(host.TryConfigure(0, new Dictionary<string, double> { ["match.minimum_players"] = 1, ["match.countdown_ticks"] = 1 }, out _), "Host tuning applied");
            Position(new(0, Ground, 35), new(0, Ground, 0), new(6, Ground, 0));
            host.Items.Grant(host.World, Shooter, HeldItem.MachineGun);
            host.Items.Grant(host.World, Shooter, HeldItem.Missile);
            await Until(() => _arenas[1].AimOverlay.Marker.HasValue && host.Items.Aims.Any(aim => aim.Vehicle == Shooter), "Centered camera cursor appears alongside remote accepted aim");
            await Frames(130);
            await Capture("01-forward-default");
            await AimAt(_arenas[1].Bodies[1].VisualPosition + Vector3.Up * 1.0f);
            await Capture("02-two-forward-targets");
            Require(_arenas[1].AimOverlay.Bounds is { } forwardBounds && forwardBounds.HasPoint(ViewCenter),
                "Center-ray car intersection replaces square with disconnected brackets");
            RequireCentered("Camera intent stays at viewport center while car brackets are shown");
            await AimAt(_arenas[1].Bodies[1].VisualPosition + new Vector3(2.8f, .35f, 0));
            Require(_arenas[1].AimOverlay.Bounds is null, "A near-axis miss returns to the small square without a target lock");
            RequireCentered("Small square is centered when aiming beside a rival");
            await Capture("02a-near-miss-square");
            await AimAt(_arenas[1].Bodies[1].VisualPosition + Vector3.Up);
            Vector3 coverPosition = Camera.GlobalPosition.Lerp(_arenas[1].Bodies[1].VisualPosition + Vector3.Up, .7f);
            var cover = new StaticBody3D { Position = coverPosition, CollisionLayer = 1, CollisionMask = 0 };
            cover.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(3, 3, 1) } });
            _arenas[1].AddChild(cover);
            await Frames(8);
            Require(_arenas[1].AimOverlay.Bounds is null, "Intervening native cover prevents car brackets");
            RequireCentered("Covered car leaves the small square at camera center");
            cover.Free(); await Frames(8);
            Require(_arenas[1].AimOverlay.Bounds.HasValue, "Removing cover restores brackets on the directly intersected car");
            var solution = host.Items.Aims.Single(aim => aim.Vehicle == Shooter);
            GD.Print($"AIM_FORWARD clear={solution.Clear} ready={solution.Ready} yaw={solution.Yaw} pitch={solution.Pitch}");
            float rackYaw = _arenas[1].Bodies[Shooter].Rack.GetParent<Node3D>().GetNode<Node3D>("WeaponRack").Rotation.Y;
            Send(new InputEventMouseMotion { ScreenRelative = new(520, -80) });
            await Frames(4);
            RequireCentered("Square remains centered immediately after mouse yaw and upward tilt under network impairment");
            Require(_arenas[1].AimOverlay.Bounds is null, "Looking away from a car removes its brackets immediately");
            _views[1].Size = new(960, 600); await Frames(4);
            RequireCentered("Cursor follows the new viewport center after resizing");
            _views[1].Size = new(1280, 800); await Frames(4);
            await Frames(70); await Capture("03-side-elevated");
            solution = host.Items.Aims.Single(aim => aim.Vehicle == Shooter);
            Require(Math.Abs(solution.Yaw) > .8f, "Side mouse aim reaches authority with the original camera orbit limits");
            Require(_arenas.All(arena => Math.Abs(arena.Bodies[Shooter].Rack.GetParent<Node3D>().GetNode<Node3D>("WeaponRack").Rotation.Y - rackYaw) < .0001f), "Deployed rack remains fixed on all peers");
            Require(_arenas[2].Driver.AcceptedAims.Any(aim => aim.Vehicle == Shooter && Math.Abs(aim.Yaw) > .8f), "Observer receives accepted articulated aim");
            Send(new InputEventMouseMotion { ScreenRelative = new(520, 0) });
            await Frames(70); await Capture("04-rear-elevated");
            Require(Math.Abs(host.Items.Aims.Single(aim => aim.Vehicle == Shooter).Yaw) > 2, "Rear aim accepted");
            Send(new InputEventMouseMotion { ScreenRelative = new(-1040, 500) });
            await Frames(70); await Capture("05-self-clearance");
            RequireCentered("Downward tilt and weapon self-clearance limits cannot displace the camera cursor");
            solution = host.Items.Aims.Single(aim => aim.Vehicle == Shooter);
            Require(!WeaponAim.IntersectsBody(WeaponAim.Pivot, WeaponAim.Direction(solution.Yaw, solution.Pitch)), "Downward articulation cannot enter owner envelope");
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
            Camera.ResetFollow(); await Frames(5);
            float before = Camera.Rotation.Y;
            float chaseYaw = before;
            Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = .35f });
            await Frames(60);
            RequireCentered("Fine controller camera rotation retains a centered cursor");
            float fine = Math.Abs(Mathf.AngleDifference(before, Camera.Rotation.Y));
            before = Camera.Rotation.Y;
            Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = 1 });
            await Frames(60);
            float fast = Math.Abs(Mathf.AngleDifference(before, Camera.Rotation.Y));
            Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = 0 });
            Require(fine > .01f && fast > fine * 3, $"Native stick fine/full turn separation: fine={fine:0.000}, full={fast:0.000} rad");
            float heldYaw = Camera.Rotation.Y; await Frames(30);
            RequireCentered("Ordinary camera recentering retains a centered cursor");
            Require(Math.Abs(Mathf.AngleDifference(chaseYaw, Camera.Rotation.Y)) < Math.Abs(Mathf.AngleDifference(chaseYaw, heldYaw)) * .2f,
                "Neutral stick restores the original chase view with a weapon selected");
            Position(new(0, Ground, 35), new(0, Ground, 25), new(7, Ground, 25));
            Camera.ResetFollow(); await Frames(20);
            await AimAt(_arenas[1].Bodies[1].VisualPosition + Vector3.Up * .9f);
            await Frames(30); await Capture("06-close-brackets");
            Require(_arenas[1].AimOverlay.Bounds is { } closeBounds && closeBounds.HasPoint(ViewCenter),
                "Directly intersected close car receives brackets instead of a distance-based square fallback");
            Position(new(0, Ground, 35), new(-3.7f, Ground, 35), new(7, Ground, 25));
            Camera.ResetFollow(); await Frames(20);
            await AimAt(_arenas[1].Bodies[1].VisualPosition + Vector3.Up * .9f);
            await Frames(35); await Capture("06b-alongside-brackets");
            Require(_arenas[1].AimOverlay.Bounds is { } alongsideBounds && alongsideBounds.HasPoint(ViewCenter),
                "Directly intersected side-by-side car receives onscreen corner brackets");
            solution = host.Items.Aims.Single(aim => aim.Vehicle == Shooter);
            Require(new Rect2(Vector2.Zero, _views[1].Size).HasPoint(_arenas[1].AimOverlay.Marker!.Value) &&
                !WeaponAim.IntersectsBody(WeaponAim.Pivot, WeaponAim.Direction(solution.Yaw, solution.Pitch)),
                "Alongside camera cursor stays centered while the accepted weapon ray remains chassis-safe");
            Require(solution.Clear || !solution.Ready, "A camera ray blocked by the owner cannot mark the weapon ready");
            Position(new(0, Ground, 35), new(0, Ground + 10, 0), new(5, Ground, 0));
            _heldTargets = (new(0, Ground + 10, 0), new(5, Ground, 0));
            Camera.ResetFollow(); await Frames(5);
            await AimAt(_arenas[1].Bodies[1].VisualPosition + Vector3.Up);
            for (int i = 0; i < 60; i++)
            {
                MoveTargets(new(0, Ground + 10, 0), new(5, Ground, 0));
                await Frames(1);
            }
            await AimAt(new(0, Ground + 11, 0)); await Capture("06c-airborne-target");
            Require(Camera.Rotation.X <= .0001f && Camera.Rotation.X >= -MathF.PI * 17 / 36 - .0001f &&
                _arenas[1].AimOverlay.Marker.HasValue, "Elevated rivals retain the square without overriding normal camera pitch limits");
            _heldTargets = null;
            Position(new(0, Ground, 35), new(-8, Ground, 5), new(-3, Ground, 5));
            Camera.ResetFollow(); await Frames(5); await AimAt(new(0, Ground + 1, 5));
            float crossingYaw = Camera.Rotation.Y;
            float crossingDrift = 0;
            int crossingFrames = 0, misplacedBrackets = 0;
            for (int i = 0; i < 120; i++)
            {
                float x = MathF.Sin(i * .10f) * 12;
                MoveTargets(new(x, Ground, 5), new(x + 4, Ground, 5));
                await Frames(1);
                crossingDrift = Math.Max(crossingDrift, Math.Abs(Mathf.AngleDifference(crossingYaw, Camera.Rotation.Y)));
                if (_arenas[1].AimOverlay.Bounds is { } crossingBounds)
                {
                    crossingFrames++;
                    if (!crossingBounds.HasPoint(ViewCenter)) { misplacedBrackets++; }
                }
                if (i % 30 == 0) { await Capture("06d-crossing-" + i); }
            }
            Require(crossingDrift < .01f, "Repeated fast crossings never steer the camera while RMB holds the view");
            Require(crossingFrames > 0 && misplacedBrackets == 0,
                $"Crossing-car brackets surround the visible center-ray hit under interpolation ({crossingFrames} bracket frames, {misplacedBrackets} misses)");
            // Real native driving; allow five seconds for the heavier chassis to accelerate.
            // Target trajectories above are explicitly fixture-authored.
            Camera.ResetFollow();
            N.Vector3 drivingStart = host.World.GetVehicle(Shooter).ObservedPhysics.Position;
            float peakDrivingSpeed = 0;
            bool drivingCaptured = false;
            for (int i = 0; i < 300; i++)
            {
                await Frames(1, throttle: ushort.MaxValue, steering: 3500);
                float speed = host.World.GetVehicle(Shooter).Speed;
                peakDrivingSpeed = Math.Max(peakDrivingSpeed, speed);
                if (!drivingCaptured && speed > 10) { await Capture("07-driving"); drivingCaptured = true; }
            }
            N.Vector3 drivingTravel = host.World.GetVehicle(Shooter).ObservedPhysics.Position - drivingStart;
            float drivingDistance = new Vector2(drivingTravel.X, drivingTravel.Z).Length();
            // A later native obstacle can stop the car; measure the exercised motion rather than its final speed.
            Require(peakDrivingSpeed > 10 && drivingDistance > 10, $"Vehicle drives while aiming (peak {peakDrivingSpeed:0.00} m/s, travel {drivingDistance:0.00} m)");
            _input.Adapter.GameplaySuppressed = true; await Frames(3);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(!_arenas[1].AimOverlay.Marker.HasValue, "Menu suppression clears marker immediately");
            _input.Adapter.GameplaySuppressed = false;
            _arenas[1].Driver.RequestItemSwitch(); await Frames(30);
            Require(_arenas[1].Driver.LocalItem?.Active.Item == HeldItem.Missile, "Item switch selects shared Missile foundation");
            await Frames(210); await Capture("08-missile-selected");
            host.Items.RemovePlayer(Shooter); await Frames(30);
            Position(new(0, Ground, 35), new(80, Ground, 0), new(86, Ground, 0), true);
            Require(host.Items.Grant(host.World, Shooter, HeldItem.Nitro), "Native NOS granted");
            Require(host.Items.Grant(host.World, Shooter, HeldItem.MachineGun), "Native high-speed weapon granted");
            await Frames(70);
            Require(_arenas[1].Driver.RequestItemUse(), "NOS request accepted");
            await Frames(150, throttle: ushort.MaxValue, held: InputButtons.UseItem);
            Require(host.World.GetVehicle(Shooter).Movement.Nitro.Active, "Real sustained NOS accelerates the native vehicle");
            _arenas[1].Driver.RequestItemSwitch();
            await Frames(40, throttle: ushort.MaxValue);
            await Capture("09-high-speed-after-nos");
            Require(host.World.GetVehicle(Shooter).Speed > 20 && _arenas[1].AimOverlay.Marker.HasValue, "Direct-fire aim resumes at high speed after selecting away from NOS");
            host.Items.RemovePlayer(Shooter); await Frames(25);
            Require(!_arenas[1].AimOverlay.Marker.HasValue && !host.Items.Aims.Any(aim => aim.Vehicle == Shooter), "Removal retires authoritative aim and local marker");
            host.Items.Grant(host.World, Shooter, HeldItem.MachineGun); await Frames(120);
            Position(new(0, Ground, 35), new(0, Ground + 5, 0), new(5, Ground, 0), true);
            await Frames(25);
            Require(!host.Items.Aims.Any(aim => aim.Vehicle == Shooter), "New life retires previous aiming capability");
            Position(new(0, Ground, 35), new(0, Ground, 45), new(80, Ground, 0));
            host.Items.Grant(host.World, Shooter, HeldItem.MachineGun);
            await Until(() => _arenas[1].AimOverlay.Marker.HasValue, "Fresh weapon restores aiming before lethal damage");
            Require(host.TryConfigure(0, new Dictionary<string, double> { ["items.maximum_damage"] = 10000, ["respawn.delay_ticks"] = 120 }, out _), "Lethal damage and timed respawn fixture tuning accepted");
            host.Items.RemovePlayer(1);
            Require(host.Items.Grant(host.World, 1, HeldItem.Missile), "Other vehicle receives a real missile");
            await Frames(20);
            ulong lifeBeforeDeath = host.World.GetVehicle(Shooter).LifeId;
            Require(_arenas[0].Driver.RequestItemUse(), "Real host missile use accepted");
            await Until(() => !host.World.GetVehicle(Shooter).CanInteract, "Actual missile explosion kills the aiming vehicle");
            Require(host.World.GetVehicle(Shooter).Damage.LastDamage?.Attribution.Source == "missile", "Death carries authoritative missile attribution");
            Require(!host.Items.Aims.Any(aim => aim.Vehicle == Shooter), "Actual death immediately clears authoritative aim");
            // Impaired delivery and render presentation need not complete in a fixed
            // twelve physics frames. Observe confirmed client death before respawn.
            await Until(() => _arenas[1].LocalState is { CanInteract: false } && !_arenas[1].AimOverlay.Marker.HasValue,
                "Confirmed client death clears the camera cursor before respawn", 90);
            await Until(() => host.World.GetVehicle(Shooter).LifeId > lifeBeforeDeath && host.World.GetVehicle(Shooter).CanInteract, "Ordinary timed respawn creates a fresh living vehicle", 240);
            Require(!_arenas[1].AimOverlay.Marker.HasValue, "Respawn cannot retain a dead weapon's marker");
            System.IO.File.WriteAllLines(System.IO.Path.Combine(_output, "evidence.txt"), _evidence);
            GD.Print($"Weapon aiming integration passed: {_evidence.Count} checks; three native UDP peers.");
            foreach (var arena in _arenas) { arena.QueueFree(); }
            // Let queued arena teardown and audio-server retirement finish before quitting.
            for (int i = 0; i < 4; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError(exception.ToString()); GetTree().Quit(1); }
    }

    private Vector2 ViewCenter => _views[1].GetVisibleRect().GetCenter();
    private void RequireCentered(string evidence) => Require(_arenas[1].AimOverlay.Marker is Vector2 marker && marker.DistanceTo(ViewCenter) < .01f, evidence);

    private async Task AimAt(Vector3 point)
    {
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
        for (int i = 0; i < 5; i++)
        {
            Vector3 direction = (point - Camera.GlobalPosition).Normalized();
            float yaw = MathF.Atan2(-direction.X, -direction.Z), pitch = MathF.Asin(direction.Y);
            Send(new InputEventMouseMotion { ScreenRelative = new(-Mathf.AngleDifference(Camera.Rotation.Y, yaw) / .003f, -(pitch - Camera.Rotation.X) / .003f) });
            await Frames(15);
        }
    }
    private static void Send(InputEvent input)
    {
        using (input) { Godot.Input.ParseInputEvent(input); Godot.Input.FlushBufferedEvents(); }
    }
    private async Task Frames(int count, ushort throttle = 0, short steering = 0, InputButtons held = 0)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            if (_heldTargets is { } targets) { MoveTargets(targets.First, targets.Second); }
            foreach (var arena in _arenas)
            {
                arena.Advance(arena == _arenas[1] ? new(0, steering, throttle, 0, held, 0, 0) : default);
                if (arena.Driver.Failure.Length > 0) { throw new InvalidOperationException(arena.Driver.Failure); }
            }
        }
    }
    private async Task Until(Func<bool> condition, string evidence, int maximum = 600)
    {
        for (int i = 0; i < maximum && !condition(); i++) { await Frames(1); }
        Require(condition(), evidence);
    }
    private void Position(N.Vector3 shooter, N.Vector3 first, N.Vector3 second, bool newLife = false)
    {
        var world = _arenas[0].Driver.Host!.World;
        var snapshot = world.State;
        var match = snapshot.Match!;
        // Fixture life replacement must retire life-scoped pending stunt continuation too.
        if (newLife)
        {
            match = new(match.Tick, match.Revision, match.KillTarget, match.Phase, match.CountdownAtTick, match.Winner,
                match.Players.Select(player => player with { Stunts = null }), match.Changes, match.Awards, match.Mode,
                match.ActiveStartedAtTick, match.DurationTicks, match.RecoveryElapsedTicks);
        }
        world.Restore(new(snapshot.Tick, snapshot.LastInput, snapshot.Vehicles.Select(state =>
        {
            var pose = new VehiclePhysicsState(state.VehicleId == Shooter ? shooter : state.VehicleId == 1 ? first : second, N.Quaternion.Identity, N.Vector3.Zero, N.Vector3.Zero);
            _arenas[0].Bodies[state.VehicleId].Apply(pose);
            return new VehicleSnapshot(state.VehicleId, state.LifeId + (newLife ? 1ul : 0), new(snapshot.Tick, pose, true, false, 0, 0), new VehicleDamageState(1000, 1000, null, null), pose);
        }), match));
    }
    private void MoveTargets(N.Vector3 first, N.Vector3 second)
    {
        var world = _arenas[0].Driver.Host!.World;
        var snapshot = world.State;
        world.Restore(new(snapshot.Tick, snapshot.LastInput, snapshot.Vehicles.Select(state =>
        {
            if (state.VehicleId == Shooter) { return state; }
            var pose = new VehiclePhysicsState(state.VehicleId == 1 ? first : second, N.Quaternion.Identity, N.Vector3.Zero, N.Vector3.Zero);
            _arenas[0].Bodies[state.VehicleId].Apply(pose);
            return new VehicleSnapshot(state.VehicleId, state.LifeId, new(snapshot.Tick, pose, true, false, 0, 0), state.Damage, pose);
        }), snapshot.Match));
    }
    private async Task Capture(string name)
    {
        if (DisplayServer.GetName() == "headless") { return; }
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = _views[1].GetTexture().GetImage();
        image.SavePng(System.IO.Path.Combine(_output, name + ".png"));
    }
    private void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
        _evidence.Add(message); GD.Print(message);
    }
    public override void _ExitTree() { foreach (var wire in _wires) { wire.Dispose(); } }
}
