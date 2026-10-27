using Godot;
using Trackstorm.Client.Items;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Verification;

/// <summary>Rendered native contacts, temporal bounds and eight-shooter cosmetic load; not damage simulation.</summary>
public sealed partial class MachineGunImpactChecks : Node3D
{
    private readonly MachineGunImpactPresentation _impacts = new();
    private readonly List<string> _checks = [];
    private ulong _revision;
    private Camera3D _camera = null!;
    private Label _label = null!;
    private string _output = string.Empty;

    public override void _Ready() => CallDeferred(MethodName.Run);

    public async void Run()
    {
        try
        {
            _output = ProjectSettings.GlobalizePath("res://.godot/machine-gun-impact-checks/");
            System.IO.Directory.CreateDirectory(_output);
            Engine.MaxFps = 60;
            AddChild(new WorldEnvironment { Environment = new Godot.Environment
            { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new(.25f, .31f, .38f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White,
                AmbientLightEnergy = .65f, TonemapMode = Godot.Environment.ToneMapper.Filmic } });
            AddChild(new DirectionalLight3D { RotationDegrees = new(-48, -30, 0), LightEnergy = 1.5f });
            AddChild(new MeshInstance3D { Position = new(0, -.1f, -20), Mesh = new BoxMesh { Size = new(100, .2f, 100) },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new(.22f, .23f, .24f) } });
            _camera = new Camera3D { Current = true, Fov = 65, Position = new(0, 3.06f, 9.205f), RotationDegrees = new(-3.5f, 0, 0) };
            AddChild(_camera);
            var car = VehicleVisual.Create(new StandardMaterial3D { AlbedoColor = new(.35f, .05f, .035f) });
            AddChild(car);
            car.GetChildren().OfType<CarDeployment>().Single().SetTimelineProgress(1);
            var rack = new WeaponAimMount(HeldItem.MachineGun) { Position = new(0, WeaponAim.Pivot.Y - 1.34f, 0) };
            car.GetNode<Node3D>("WeaponRack").AddChild(rack);
            AddChild(_impacts); _impacts.SetProcess(false);
            var overlay = new CanvasLayer(); AddChild(overlay);
            _label = new Label { Position = new(24, 24) }; _label.AddThemeFontSizeOverride("font_size", 22); overlay.AddChild(_label);
            var materials = new (string Name, MachineGunImpactSurface.Kind Kind)[]
            {
                ("Metal", MachineGunImpactSurface.Kind.Metal), ("Concrete", MachineGunImpactSurface.Kind.Stone),
                ("Asphalt", MachineGunImpactSurface.Kind.Stone), ("Rock", MachineGunImpactSurface.Kind.Stone),
                ("Dirt", MachineGunImpactSurface.Kind.Soil), ("Grass", MachineGunImpactSurface.Kind.Soil),
                ("Mud", MachineGunImpactSurface.Kind.Wet), ("DeepMud", MachineGunImpactSurface.Kind.Wet),
                ("Water", MachineGunImpactSurface.Kind.Water), ("Unknown", MachineGunImpactSurface.Kind.Neutral),
            };
            foreach (var material in materials)
            {
                var body = Target(new(0, 1.5f, -12), material.Name);
                await PhysicsFrames(3);
                var contact = MachineGunImpactSurface.Sample(GetWorld3D(), new(0, 1.5f, -11.75f), Vector3.Forward);
                Check(contact.Material == material.Kind && contact.Normal.Dot(Vector3.Back) > .99f, $"Native {material.Name} classification and outward wall normal");
                _label.Text = $"{material.Name} · single impact · normal chase lens · scripted accepted outcome";
                _impacts.Clear(); Publish(1, 1, new(0, 1.5f, -11.75f));
                Check(_impacts.ActiveCount == 1, "An isolated confirmed contact emits");
                for (int frame = 0; frame < 30; frame++)
                {
                    _impacts.Advance(1f / 60); await Frame();
                    if (frame is 3 or 9 or 18) { await Capture($"single-{material.Name}-{frame:D2}"); }
                }
                Check(_impacts.ActiveCount == 0 && _impacts.Multimesh.VisibleInstanceCount == 0, $"{material.Name} impact fully retires");
                body.Free();
            }
            Check(MachineGunImpactSurface.Classify(null, Vector3.Zero) == MachineGunImpactSurface.Kind.Neutral, "Unavailable identity falls back to neutral debris");
            foreach (string material in new[] { "Asphalt", "Dirt" })
            {
                var ground = Target(new(0, .05f, -8), material);
                ground.RotationDegrees = new(-90, 0, 0);
                await PhysicsFrames(3);
                var point = new Vector3(0, .3f, -8);
                _camera.Position = new(0, 6, 9.205f); _camera.LookAt(point);
                var sampled = MachineGunImpactSurface.Sample(GetWorld3D(), point, Vector3.Down);
                Check(sampled.Normal.Dot(Vector3.Up) > .99f, $"{material} floor contact uses upward normal");
                _impacts.Clear();
                _label.Text = $"{material} · horizontal track contact · downward chase look";
                for (int frame = 0; frame < 60; frame++)
                {
                    _impacts.Observe(Publication([Shot(1, frame, point) with { Origin = VehicleBody.ToCore(point + Vector3.Up * 2 + Vector3.Back * 8) }]));
                    _impacts.Advance(1f / 60); await Frame();
                    if (frame is 20 or 25) { await Capture($"floor-{material}-{frame}"); }
                }
                ground.Free();
            }
            _impacts.Clear();
            _camera.Position = new(0, 3.06f, 9.205f); _camera.RotationDegrees = new(-3.5f, 0, 0);
            var target = Target(new(0, 1.5f, -12), "Concrete");
            await PhysicsFrames(3);
            var wallPoint = new Vector3(0, 1.5f, -11.75f);
            var publication = Publication([Shot(1, 1, wallPoint)]);
            _impacts.Observe(publication); int emitted = _impacts.EmittedCount;
            _impacts.Observe(publication);
            Check(_impacts.EmittedCount == emitted, "Duplicate reliable publication does not replay an impact");
            _impacts.Observe(Publication([Shot(1, 2, wallPoint) with { Impact = false }]));
            Check(_impacts.EmittedCount == emitted, "An accepted miss creates no contact effect");
            _impacts.Observe(new ItemPublication(1, World(1), [], [], []));
            Check(_impacts.ActiveCount == 0, "Older empty recovery clears old impact presentation");
            foreach (int fps in new[] { 30, 60 })
            {
                Engine.MaxFps = fps;
                foreach (string material in new[] { "Metal", "Concrete", "Dirt" })
                {
                    target.Free(); target = Target(new(0, 1.5f, -12), material); await PhysicsFrames(3);
                    _impacts.Clear();
                    _label.Text = $"{material} · sustained 80 rounds/s · {fps} FPS · normal chase lens";
                    for (int frame = 0; frame < fps * 2; frame++)
                    {
                        int rounds = (frame + 1) * 80 / fps - frame * 80 / fps;
                        Publish(1, rounds, wallPoint + new Vector3(MathF.Sin(frame * .7f) * .12f, MathF.Cos(frame * .9f) * .1f, 0));
                        _impacts.Advance(1f / fps); await Frame();
                        if (frame >= fps && frame < fps + 12) { await Capture($"stream-{material}-{fps}-{frame - fps:D2}"); }
                    }
                    Check(_impacts.ActiveCount <= MachineGunImpactPresentation.PerShooterCapacity, $"{fps} FPS sustained {material} stays within per-shooter bounds");
                }
            }
            Engine.MaxFps = 60;
            target.Free();
            for (int i = 0; i < 8; i++) { Target(new((i - 3.5f) * 4, 1.5f, -12), i % 2 == 0 ? "Metal" : "Concrete"); }
            await PhysicsFrames(3);
            _camera.Position = new(0, 7, 15); _camera.LookAt(new(0, 1.5f, -12));
            _impacts.Clear();
            var times = new List<double>();
            int nodes = GetChildCount();
            _label.Text = "Eight shooters · 80 rounds/s each · ten seconds · alternating metal / concrete";
            for (int frame = 0; frame < 600; frame++)
            {
                var timer = System.Diagnostics.Stopwatch.GetTimestamp();
                Publish(8, (frame + 1) * 80 / 60 - frame * 80 / 60, wallPoint);
                _impacts.Advance(1f / 60);
                times.Add(System.Diagnostics.Stopwatch.GetElapsedTime(timer).TotalMilliseconds);
                await Frame();
                if (frame is >= 240 and < 252) { await Capture($"eight-shooters-{frame - 240:D2}"); }
            }
            Check(_impacts.ActiveCount <= MachineGunImpactPresentation.Capacity && GetChildCount() == nodes && _impacts.GetChildCount() == 0,
                "6,400 scripted contacts keep bounded instances and create no per-hit nodes");
            int drops = _impacts.DroppedCount;
            for (int frame = 0; frame < 120; frame++)
            {
                Publish(8, 2, wallPoint); _impacts.Advance(1f / 60); await Frame();
            }
            Check(_impacts.DroppedCount == drops, "Eight shooters at maximum configured 120 rounds/s coalesce dense contacts without pool exhaustion");
            // Dispersed catch-up bursts exercise saturation, not just coalescing.
            _impacts.Clear();
            for (int owner = 0; owner < 8; owner++)
            {
                _impacts.Observe(Publication(Enumerable.Range(0, 40)
                    .Select(round => Shot((ulong)owner + 1, round, new(round, 1, -12)))));
            }
            Check(_impacts.ActiveCount == MachineGunImpactPresentation.Capacity && _impacts.DroppedCount > 0, "Catch-up saturation is bounded and drops excess cosmetic work");
            _impacts.Advance(1); _impacts.Advance(0);
            Check(_impacts.ActiveCount == 0 && _impacts.Multimesh.VisibleInstanceCount == 0, "Release drains all eight shooters without replay");
            await Capture("released");
            times.Sort();
            _checks.Add($"CPU sample + update only: median={times[times.Count / 2]:F3}ms p95={times[(int)(times.Count * .95)]:F3}ms; peak={_impacts.PeakCount}, emitted={_impacts.EmittedCount}, coalesced={_impacts.CoalescedCount}, dropped={_impacts.DroppedCount}; not GPU profiling.");
            System.IO.File.WriteAllLines(_output + "checks.txt", _checks);
            foreach (var check in _checks) { GD.Print(check); }
            GD.Print($"Machine Gun impact checks passed: {_checks.Count}");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private StaticBody3D Target(Vector3 position, string material)
    {
        var body = new StaticBody3D { Position = position, CollisionLayer = 1 };
        if (Enum.TryParse<SurfaceIdentity>(material, out var identity)) { body.SetMeta("surface_identity", identity.ToString()); }
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(3.5f, 3, .5f) } });
        body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new(3.5f, 3, .5f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = material == "Metal" ? new(.18f, .22f, .25f) : new(.38f, .38f, .36f),
                Metallic = material == "Metal" ? .8f : 0, Roughness = .85f } });
        AddChild(body); return body;
    }

    private void Publish(int shooters, int rounds, Vector3 point)
    {
        _impacts.Observe(Publication(Enumerable.Range(0, shooters).SelectMany(owner => Enumerable.Range(0, rounds)
            .Select(round => Shot((ulong)owner + 1, (int)_revision * 3 + round,
                point + (shooters == 1 ? Vector3.Zero : new Vector3((owner - 3.5f) * 4, 0, 0)))))));
    }
    private ItemPublication Publication(IEnumerable<ItemEvent> shots) => new(++_revision, World(_revision), [], [], shots);
    private static WorldSnapshot World(ulong tick)
    {
        var physics = new VehiclePhysicsState(System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity,
            System.Numerics.Vector3.Zero, System.Numerics.Vector3.Zero);
        return new(236, tick, Enumerable.Range(1, 8).Select(id => new ReplicatedVehicle(
            new VehicleSnapshot((ulong)id, 1, new VehicleState(tick, physics, true, false, 0, 0), new(1000, 1000, null, null), physics), 0)));
    }
    private static ItemEvent Shot(ulong owner, int round, Vector3 point) => new(owner, owner, HeldItem.MachineGun, VehicleBody.ToCore(point), true)
        { Origin = VehicleBody.ToCore(point + Vector3.Back * 12), MachineGunRound = round };
    private void Check(bool condition, string message)
    { if (!condition) { throw new InvalidOperationException(message); } _checks.Add(message); }
    private async Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    private async Task PhysicsFrames(int count)
    { for (int i = 0; i < count; i++) { await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); } }
    private async Task Capture(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage(); image.SavePng(_output + name + ".png");
    }
}
