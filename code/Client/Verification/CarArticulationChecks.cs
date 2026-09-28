using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;

namespace Trackstorm.Client.Verification;

/// <summary>Native driving and rendered articulation acceptance fixture for the production Blender Car.</summary>
public sealed partial class CarArticulationChecks : Node3D
{
    private VehicleArena _arena = null!;
    private Camera3D _camera = null!;
    private Node3D _model = null!;
    private int _assertions;
    private readonly List<object> _trace = new();
    private readonly string _output = "res://.godot/ts259-round4/car";

    public override void _Ready() => CallDeferred(MethodName.Run);

    public async void Run()
    {
        try
        {
            System.IO.Directory.CreateDirectory(ProjectSettings.GlobalizePath(_output));
            _arena = new VehicleArena { LegacyTestLayout = true };
            AddChild(_arena);
            _camera = new Camera3D { Current = true, Fov = 48, PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off };
            AddChild(_camera);
            _camera.MakeCurrent();
            _model = _arena.Player.GetNode<Node3D>("WastelandVehicle");
            Check(Math.Abs(Math.Abs(_model.GetNode<Node3D>("WheelCarrier_FL").Position.Z - _model.GetNode<Node3D>("WheelCarrier_RL").Position.Z) - 3.101105f) < 0.001f, "Refined visual wheelbase is 3.101105 metres.");
            for (int i = 0; i < 150; i++) { await Step(); }
            await Capture("front-closed", new Vector3(5, 2.9f, -6));
            await Capture("rear-closed", new Vector3(-5, 2.9f, 6));
            await Capture("front", new Vector3(0, 1.2f, -6.4f));
            await Capture("rear", new Vector3(0, 1.2f, 6.4f));
            await Capture("left", new Vector3(-6.8f, .8f, 0));
            await Capture("right", new Vector3(6.8f, .8f, 0));
            await Capture("high-front", new Vector3(-4.5f, 4.8f, -5));
            await Capture("high-rear", new Vector3(4.5f, 4.8f, 5));
            await Capture("top-closed", new Vector3(0, 8, .01f));
            VerifyIndependentSpeeds();
            Check(Math.Abs(_model.GetNode<Node3D>("WeaponRack").Position.Z - 1.72f) < 0.001f, "Deployment preserves the authored rear-bay position.");
            await Capture("suspension-front", new Vector3(2.8f, -0.15f, -3.7f));
            await Capture("suspension-rear", new Vector3(-2.8f, -.2f, 3.7f));
            VerifySuspensionMounts();
            Check(_model.GetNode<Node3D>("WeaponRack").Position.Y < 0, "Rack rests inside the rear compartment.");
            string[] names = ["FL", "FR", "RL", "RR"];
            foreach (string corner in names)
            {
                Check(_model.HasNode("ShockRod_" + corner) && _model.HasNode("ShockRod2_" + corner), "Two shock rods at " + corner);
                Check(_model.HasNode("SuspensionLink_" + corner + "_Upper2"), "Second articulated shock sleeve at " + corner);
            }
            Vector3[] rotations = names.Select(n => _model.GetNode<Node3D>($"WheelCarrier_{n}/WheelSpin_{n}").Rotation).ToArray();
            for (int i = 0; i < 90; i++) { await Step(ushort.MaxValue, 6000); }
            for (int i = 0; i < 4; i++)
            {
                Node3D carrier = _model.GetNode<Node3D>("WheelCarrier_" + names[i]);
                Check(!carrier.GetNode<Node3D>("WheelSpin_" + names[i]).Rotation.IsEqualApprox(rotations[i]), names[i] + " rotates under actual driving.");
                Check(i >= 2 || Math.Abs(carrier.Rotation.Y) > 0.01f, names[i] + " steers from movement input.");
                Check(i < 2 || Math.Abs(carrier.Rotation.Y) < 0.001f, names[i] + " rear carrier stays unsteered.");
            }
            Check(_arena.Player.State.CommandSpeed > 3, "Production physics still accelerates the integrated Car.");
            await Capture("driving-turn", new Vector3(4, 2.3f, -5));
            VerifySuspensionMounts();
            CarLighting lamps = _model.GetChildren().OfType<CarLighting>().Single();
            for (int i = 0; i < 10; i++) { await Step(); }
            Check(!lamps.Braking && !lamps.Reversing, "Forward coasting does not illuminate brake/reverse lamps.");
            for (int i = 0; i < 8; i++) { await Step(0, 0, ushort.MaxValue); }
            Check(lamps.Braking, "Actual brake input activates lamps while moving forward.");
            await Capture("braking", new Vector3(-4, 2, 5));
            for (int i = 0; i < 150; i++) { await Step(0, 0, ushort.MaxValue); }
            Check(lamps.Reversing && !lamps.Braking, "Reverse travel lights white lamps without falsely reporting braking.");
            await Capture("reversing", new Vector3(-4, 2, 5));
            var environment = Descendants(_arena).OfType<Trackstorm.Client.Arenas.EnvironmentPresentation>().Single();
            environment.Apply(Trackstorm.Core.Development.EnvironmentPreset.Night);
            await Capture("night-reverse", new Vector3(-4, 2, 5));
            await Capture("night-headlights", new Vector3(4, 3, -7));
            environment.Apply(Trackstorm.Core.Development.EnvironmentPreset.ClearBlue);
            CarDeployment deployment = _model.GetChildren().OfType<CarDeployment>().Single();
            for (int cycle = 0; cycle < 3; cycle++)
            {
                deployment.Deployed = true;
                for (int i = 0; i < 110; i++)
                {
                    await Step();
                    if (cycle == 0 && i == 6) { await Capture("lids-opening", new Vector3(-4, 3.8f, 5)); }
                    if (cycle == 0 && i == 22) { await Capture("rack-rising", new Vector3(-4, 3.8f, 5)); }
                }
                var rack = _model.GetNode<Node3D>("WeaponRack");
                Check(Math.Abs(rack.Position.Y - 1.34f) < .002f, "Rack reaches deployed height.");
                Check(Math.Abs(_model.GetNode<Node3D>("TrunkHinge_L").Rotation.Z) > 1.69f, "Lid clears rack before full lift.");
                foreach (string mount in new[] { "WeaponMount_L_Front", "WeaponMount_R_Front", "WeaponMount_L_Rear", "WeaponMount_R_Rear" })
                {
                    Check(rack.GetNode<Node3D>(mount).Position.Y == 0.17f, "Weapon mount retains its rack-local transform.");
                }
                if (cycle == 0) { await Capture("rear-deployed", new Vector3(-4, 3.2f, 5)); await Capture("top-deployed", new Vector3(0, 7, 1)); }
                deployment.Deployed = false;
                for (int i = 0; i < 110; i++) { await Step(); }
                Check(Math.Abs(rack.Position.Y + .08f) < .002f && _model.GetNode<Node3D>("TrunkHinge_L").Rotation.IsZeroApprox(), "Rack retracts before lids close.");
            }
            deployment.Deployed = true;
            for (int i = 0; i < 60; i++) { await Step(); }
            deployment.Deployed = false;
            for (int i = 0; i < 110; i++) { await Step(); }
            Check(_model.GetNode<Node3D>("TrunkHinge_R").Rotation.IsZeroApprox(), "Mid-deployment reversal returns to closed pose.");
            for (int i = 0; i < 4; i++)
            {
                Node3D wheel = _model.GetNode<Node3D>("WheelCarrier_" + names[i]);
                Check(wheel.Position.Y < -.25f, "Settled tire remains beneath fender.");
            }
            Check(Descendants(_model).OfType<MeshInstance3D>().Count(m => m.Name.ToString().StartsWith("RoofAuxLight", StringComparison.Ordinal)) == 4, "Four separate auxiliary lenses survive import.");
            foreach (string prefix in new[] { "Headlight_", "TailRunning_", "Brake_", "Reverse_", "RearIndicator_" })
            {
                Check(Descendants(_model).OfType<MeshInstance3D>().Count(m => m.Name.ToString().StartsWith(prefix, StringComparison.Ordinal)) == 2, prefix + " has independent left/right meshes.");
            }
            await Capture("final-closed", new Vector3(4, 2.5f, -5));
            _arena.Player.ResetBody(new(new System.Numerics.Vector3(0, 4, 0), System.Numerics.Quaternion.Identity, System.Numerics.Vector3.Zero, System.Numerics.Vector3.Zero));
            float maximum = 0;
            for (int i = 0; i < 180; i++)
            {
                await Step();
                maximum = Math.Max(maximum, _arena.Player.State.Wheels.Compression.X);
                if (i == 10) { await Capture("airborne-rebound", new Vector3(4, 1.1f, -5)); }
                if (i == 48) { await Capture("landing-compression", new Vector3(4, 1.1f, -5)); }
            }
            Check(maximum > .45f, "Native landing exercises substantial suspension compression.");
            Check(_arena.Player.State.Grounded, "Car settles after native drop and rebound.");
            VerifySuspensionMounts();
            System.IO.File.WriteAllText(ProjectSettings.GlobalizePath(_output + "/trace.json"), System.Text.Json.JsonSerializer.Serialize(_trace));
            System.IO.File.WriteAllText(ProjectSettings.GlobalizePath(_output + "/results.txt"), $"PASS: {_assertions} native assertions. Actual acceleration, steering, four tire rotations, three complete deployment cycles and one reversal. Rendered captures when display is available.\n");
            GD.Print($"Car articulation passed: {_assertions} assertions.");
            _arena.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await Task.Delay(100);
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError(exception.ToString()); GetTree().Quit(1); }
    }

    private void VerifySuspensionMounts()
    {
        foreach (string corner in new[] { "FL", "FR", "RL", "RR" })
        {
            Node3D carrier = _model.GetNode<Node3D>("WheelCarrier_" + corner);
            MeshInstance3D tire = carrier.GetNode<MeshInstance3D>($"WheelSpin_{corner}/WheelSpin_{corner}_Car_Rubber");
            Aabb bounds = carrier.GlobalTransform.AffineInverse() * tire.GlobalTransform * tire.GetAabb();
            Check(Math.Abs(bounds.Size.X - .526f) < .003f, "Tire width remains 0.526 metres at " + corner);
            foreach (string suffix in new[] { "A", "B", "Upper", "Upper2" })
            {
                Node3D mount = carrier.GetNode<Node3D>($"WheelLinkMount_{corner}_{suffix}");
                Check(Math.Abs(mount.Position.X) > .38f, "Suspension mount stays inboard of rubber at " + corner);
                Node3D endPart = _model.GetNode<Node3D>(suffix is "A" or "B"
                    ? $"SuspensionLink_{corner}_{suffix}"
                    : (suffix == "Upper" ? "ShockRod_" : "ShockRod2_") + corner);
                Vector3 end = endPart.GlobalTransform * new Vector3(0, .5f, 0);
                Check(end.DistanceTo(mount.GlobalPosition) < .002f, "Articulated bar terminates on its hub bracket at " + corner + suffix);
            }
        }
    }

    private void VerifyIndependentSpeeds()
    {
        CarDeployment deployment = _model.GetChildren().OfType<CarDeployment>().Single();
        var original = _arena.Player.Configuration;
        deployment.SetProcess(false);
        foreach (var speeds in new[] { (Trunk: 3f, Rack: 3f), (Trunk: 1f, Rack: 3f), (Trunk: 3f, Rack: 1f) })
        {
            _arena.Player.Configuration = original with { TrunkDeploymentSpeed = speeds.Trunk, RackDeploymentSpeed = speeds.Rack };
            deployment.ResetPose();
            deployment.Deployed = true;
            int lidSteps = 0;
            while (deployment.Progress < .45f - .00001f && lidSteps < 10000) { deployment._Process(.001); lidSteps++; }
            Check(Math.Abs(lidSteps * .001f - .72f / speeds.Trunk) < .002f, "Independent trunk duration at " + speeds);
            int rackSteps = 0;
            while (deployment.Progress < 1 && rackSteps < 10000) { deployment._Process(.001); rackSteps++; }
            Check(Math.Abs(rackSteps * .001f - .88f / speeds.Rack) < .002f, "Independent rack duration at " + speeds);
            Node3D rack = _model.GetNode<Node3D>("WeaponRack");
            float lampTop = Descendants(_model).OfType<MeshInstance3D>().Where(m => m.Name.ToString().StartsWith("RoofAuxLight", StringComparison.Ordinal))
                .Max(m => (_model.GlobalTransform.AffineInverse() * m.GlobalTransform * m.GetAabb()).End.Y);
            Check(rack.Position.Y > lampTop + .15f, "Deployed rack clears roof lenses with margin.");
            deployment.Deployed = false;
            deployment._Process(.88 / speeds.Rack);
            Check(Math.Abs(deployment.Progress - .45f) < .0001f, "Rack retracts fully before trunk closing.");
            deployment._Process(.72 / speeds.Trunk);
            Check(deployment.Progress < .0001f, "Independent reverse travel closes fully.");
        }
        deployment.ResetPose();
        deployment.Deployed = true;
        deployment._Process(.12);
        float before = deployment.Progress;
        _arena.Player.Configuration = original with { TrunkDeploymentSpeed = 1 };
        deployment.Deployed = false;
        deployment._Process(.1);
        Check(deployment.Progress > 0 && deployment.Progress < before, "Retuned mid-trunk reversal retains continuous pose.");
        deployment.ResetPose();
        _arena.Player.Configuration = original;
        deployment.SetProcess(true);
    }

    private async Task Step(ushort throttle = 0, short steer = 0, ushort brake = 0)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        _arena.Advance(new InputFrame(_arena.Player.State.Tick + 1, steer, throttle, brake, InputButtons.None, InputButtons.None, InputButtons.None));
        var state = _arena.Player.State;
        var compression = state.Wheels.Compression;
        _trace.Add(new { state.Tick, state.SteeringAngle, state.Grounded, compression = new[] { compression.X, compression.Y, compression.Z, compression.W }, speed = state.CommandSpeed });
        _camera.Position = _arena.Player.Position + new Vector3(4, 3, 5);
        _camera.LookAt(_arena.Player.Position);
    }

    private async Task Capture(string name, Vector3 offset)
    {
        if (DisplayServer.GetName() == "headless") { return; }
        _camera.Position = _arena.Player.GlobalPosition + _arena.Player.GlobalBasis * offset;
        _camera.LookAt(_arena.Player.GlobalPosition);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using Image image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng(ProjectSettings.GlobalizePath(_output + "/" + name + ".png")) == Error.Ok, "Captured " + name);
    }

    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            yield return child;
            foreach (Node descendant in Descendants(child)) { yield return descendant; }
        }
    }

    private void Check(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
        _assertions++;
    }
}
