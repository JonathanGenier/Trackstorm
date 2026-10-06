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
            await MotionFrames(70, "side-turn"); await Capture("03-side-elevated");
            solution = host.Items.Aims.Single(aim => aim.Vehicle == Shooter);
            Require(Math.Abs(solution.Yaw) > .8f, "Side mouse aim reaches authority with the original camera orbit limits");
            Require(_arenas.All(arena => Math.Abs(arena.Bodies[Shooter].Rack.GetParent<Node3D>().GetNode<Node3D>("WeaponRack").Rotation.Y - rackYaw) < .0001f), "Deployed rack remains fixed on all peers");
            Require(_arenas[2].Driver.AcceptedAims.Any(aim => aim.Vehicle == Shooter && Math.Abs(aim.Yaw) > .8f), "Observer receives accepted articulated aim");
            RequireObserverMount("side");
            Send(new InputEventMouseMotion { ScreenRelative = new(520, 0) });
            await MotionFrames(70, "rear-turn"); await Capture("04-rear-elevated");
            Require(Math.Abs(host.Items.Aims.Single(aim => aim.Vehicle == Shooter).Yaw) > 2, "Rear aim accepted");
            RequireObserverMount("rear");
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
            float heldYaw = Camera.Rotation.Y; await MotionFrames(30, "stick-recenter");
            RequireCentered("Ordinary camera recentering retains a centered cursor");
            Require(Math.Abs(Mathf.AngleDifference(chaseYaw, Camera.Rotation.Y)) < Math.Abs(Mathf.AngleDifference(chaseYaw, heldYaw)) * .2f,
                "Neutral stick restores the original chase view with a weapon selected");
            Position(new(0, Ground, 35), new(0, Ground, 25), new(7, Ground, 25));
            Camera.ResetFollow(); await Frames(20);
            await AimAtCurrent(() => _arenas[1].Bodies[1].VisualPosition + Vector3.Up * .9f);
            await Frames(30); await Capture("06-close-brackets");
            LogTargetGeometry("close", 1);
            Require(_arenas[1].AimOverlay.Bounds is { } closeBounds && closeBounds.HasPoint(ViewCenter),
                "Directly intersected close car receives brackets instead of a distance-based square fallback");
            Position(new(0, Ground, 35), new(-3.7f, Ground, 35), new(7, Ground, 25));
            Camera.ResetFollow(); await Frames(20);
            await AimAtCurrent(() => _arenas[1].Bodies[1].VisualPosition + Vector3.Up * .9f);
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
            // Let the impaired peer settle after staging before comparing lens height.
            await Frames(60);
            Camera.ResetFollow(); await Frames(5);
            float elevatedChaseHeight = Camera.GlobalPosition.Y - _arenas[1].Bodies[Shooter].VisualPosition.Y;
            await AimAt(_arenas[1].Bodies[1].VisualPosition + Vector3.Up);
            for (int i = 0; i < 60; i++)
            {
                MoveTargets(new(0, Ground + 10, 0), new(5, Ground, 0));
                await Frames(1);
            }
            await AimAt(new(0, Ground + 11, 0)); await Capture("06c-airborne-target");
            Require(Camera.Rotation.X > .1f && Camera.Rotation.X <= MathF.PI * 17 / 36 + .0001f,
                "Elevated rivals can be aimed at above the horizon");
            Require(Camera.GlobalPosition.Y - _arenas[1].Bodies[Shooter].VisualPosition.Y >= elevatedChaseHeight - .15f,
                "Upward aiming does not lower the grounded chase camera relative to the car");
            RequireCentered("Upward aiming retains the centered shared aiming cue");
            Require(_arenas[1].AimOverlay.Bounds is { } elevatedBounds && elevatedBounds.HasPoint(ViewCenter), "The upward camera ray actually intersects the elevated rival");
            Require(host.Items.Aims.Any(aim => aim.Vehicle == Shooter && aim.Pitch > .1f) &&
                _arenas[2].Driver.AcceptedAims.Any(aim => aim.Vehicle == Shooter && aim.Pitch > .1f), "Upward desired aim reaches authority and observer");
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
            await CheckAssistanceAndOutage();
            await CheckCameraPreferences();
            Position(new(0, Ground, 35), new(-8, Ground, 5), new(-3, Ground, 5));
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
            Position(new(0, Ground + 25, 35), new(0, Ground, 0), new(6, Ground, 0));
            Camera.ResetFollow();
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
            await Until(() => Camera.AerialMotion.Pullback > 2 && _arenas[1].LocalState is { Movement.Grounded: false }, "Airborne shooter opens wider camera framing");
            Send(new InputEventMouseMotion { ScreenRelative = new(60, -90) });
            await Frames(8);
            Require(Camera.Rotation.X > .1f, "Airborne shooter can aim above the horizon");
            RequireCentered("Airborne camera input retains the centered shared aiming cursor");
            float airborneYaw = Camera.Rotation.Y;
            await Frames(20);
            Require(Math.Abs(Mathf.AngleDifference(airborneYaw, Camera.Rotation.Y)) < .01f, "Aerial pullback cannot drift held camera aim");
            Require(host.Items.Aims.Any(aim => aim.Vehicle == Shooter) && _arenas[2].Driver.AcceptedAims.Any(aim => aim.Vehicle == Shooter), "Airborne desired aim reaches authority and the observer");
            await Capture("07a-airborne-shooter");
            await Frames(180);
            Require(Camera.AerialMotion.Pullback < .1f, "Aiming survives landing and smooth chase recovery");
            RequireCentered("Landing retains camera-relative aiming presentation");
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
    private async Task CheckCameraPreferences()
    {
        var settings = new Settings.PlayerSettingsController();
        settings.Initialize(_input.Adapter, System.IO.Path.Combine(_output, "camera-settings.json")); AddChild(settings);
        _arenas[1].CameraSettings = settings;
        var host = _arenas[0].Driver.Host!;
        var configuration = host.Configuration.Configuration;
        float observerFov = _arenas[2].GetNode<VehicleChaseCamera>("ChaseCamera").Fov;
        var cases = new (string Name, double[] Values)[]
        {
            ("CameraDistance", [1.15, 1.3, 1.5]), ("CameraInertia", [0, .5, 1]),
            ("CameraAerialPullback", [0, 1, 1.5]), ("CameraShakeIntensity", [0, .5, 1]),
            ("HorizontalLookSensitivity", [.25, 1, 3]), ("VerticalLookSensitivity", [.25, 1, 3]),
            ("StickAimSensitivity", [.25, 1, 3]), ("CameraRecenterSpeed", [.25, 1, 3]),
            ("CameraFov", [50, 65, 90]), ("CameraHeight", [.5, 1.25, 3]),
            ("InvertY", [0, 1]), ("MouseAimSensitivity", [.25, 1, 3]), ("StickAimCurve", [1, 2, 3]),
        };
        foreach (var option in cases)
        foreach (double value in option.Values)
        foreach (bool airborne in new[] { false, true })
        foreach (bool upward in new[] { false, true })
        {
            // The production codec creates one changed preference; no synchronized configuration is edited.
            string key = char.ToLowerInvariant(option.Name[0]) + option.Name[1..];
            string jsonValue = option.Name == "InvertY" ? (value == 1 ? "true" : "false") : value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            settings.UpdateSettings(Core.Settings.PlayerSettingsJson.Deserialize($"{{\"{key}\":{jsonValue}}}"));
            Position(new(0, Ground + (airborne ? 35 : 0), 35), new(80, Ground, 0), new(-80, Ground, 0));
            Camera.ResetFollow(); await Frames(6);
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
            float vertical = (float)settings.Current.VerticalLookSensitivity * (float)settings.Current.MouseAimSensitivity * (settings.Current.InvertY ? -1 : 1);
            Send(new InputEventMouseMotion { ScreenRelative = new(0, (upward ? -70 : 70) / vertical) });
            // Real controller input also contributes; hold RMB to retain the resulting view.
            Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = .3f });
            await Frames(4);
            Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = 0 });
            if (option.Name == "CameraShakeIntensity") Camera.Motion.Impulse(.6f);
            await Frames(36);
            // Sample after arena render-follow and PresentAiming, not at the pre-process signal.
            await ToSignal(GetTree().CreateTimer(0), SceneTreeTimer.SignalName.Timeout);
            Vector3 lens = Camera.ProjectRayOrigin(ViewCenter);
            Vector3 end = lens + Camera.ProjectRayNormal(ViewCenter) * 300;
            using var query = PhysicsRayQueryParameters3D.Create(lens, end, 1); query.HitFromInside = true;
            using var hit = Camera.GetWorld3D().DirectSpaceState.IntersectRay(query);
            Vector3 target = hit.Count == 0 ? end : hit["position"].AsVector3();
            var local = _arenas[1].Driver.LocalState!;
            N.Vector3 pivot = local.ObservedPhysics.Position + N.Vector3.Transform(WeaponAim.Pivot, local.ObservedPhysics.Orientation);
            N.Vector3 expected = N.Vector3.Normalize(VehicleBody.ToCore(target) - pivot);
            string phase = $"{option.Name}={value} air={airborne} up={upward}";
            GD.Print($"CAMERA_AIM_SAMPLE {phase} expected={expected} actual={_arenas[1].Driver.DesiredAim} lens={lens} target={target} pitch={Camera.Rotation.X}");
            Require(_arenas[1].Driver.DesiredAim is { } desired && N.Vector3.Dot(expected, desired) > .9999f,
                "Final centered rendered ray and pivot-relative intent agree: " + phase);
            RequireCentered("Camera settings retain centered HUD: " + phase);
            var accepted = host.Items.Aims.Single(aim => aim.Vehicle == Shooter);
            // Solve to convergence independently, retaining existing angular/clearance limits.
            WeaponAimSolution? solved = null;
            var hostState = host.World.GetVehicle(Shooter);
            for (ulong tick = 1; tick <= 240; tick++) solved = WeaponAim.Solve(hostState, accepted.Token, expected, solved, configuration.Items.Aim, tick);
            Require(N.Vector3.Dot(solved!.Direction, accepted.Direction) > .995f,
                "Authoritative articulation follows camera intent within network/pose tolerance: " + phase);
            Require(!WeaponAim.IntersectsBody(WeaponAim.Pivot, WeaponAim.Direction(accepted.Yaw, accepted.Pitch)), "Shared body clearance remains safe: " + phase);
            RequireObserverMount(phase);
            Require(host.Configuration.Configuration == configuration && _arenas[2].GetNode<VehicleChaseCamera>("ChaseCamera").Fov == observerFov,
                "Local preference leaves host tuning and peer camera unchanged: " + phase);
            if (option.Name is "CameraFov" or "CameraHeight") await Capture("settings-" + option.Name + "-" + value + "-" + airborne + "-" + upward);
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
        }
        _arenas[1].CameraSettings = null; settings.QueueFree(); Camera.ResetFollow(); await Frames(6);
        GD.Print("Camera preference aiming matrix passed: 152 low/default/high ground/air upward/downward cases through three real UDP peers.");
    }
    private void RequireCentered(string evidence) => Require(_arenas[1].AimOverlay.Marker is Vector2 marker && marker.DistanceTo(ViewCenter) < .01f, evidence);

    private void LogTargetGeometry(string phase, ulong target)
    {
        var body = _arenas[1].Bodies[target];
        Vector3 point = body.VisualPosition + Vector3.Up * .9f;
        GD.Print($"AIM_GEOMETRY {phase}: lens={Camera.GlobalPosition} rotation={Camera.Rotation} targetDisplayed={body.VisualPosition} targetNative={body.GlobalPosition} targetScreen={Camera.UnprojectPosition(point)} center={ViewCenter} bounds={_arenas[1].AimOverlay.Bounds}");
    }

    private void RequireObserverMount(string phase)
    {
        var accepted = _arenas[2].Driver.AcceptedAims.Single(aim => aim.Vehicle == Shooter);
        var rack = _arenas[2].Bodies[Shooter].Rack.GetParent<Node3D>().GetNode<Node3D>("WeaponRack");
        var yaw = rack.GetNode<Node3D>("WeaponYaw");
        var pitch = yaw.GetNode<Node3D>("WeaponPitch");
        GD.Print($"AIM_REMOTE {phase}: acceptedYaw={accepted.Yaw} renderedYaw={yaw.Rotation.Y} acceptedPitch={accepted.Pitch} renderedPitch={pitch.Rotation.X}");
        Require(Math.Abs(Mathf.AngleDifference(yaw.Rotation.Y, accepted.Yaw)) < .03f &&
            Math.Abs(Mathf.AngleDifference(pitch.Rotation.X, accepted.Pitch)) < .03f,
            $"Remote mount yaw and payload pitch settle to accepted {phase} aim on actual presentation nodes");
    }

    private async Task CheckAssistanceAndOutage()
    {
        var host = _arenas[0].Driver.Host!;
        var original = host.Configuration.Configuration.Items.Aim;
        var right = new N.Vector3(1.8f, Ground, 0);
        var far = new N.Vector3(80, Ground, 0);
        Position(new(0, Ground, 35), right, far);
        _heldTargets = (right, far);
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
        await Frames(60);
        float[] assisted = await MeasureResponses(original.MouseFriction, original.StickFriction);
        float[] unassisted = await MeasureResponses(0, 0);
        for (int i = 0; i < assisted.Length; i++)
        {
            GD.Print($"AIM_RESPONSE case={i} default={assisted[i]:F7} zero={unassisted[i]:F7} ratio={assisted[i] / unassisted[i]:F4}");
        }
        Require(assisted[0] > unassisted[0] * .90f && assisted[0] < unassisted[0] * .995f,
            "Small native mouse input toward a visible rival receives bounded default friction");
        Require(assisted[2] > unassisted[2] * .70f && assisted[2] < unassisted[2] * .99f,
            "Fine native stick input toward a visible rival receives bounded default friction");
        foreach (int i in new[] { 1, 3, 4, 5 })
        {
            Require(Math.Abs(assisted[i] - unassisted[i]) < .00001f,
                $"Moving away/full-stick/flick bypass matches zero friction (case {i})");
        }
        await SetFriction(original.MouseFriction, original.StickFriction);
        _heldTargets = (far, new(-1.8f, Ground, 0));
        await Frames(30);
        float towardSecond = await MeasureResponse(-2, 0);
        float awaySecond = await MeasureResponse(2, 0);
        Require(towardSecond < awaySecond * .995f && towardSecond > awaySecond * .90f,
            "Eligibility transfers from the first right-hand rival to the second left-hand rival without a retained lock");
        _heldTargets = (far, new(86, Ground, 0)); await Frames(30);
        float noTarget = await MeasureResponse(-2, 0);
        Require(Math.Abs(noTarget - awaySecond) < .00001f,
            "Removing both rivals from the cone releases small-input assistance");
        _heldTargets = null;
        await Until(() => _arenas[2].Driver.AcceptedAims.Any(aim => aim.Vehicle == Shooter), "Observer has accepted aim before packet blackout");
        ulong beforeTick = _arenas[2].Driver.AcceptedAims.Single(aim => aim.Vehicle == Shooter).Tick;
        _wires[0].ConfigureSimulation(new(0, 0, 100));
        await Frames(75);
        Require(_arenas[1].Driver.AcceptedAims.Count == 0 && _arenas[2].Driver.AcceptedAims.Count == 0,
            "A bounded real UDP blackout expires accepted aim on shooter and observer");
        _wires[0].ConfigureSimulation(OS.GetCmdlineUserArgs().Contains("--aim-impaired") ? new(30, 5, 2) : new());
        await Until(() => _arenas[2].Driver.AcceptedAims.Any(aim => aim.Vehicle == Shooter && aim.Tick > beforeTick),
            "Fresh accepted aim resumes after packet delivery recovers (not authenticated reconnect)");
        RequireCentered("Camera-intent cursor remains centered after transient packet recovery");
    }

    private async Task SetFriction(float mouse, float stick)
    {
        var host = _arenas[0].Driver.Host!;
        Require(host.TryConfigure(0, new Dictionary<string, double> { ["items.aim_mouse_friction"] = mouse, ["items.aim_stick_friction"] = stick }, out _),
            $"Host accepts probe friction mouse={mouse}, stick={stick}");
        await Until(() => _arenas[1].Driver.Configuration.Configuration.Items.Aim.MouseFriction == mouse &&
            _arenas[1].Driver.Configuration.Configuration.Items.Aim.StickFriction == stick, "Probe friction reaches the native client");
    }

    private async Task<float[]> MeasureResponses(float mouse, float stick)
    {
        await SetFriction(mouse, stick);
        var samples = new List<float>();
        foreach (var input in new[] { (2f, 0f), (-2f, 0f), (0f, .35f), (0f, -.35f), (0f, 1f), (40f, 0f) })
        {
            samples.Add(await MeasureResponse(input.Item1, input.Item2));
        }
        return samples.ToArray();
    }

    private async Task<float> MeasureResponse(float mouse, float stick)
    {
        Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = 0 });
        Camera.ResetFollow(); await Frames(6);
        float before = Camera.Rotation.Y;
        if (mouse != 0) { Send(new InputEventMouseMotion { ScreenRelative = new(mouse, 0) }); }
        if (stick != 0) { Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = stick }); }
        // One native production follow at a fixed delta isolates input response from render timing.
        var body = _arenas[1].Bodies[Shooter];
        Camera.Follow(body.VisualTransform, _arenas[1].LocalState!, 1f / 30, body.GetRid());
        float result = Math.Abs(Mathf.AngleDifference(before, Camera.Rotation.Y));
        Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = 0 });
        return result;
    }

    private Task AimAt(Vector3 point) => AimAtCurrent(() => point);

    private async Task AimAtCurrent(Func<Vector3> resolvePoint)
    {
        Vector3 initialTarget = _arenas[1].Bodies[1].VisualPosition;
        Vector3 point = resolvePoint();
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
        for (int i = 0; i < 5; i++)
        {
            // Near cars are still settling after fixture relocation. Re-sample their
            // displayed target point instead of aiming at a stale airborne position.
            point = resolvePoint();
            Vector3 direction = (point - Camera.GlobalPosition).Normalized();
            float yaw = MathF.Atan2(-direction.X, -direction.Z), pitch = MathF.Asin(direction.Y);
            Send(new InputEventMouseMotion { ScreenRelative = new(-Mathf.AngleDifference(Camera.Rotation.Y, yaw) / .003f, -(pitch - Camera.Rotation.X) / .003f) });
            await Frames(15);
        }
        GD.Print($"AIM_AT requested={point} firstTargetDrift={_arenas[1].Bodies[1].VisualPosition - initialTarget} requestedScreen={Camera.UnprojectPosition(point)}");
    }
    private static void Send(InputEvent input)
    {
        using (input) { Godot.Input.ParseInputEvent(input); Godot.Input.FlushBufferedEvents(); }
    }
    private async Task MotionFrames(int count, string phase)
    {
        if (!OS.GetCmdlineUserArgs().Contains("--aim-motion") || DisplayServer.GetName() == "headless")
        {
            await Frames(count);
            return;
        }
        for (int i = 0; i < count; i++)
        {
            await Frames(1);
            if (i % 6 == 0) { await Capture($"motion-{phase}-{i:D3}"); }
        }
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
