using Godot;
using Trackstorm.Client.Items;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Rendered production barrels and fixed-pool behavior with explicitly scripted confirmed outcomes.</summary>
public sealed partial class MachineGunVfxChecks : Node3D
{
    private const string Output = "res://.godot/ts235-vfx/";
    private readonly List<Node3D> _cars = [];
    private readonly List<WeaponAimMount> _mounts = [];
    private readonly List<string> _checks = [];
    private readonly MachineGunFirePresentation _fire = new();
    private Camera3D _camera = null!;
    private ulong _tick;
    private readonly int[] _rounds = new int[8];
    private readonly Vector3[] _lastMuzzles = new Vector3[2];

    public override void _Ready() => CallDeferred(MethodName.Run);

    private async void Run()
    {
        try
        {
            Engine.MaxFps = 60;
            System.IO.Directory.CreateDirectory(ProjectSettings.GlobalizePath(Output));
            AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new(.3f, .37f, .44f), AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new(.8f, .86f, 1), AmbientLightEnergy = .65f, TonemapMode = Godot.Environment.ToneMapper.Filmic } });
            AddChild(new DirectionalLight3D { RotationDegrees = new(-48, -30, 0), LightEnergy = 2, ShadowEnabled = true });
            AddChild(new MeshInstance3D { Position = new(0, -.75f, -100), Mesh = new BoxMesh { Size = new(200, .15f, 350) },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new(.24f, .26f, .28f), Roughness = .95f } });
            for (int i = 0; i < 8; i++)
            {
                var car = VehicleVisual.Create(new StandardMaterial3D { AlbedoColor = new(.5f, .05f, .04f) });
                car.Position = i == 0 ? Vector3.Zero : new((i % 4 - 1.5f) * 5, 0, 10 + i / 4 * 7);
                AddChild(car); car.GetChildren().OfType<CarDeployment>().Single().SetTimelineProgress(1);
                var mount = new WeaponAimMount(HeldItem.MachineGun) { Position = new(0, WeaponAim.Pivot.Y - 1.34f, 0) };
                car.GetNode<Node3D>("WeaponRack").AddChild(mount);
                _cars.Add(car); _mounts.Add(mount); car.Visible = i == 0;
            }
            _camera = new Camera3D { Current = true, Fov = 70, Position = new(0, 5, 11) };
            AddChild(_camera); _camera.LookAt(new(0, 2, -18));
            AddChild(_fire); _fire.SetProcess(false);
            _fire.Muzzle = (shot, side) =>
            {
                Vector3 muzzle = _mounts[(int)shot.Owner - 1].TwinFang!.Muzzle(side);
                if (shot.Owner == 1) { _lastMuzzles[side] = muzzle; }
                return muzzle;
            };
            await Frame();
            await CheckOriginsAndBounds();
            bool motion = OS.GetCmdlineUserArgs().Contains("--vfx-motion");
            foreach (int fps in new[] { 60, 30 })
            {
                Engine.MaxFps = fps;
                _fire.Clear(); Array.Clear(_rounds);
                _camera.Position = new(0, 5, 11); _camera.LookAt(new(0, 2, -25));
                int before = _fire.EmittedCount;
                for (int frame = 0; frame < fps * 3; frame++)
                {
                    // 80 scripted outcomes/s distributed over display frames. Native checks separately verify Core spread.
                    int count = (frame + 1) * 80 / fps - frame * 80 / fps;
                    Publish(1, count, 225, frame / (float)fps);
                    _fire.Advance(1f / fps);
                    await Frame();
                    if (frame == fps) { await Capture($"chase-{fps}fps"); }
                    if (motion && frame >= fps && frame < fps * 2) { await Capture($"motion-{fps}-{frame - fps:D3}"); }
                }
                Check(_fire.EmittedCount - before == 240, $"{fps} FPS preserves all 240 confirmed rounds over three seconds");
                _fire.Advance(1); _fire.Advance(0);
                Check(_fire.ActiveCount == 0, $"{fps} FPS release drains flight within the bounded lifetime");
            }
            Engine.MaxFps = 60;
            foreach (var car in _cars) { car.Visible = true; }
            _camera.Position = new(18, 12, 30); _camera.LookAt(new(0, 2, -20));
            var times = new List<double>();
            int dropped = _fire.DroppedCount;
            int emitted = _fire.EmittedCount;
            for (int frame = 0; frame < 600; frame++)
            {
                int count = (frame + 1) * 80 / 60 - frame * 80 / 60;
                Publish(8, count, 225, frame / 60f);
                var start = System.Diagnostics.Stopwatch.GetTimestamp();
                _fire.Advance(1f / 60);
                times.Add(System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                await Frame();
                if (frame == 240) { await Capture("eight-shooters"); }
            }
            Check(_fire.EmittedCount - emitted == 6400 && _fire.DroppedCount == dropped,
                "Eight scripted shooters at 80 rounds/s for ten seconds emit 6,400 rounds without pool drops");
            Check(_fire.GetChildCount() == 0 && _fire.Multimesh.InstanceCount == MachineGunFirePresentation.Capacity,
                "Sustained fire creates no per-round scene nodes and retains exactly 512 allocated instances");
            for (int frame = 0; frame < 120; frame++)
            { Publish(8, 2, 300, frame / 60f); _fire.Advance(1f / 60); await Frame(); }
            Check(_fire.DroppedCount == dropped, "Eight shooters at maximum configured 120 rounds/s and 300 m remain within capacity");
            _fire.Advance(1); _fire.Advance(0);
            Check(_fire.ActiveCount == 0 && _fire.Multimesh.VisibleInstanceCount == 0, "All flights retire after release");
            foreach (var mount in _mounts) { mount.TwinFang!.Stop(); }
            await Capture("eight-released");
            times.Sort();
            GD.Print($"VFX CPU update only: median={times[times.Count / 2]:F3}ms p95={times[(int)(times.Count * .95)]:F3}ms; peak={_fire.PeakCount}; dropped={_fire.DroppedCount}. Not GPU profiling.");
            System.IO.File.WriteAllLines(ProjectSettings.GlobalizePath(Output + "checks.txt"), _checks);
            GD.Print($"Machine Gun VFX checks passed: {_checks.Count}; scripted rendered evidence, separate native harness required.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private async Task CheckOriginsAndBounds()
    {
        foreach (var pose in new[] { (0f, 0f), (MathF.PI / 2, .3f), (MathF.PI, .4f), (0f, MathF.PI / 2) })
        {
            _fire.Clear();
            var mount = _mounts[0];
            var aim = new WeaponAimSolution(1, 1, 1, ++_tick, pose.Item1, pose.Item2, WeaponAim.Pivot,
                WeaponAim.Direction(pose.Item1, pose.Item2), true) { Ready = true };
            for (int i = 0; i < 30; i++) { mount.Present(aim, .1f); }
            Vector3 direction = VehicleBody.ToGodot(aim.Direction);
            Vector3 center = (mount.TwinFang!.Muzzle(0) + mount.TwinFang.Muzzle(1)) / 2;
            var shots = new[] { Shot(0, 0, center, center + direction * 30), Shot(0, 1, center, center + direction * 30) };
            _fire.Observe(Publication([new ItemSlot(1, 1, 1, HeldItem.MachineGun) { Ammo = new(798, 800) }], shots));
            _fire.Advance(0);
            Check(_fire.ActiveCount == 2, $"Two distinct confirmed rounds render at yaw/pitch {pose}");
            for (int side = 0; side < 2; side++)
            {
                Transform3D visual = _fire.Multimesh.GetInstanceTransform(side);
                Vector3 from = visual.Origin - visual.Basis.Y * .5f;
                Check(from.DistanceTo(mount.TwinFang.Muzzle(side)) < .0001f,
                    $"Streak {side} starts at its actual articulated barrel at yaw/pitch {pose}");
            }
            Check(_lastMuzzles[0].DistanceTo(_lastMuzzles[1]) > .57f, "Paired origins never collapse to the midpoint");
            _fire.Advance(.01f); _fire.Advance(0);
            Check(_fire.Multimesh.GetInstanceTransform(0).Origin.DistanceTo(_lastMuzzles[0]) > 4.2f, "Visible streak advances away from the barrel");
        }
        _fire.Clear();
        var forward = new WeaponAimSolution(1, 1, 1, _tick, 0, 0, WeaponAim.Pivot, -N.Vector3.UnitZ, true) { Ready = true };
        for (int i = 0; i < 30; i++) { _mounts[0].Present(forward, .1f); }
        Publish(1, 2, 20, 0); _fire.Advance(0);
        _camera.Position = new(2.7f, 3.3f, 1.5f); _camera.LookAt(new(0, 2.4f, -4));
        await Capture("paired-muzzles");
        var slot = new ItemSlot(1, 1, 1, HeldItem.MachineGun) { Ammo = new(790, 800) };
        Vector3 origin = _mounts[0].TwinFang!.Muzzle(0);
        _fire.Clear();
        var blocked = Publication([slot], [Shot(0, 0, origin, origin)]);
        _fire.Observe(blocked);
        Check(_fire.ActiveCount == 0, "Cover before the muzzle emits no backwards or sideways streak");
        var close = Publication([slot], [Shot(0, 0, origin, origin + Vector3.Forward * .2f)]);
        _fire.Observe(close); _fire.Advance(0);
        Transform3D shortTrace = _fire.Multimesh.GetInstanceTransform(0);
        Check((shortTrace.Origin + shortTrace.Basis.Y * .5f).DistanceTo(origin + Vector3.Forward * .2f) < .0001f,
            "Close trace clamps exactly to the accepted endpoint");
        int before = _fire.EmittedCount; _fire.Observe(close);
        Check(_fire.EmittedCount == before, "Repeated publication never duplicates fire");
        _fire.Clear();
        for (int i = 0; i < 100; i++) { Publish(1, 2, 225, 0); }
        Check(_fire.ActiveCount == MachineGunFirePresentation.PerShooterCapacity && _fire.DroppedCount > 0,
            "Delivery catch-up is capped per shooter without replay queues");
        _fire.Clear();
        Check(_fire.ActiveCount == 0, "Recovery clears all old flights");
        var muzzle = _fire.Muzzle;
        _fire.Muzzle = (_, _) => origin + Vector3.Back * 1000;
        _fire.Observe(Publication([slot], [Shot(0, 0, origin, origin + Vector3.Forward * 20)]));
        _fire.Advance(.56f); _fire.Advance(0);
        Check(_fire.ActiveCount == 0, "A displaced presentation pose cannot extend flight beyond 550 ms");
        _fire.Muzzle = muzzle;
    }

    private void Publish(int shooters, int count, float range, float time)
    {
        var slots = new List<ItemSlot>(); var shots = new List<ItemEvent>();
        for (int car = 0; car < shooters; car++)
        {
            float yaw = MathF.Sin(time * .6f) * .13f;
            var direction = WeaponAim.Direction(yaw, 0);
            var aim = new WeaponAimSolution((ulong)car + 1, 1, (ulong)car + 1, _tick + 1, yaw, 0, WeaponAim.Pivot, direction, true) { Ready = true };
            _mounts[car].Present(aim, 1f / 60);
            Vector3 center = ( _mounts[car].TwinFang!.Muzzle(0) + _mounts[car].TwinFang!.Muzzle(1)) * .5f;
            var volley = new List<ItemEvent>();
            for (int i = 0; i < count; i++)
            {
                int round = _rounds[car]++;
                var across = N.Vector3.Normalize(N.Vector3.Cross(direction, N.Vector3.UnitY));
                float radius = .007f * MathF.Sqrt((round % 17 + .5f) / 17);
                var ray = N.Vector3.Normalize(direction + across * (MathF.Sin(round * 2.4f) * radius) + N.Vector3.UnitY * (MathF.Cos(round * 2.4f) * radius));
                volley.Add(Shot(car, round, center, center + VehicleBody.ToGodot(ray) * range));
            }
            var slot = new ItemSlot((ulong)car + 1, 1, (ulong)car + 1, HeldItem.MachineGun)
                { Ammo = new(10000 - _rounds[car], 10000), EngagedToken = (ulong)car + 1 };
            slots.Add(slot); shots.AddRange(volley);
            _mounts[car].TwinFang!.Observe(slot, volley, _tick + 1);
        }
        _fire.Observe(Publication(slots, shots));
    }

    private ItemPublication Publication(IEnumerable<ItemSlot> slots, IEnumerable<ItemEvent> shots)
    {
        _tick++;
        var vehicles = _cars.Select((car, i) =>
        {
            var physics = new VehiclePhysicsState(VehicleBody.ToCore(car.Position), N.Quaternion.Identity, N.Vector3.Zero, N.Vector3.Zero);
            return new ReplicatedVehicle(new VehicleSnapshot((ulong)i + 1, 1, new VehicleState(_tick, physics, true, false, 0, 0), new(1000, 1000, null, null), physics), 0);
        });
        return new(_tick, new WorldSnapshot(235, _tick, vehicles), slots, [], shots);
    }

    private static ItemEvent Shot(int car, int round, Vector3 start, Vector3 end) =>
        new((ulong)car + 1, (ulong)car + 1, HeldItem.MachineGun, VehicleBody.ToCore(end), false)
            { Origin = VehicleBody.ToCore(start), Tracer = round % 2 == 0 };

    private void Check(bool condition, string message)
    { if (!condition) { throw new InvalidOperationException(message); } _checks.Add(message); }
    private async Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    private async Task Capture(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using Image image = GetViewport().GetTexture().GetImage();
        image.SavePng(ProjectSettings.GlobalizePath(Output + name + ".png"));
    }
}
