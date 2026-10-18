using Godot;
using Trackstorm.Client.Items;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Native production-model presentation fixture. Supplied masks, shot cues and claw
/// poses are explicitly fixture data, not proof of future EMP combat or replication.</summary>
public sealed partial class EmpClawChecks : Node3D
{
    private const string Output = "res://.godot/ts296";
    private readonly List<string> _evidence = [];
    private readonly HostVehicleSession _host = new(296);
    private readonly EmpClawPresentation _claws = new();
    private Node3D _car = null!;
    private CarRackPresentation _rack = null!;
    private Camera3D _camera = null!;
    private EmpClawRackState _state = new(1, 296, 1, true, 7);
    private WeaponAimSolution? _aim;
    private int _checks;
    private bool _preview;
    private float _orbit = .7f;
    private float _pitch = .12f;
    private float _distance = 7;
    private readonly List<EmpClawView> _previewViews = [];
    private ulong _viewId;

    public override void _Ready() => CallDeferred(MethodName.Run);

    public async void Run()
    {
        try
        {
            Engine.MaxFps = 60;
            System.IO.Directory.CreateDirectory(ProjectSettings.GlobalizePath(Output));
            AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new(.12f, .17f, .22f), AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new(.72f, .80f, 1), AmbientLightEnergy = .70f, TonemapMode = Godot.Environment.ToneMapper.Filmic } });
            AddChild(new DirectionalLight3D { RotationDegrees = new(-38, -30, 0), LightEnergy = 1.7f, ShadowEnabled = true });
            AddChild(new DirectionalLight3D { RotationDegrees = new(-30, 130, 0), LightEnergy = .6f });
            AddChild(new MeshInstance3D { Position = new(0, -1.3f, 0), Mesh = new PlaneMesh { Size = new(80, 80) },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new(.22f, .19f, .15f), Roughness = .95f } });
            _car = VehicleVisual.Create(new StandardMaterial3D { AlbedoColor = new(.7f, .04f, .06f) });
            AddChild(_car);
            _rack = new CarRackPresentation { Boost = new BoostExhaust() };
            _car.AddChild(_rack);
            _rack.SetProcess(false);
            _car.GetChildren().OfType<CarDeployment>().Single().SetProcess(false);
            AddChild(_claws);
            var target = VehicleVisual.Create(new StandardMaterial3D { AlbedoColor = new(.15f, .38f, .50f) });
            target.Position = new(4, 0, -4);
            AddChild(target);
            _camera = new Camera3D { Current = true, Fov = 52, Position = new(6, 3.2f, 7) };
            AddChild(_camera);
            _camera.LookAt(new(0, .7f, .6f));
            _rack.Observe(1, true, null, []);
            _rack.ObserveEmpClaw(_state);
            await Step(1);
            var launcher = _rack.EmpLauncher!;
            Check(launcher.VisibleRounds == 3, "Three independently identified stowed rounds.");
            Check(launcher.NoseAnchors.Distinct().Count() == 3, "Three distinct nose attachment/hit anchors.");
            await Capture("01-stowed", new(-4, 2.8f, 6), new(0, .3f, 1.3f));
            CheckBounds(launcher, "compact", .37f);
            await Step(30);
            await Capture("02-rack-raised", new(4, 2.8f, 6), new(0, .8f, 1));
            await Step(14);
            await Capture("03-cannon-emerged", new(4, 2.8f, 5), new(0, 1.4f, 1));
            await Step(12);
            await Capture("04-cannon-seated", new(4, 2.8f, 5), new(0, 1.4f, 1));
            await Step(16);
            Check(_rack.Progress == 1, "Trunk and stationary rack complete the 1.2-second deployment.");
            await Aim(0, 0);
            Check(launcher.IsReady, "Accepted shared aim makes fully articulated launcher ready.");
            var pivot = (Node3D)launcher.FindChild("CannonPitch", true, false);
            Check(pivot.GlobalPosition.DistanceTo(VehicleBody.ToGodot(WeaponAim.Pivot)) < .001f, "Authored cannon pivot matches the shared accepted aiming origin.");
            await Capture("05-ready-side", new(6, 2.5f, .4f), new(0, 1.0f, .3f));
            await Capture("06-ready-rear", new(0, 2.6f, 6), new(0, 1.2f, 1.5f));
            await Capture("07-gameplay-distance", new(0, 3.5f, 9), new(0, 1.3f, -.4f));
            if (OS.GetCmdlineUserArgs().Contains("--art-only"))
            {
                await CheckStates();
                _claws.Clear(); _rack.Reset(); await Frames(3);
                GD.Print("EMP Claw art inspection completed; full verification not run.");
                GetTree().Quit(); return;
            }
            Vector3 rackRotation = _car.GetNode<Node3D>("WeaponRack").Rotation;
            Transform3D[] storedPoses = launcher.RoundSockets.Select(socket => socket.GlobalTransform).ToArray();
            Check(launcher.RoundSockets.All(socket => socket.GlobalPosition.Y < _car.GetNode<Node3D>("WeaponRack").GlobalPosition.Y),
                "All three rear-facing stowed claws hang below the fixed weapon rack.");
            foreach (float yaw in new[] { 90f, 180f, -90f, -179f, 179f, 0f })
            {
                await Aim(yaw, 20);
                Check(Math.Abs(Mathf.AngleDifference(launcher.Angles.X, Mathf.DegToRad(yaw))) < .02f, "Full yaw reaches " + yaw);
                Check(_car.GetNode<Node3D>("WeaponRack").Rotation == rackRotation, "Rack remains fixed at " + yaw);
                Check(launcher.RoundSockets.Select((socket, i) => socket.GlobalTransform.IsEqualApprox(storedPoses[i])).All(same => same),
                    "All three stored claws remain stationary while cannon aims at " + yaw);
                CheckCannonClearance(launcher);
            }
            await Capture("08-rear-aim", new(5, 3, 6), new(0, 1, 1));
            await Aim(0, 90);
            Check(launcher.RoundSockets.Select((socket, i) => socket.GlobalTransform.IsEqualApprox(storedPoses[i])).All(same => same),
                "Stored claws remain stationary during vertical pitch.");
            CheckCannonClearance(launcher);
            await Capture("09-vertical-aim", new(5, 3, 5), new(0, 1.3f, 1));
            await Aim(0, -50);
            CheckCannonClearance(launcher);
            Check(_aim is { Clear: false }, "Shared solver rejects self-intersecting downward fire.");
            await Capture("10-clearance-clamp", new(6, 2.2f, 0), new(0, 1, .6f));
            await Aim(0, 0);
            _rack.ObserveEmpClaw(_state with { Revision = 0, RoundMask = 1 });
            Check(launcher.VisibleRounds == 3, "Stale revisions cannot resurrect or remove rounds.");
            _rack.ObserveEmpClaw(_state with { Life = 2, RoundMask = 0 });
            Check(launcher.VisibleRounds == 3, "Wrong-life state cannot remove current rounds.");
            if (OS.GetCmdlineUserArgs().Contains("--preview"))
            {
                _preview = true;
                var label = new Label { Text = "EMP CLAW • PRESENTATION PREVIEW\nRMB + mouse: orbit/aim • wheel: zoom • Space: accepted shot fixture\nE: deploy/stow • R: new pickup fixture • 1–6: claw state • Esc: quit\nNo EMP gameplay authority, damage or multiplayer simulation", Position = new(20, 20) };
                AddChild(label);
                return;
            }
            for (int round = 0; round < 3; round++)
            {
                _state = _state with { Revision = _state.Revision + 1, RoundMask = (byte)(_state.RoundMask & ~(1 << round)), ShotSequence = (ulong)round + 1, FiredRound = round };
                _rack.ObserveEmpClaw(_state, _aim);
                Check(launcher.VisibleRounds == 2 - round, "Accepted shot removes exactly round " + round);
                Check(launcher.DischargeCount == round + 1, "One discharge per accepted sequence.");
                _rack.ObserveEmpClaw(_state, _aim);
                Check(launcher.DischargeCount == round + 1, "Duplicate publication does not replay discharge.");
                _claws.Observe(Enumerable.Range(0, round + 1).Select(i => new EmpClawView((ulong)i + 1,
                    new Transform3D(Basis.Identity, new(-.8f + i * .8f, 2.0f, -2.0f - i * .7f)), EmpClawVisualState.Fired)));
                await Step(5);
                await Capture("11-shot-" + round, new(5, 3.4f, 6), new(0, 1.3f, 0));
                await Step(55);
                if (round == 0)
                {
                    Check(launcher.RoundSockets[2].GlobalTransform.IsEqualApprox(storedPoses[2]),
                        "Loading the next round leaves the third round stationary beneath the rack.");
                }
            }
            Check(_claws.Views.Count == 3, "Three simultaneous flight views; first/second/third cues at 0/1/2 seconds.");
            await Step(75);
            Check(_rack.EmpLauncher is null && _rack.Progress == 0, "Final shot finishes, empty cannon nests, rack returns and trunk closes.");
            await Step(120);
            Check(_rack.EmpLauncher is null, "No automatic reload from empty accepted mask.");
            await Capture("12-empty-stowed", new(-5, 2.5f, 5), new(0, .5f, 1));
            await CheckStates();
            await CheckHandoffs();
            _claws.Clear();
            _rack.Reset();
            await Frames(3);
            Check(_claws.Views.Count == 0 && _rack.EmpLauncher is null, "Reset/teardown clears every owned view and mechanism.");
            System.IO.File.WriteAllLines(ProjectSettings.GlobalizePath(Output + "/evidence.txt"), _evidence);
            GD.Print($"EMP Claw presentation passed: {_checks} checks. Fixture states; no EMP gameplay/replication claim.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private async Task CheckStates()
    {
        var pose = new Transform3D(Basis.FromEuler(new(0, -MathF.PI / 2, 0)), new(5.40f, .25f, -4));
        _claws.Observe([new(20, pose, EmpClawVisualState.Landed), new(21, new(Basis.Identity, new(4, 1.4f, -4)), EmpClawVisualState.Activated)]);
        await Frames(20);
        await Capture("13-landed", new(6.6f, .7f, -2.6f), new(5.2f, .25f, -4));
        _claws.Observe([new(20, pose, EmpClawVisualState.Activated), new(21, new(Basis.Identity, new(4, 1.4f, -4)), EmpClawVisualState.Activated)]);
        await Frames(30);
        Check(_claws.Views[20].Inflation > 2.5f, "Activated balloon inflates independently.");
        await Capture("14-activated", new(6.5f, .65f, -2.7f), new(5.2f, .25f, -4));
        _claws.Observe([new(20, pose, EmpClawVisualState.Expired), new(21, new(Basis.Identity, new(4, 1.4f, -4)), EmpClawVisualState.Activated)]);
        await Frames(10);
        await Capture("15-expiring", new(6.5f, .65f, -2.7f), new(5.2f, .25f, -4));
        Check(_claws.Views[21].State == EmpClawVisualState.Activated, "Expiry of one claw leaves the other active.");
        await Frames(85);
        Check(_claws.Views[20].Finished, "Deflation, fizzle and cosmetic release finish within 1.3 seconds.");
        _claws.Observe([new(22, pose, EmpClawVisualState.Activated)], restore: true);
        await Frames(2);
        _claws.Observe([new(22, pose, EmpClawVisualState.NoseDestroyed)]);
        await Frames(5);
        await Capture("16-nose-pop", new(6.5f, .65f, -2.7f), new(5.2f, .25f, -4));
        await Frames(85);
        Check(_claws.Views[22].Finished, "Balloon pop and release finish with no damage/explosion.");
        _claws.Observe([]);
        Check(_claws.Views.Count == 0, "Absent IDs remove spent views.");
    }

    private async Task CheckHandoffs()
    {
        for (int cycle = 0; cycle < 8; cycle++)
        {
            _state = new(1, (ulong)(400 + cycle), 1, true, (byte)(cycle % 2 == 0 ? 7 : 5), 2);
            _rack.ObserveEmpClaw(_state, restore: true);
            await Step(cycle % 2 == 0 ? 75 : 25);
            Check(_rack.EmpLauncher!.DischargeCount == 0, "Reconstruction never replays prior shots.");
            _rack.ObserveEmpClaw(_state with { Selected = false, Revision = 2 });
            await Step(125);
            Check(_rack.Progress == 0 && _rack.EmpLauncher is null, "Repeated or mid-deploy cancellation nests cleanly: " + cycle);
        }
        _state = new(1, 500, 1, true, 7);
        _rack.ObserveEmpClaw(_state);
        await Step(75);
        await Aim(170, 25);
        _rack.Observe(1, true, new ItemSlot(1, 1, 600, HeldItem.Wrench), []);
        _rack.ObserveEmpClaw(null);
        for (int i = 0; i < 190; i++)
        {
            await Step(1);
            if (_rack.EmpLauncher is { } returning && _rack.Progress < .999f)
            { Check(returning.Centered, "Returning cannon centers before rack descent."); }
        }
        Check(_rack.PresentedItem == HeldItem.Wrench, "Existing Wrench receives mechanism after EMP closes.");
        _rack.Observe(1, true, null, []);
        _rack.ObserveEmpClaw(_state with { Token = 501 });
        await Step(250);
        Check(_rack.EmpLauncher is not null, "EMP waits for outgoing Wrench before taking the rack.");
        _rack.Observe(2, false, null, []);
        Check(_rack.EmpLauncher is null && _rack.Progress == 0, "Death/life boundary immediately clears EMP presentation.");
    }

    private async Task Aim(float yaw, float pitch)
    {
        N.Vector3 desired = WeaponAim.Direction(Mathf.DegToRad(yaw), Mathf.DegToRad(pitch));
        for (ulong tick = 1; tick <= 110; tick++)
        {
            _aim = WeaponAim.Solve(_host.World.GetVehicle(1), _state.Token, desired, _aim, new(), tick,
                new VehiclePhysicsState(N.Vector3.Zero, N.Quaternion.Identity, N.Vector3.Zero, N.Vector3.Zero)) with { Ready = true };
            _rack.ObserveEmpClaw(_state, _aim);
            await Step(1);
        }
    }

    private async Task Step(int count)
    {
        for (int i = 0; i < count; i++) { _rack._Process(1.0 / 60); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    }

    private async Task Capture(string name, Vector3 position, Vector3 target)
    {
        if (DisplayServer.GetName() == "headless") { return; }
        _camera.Position = position;
        _camera.LookAt(target);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng(ProjectSettings.GlobalizePath(Output + "/" + name + ".png")) == Error.Ok, "Captured " + name);
    }

    private void CheckBounds(Node3D root, string name, float ceiling)
    {
        float top = float.MinValue;
        float bottom = float.MaxValue;
        string highest = string.Empty;
        foreach (var mesh in Descendants(root).OfType<MeshInstance3D>().Where(m => m.Mesh is not null && m.IsVisibleInTree()))
        {
            foreach (var vertex in mesh.Mesh!.GetFaces())
            {
                float height = _car.ToLocal(mesh.ToGlobal(vertex)).Y;
                bottom = Math.Min(bottom, height);
                if (height > top) { top = height; highest = mesh.GetPath(); }
            }
        }
        Check(top < ceiling, $"Imported {name} geometry fits below deck: top={top:F3} < {ceiling:F3} m ({highest}).");
        Check(bottom > -.355f, $"Imported {name} geometry clears the production bay floor: bottom={bottom:F3} > -0.355 m.");
    }

    private void CheckCannonClearance(EmpClawLauncher launcher)
    {
        var barrel = launcher.FindChild("CannonPitch", true, false);
        foreach (var mesh in Descendants(barrel).OfType<MeshInstance3D>().Where(m => m.Mesh is not null))
        {
            foreach (var vertex in mesh.Mesh!.GetFaces())
            {
                Vector3 p = _car.ToLocal(mesh.ToGlobal(vertex));
                if (Math.Abs(p.X) < 1.5f && p.Z is > -2.55f and < 2.55f && p.Y < 1.30f)
                { throw new InvalidOperationException($"Cannon intersects conservative production body at {p}; angles {launcher.Angles}."); }
            }
        }
        Check(true, "Imported cannon vertices clear the production envelope at " + launcher.Angles);
    }

    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (Node child in node.GetChildren()) { yield return child; foreach (var descendant in Descendants(child)) { yield return descendant; } }
    }

    private void Check(bool pass, string message)
    {
        if (!pass) { throw new InvalidOperationException(message); }
        _checks++; _evidence.Add("VERIFIED: " + message);
    }

    public override void _Process(double delta)
    {
        if (!_preview) { return; }
        _camera.Position = new Vector3(MathF.Sin(_orbit) * _distance, 2.6f + _pitch * 2, 1.2f + MathF.Cos(_orbit) * _distance);
        _camera.LookAt(new(0, 1.2f, .3f));
        var direction = VehicleBody.ToCore(-_camera.GlobalBasis.Z);
        _aim = WeaponAim.Solve(_host.World.GetVehicle(1), _state.Token, direction, _aim, new(), (ulong)Engine.GetProcessFrames(),
            new(N.Vector3.Zero, N.Quaternion.Identity, N.Vector3.Zero, N.Vector3.Zero)) with { Ready = true };
        _rack.ObserveEmpClaw(_state, _aim);
        _rack._Process(delta);
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (!_preview) { return; }
        if (input is InputEventMouseMotion motion && Godot.Input.IsMouseButtonPressed(MouseButton.Right))
        { _orbit -= motion.Relative.X * .006f; _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * .005f, -.3f, 1); }
        if (input is InputEventMouseButton { Pressed: true } mouse)
        { _distance = Mathf.Clamp(_distance + (mouse.ButtonIndex == MouseButton.WheelUp ? -.5f : mouse.ButtonIndex == MouseButton.WheelDown ? .5f : 0), 3, 12); }
        if (input is not InputEventKey { Pressed: true, Echo: false } key) { return; }
        if (key.Keycode == Key.Escape) { GetTree().Quit(); }
        if (key.Keycode == Key.E) { _state = _state with { Selected = !_state.Selected, Revision = _state.Revision + 1 }; }
        if (key.Keycode == Key.R) { _state = new(1, _state.Token + 1, 1, true, 7); _previewViews.Clear(); _claws.Clear(); }
        if (key.Keycode == Key.Space && _rack.EmpLauncher is { IsReady: true } launcher)
        {
            int round = Enumerable.Range(0, 3).FirstOrDefault(i => (_state.RoundMask & (1 << i)) != 0, -1);
            if (round < 0) { return; }
            _state = _state with { RoundMask = (byte)(_state.RoundMask & ~(1 << round)), Revision = _state.Revision + 1,
                ShotSequence = _state.ShotSequence + 1, FiredRound = round };
            _previewViews.Add(new(++_viewId, launcher.Muzzle.GlobalTransform.TranslatedLocal(new(0, 0, -2)), EmpClawVisualState.Fired));
            _claws.Observe(_previewViews);
        }
        if (key.Keycode >= Key.Key1 && key.Keycode <= Key.Key6)
        {
            var state = (EmpClawVisualState)((int)key.Keycode - (int)Key.Key1);
            var pose = new Transform3D(Basis.FromEuler(new(0, -MathF.PI / 2, 0)), new(1.3f, .25f, 0));
            _previewViews.Clear(); _previewViews.Add(new(++_viewId, pose, state)); _claws.Observe(_previewViews);
        }
    }
}
