using System.Text.Json;
using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Same-boundary native vehicle/world contact preservation through the production capture batch.</summary>
public sealed partial class MixedContactChecks : Node3D
{
    private Camera3D? _camera;
    private string _directory = "";
    public override void _Ready() => CallDeferred(MethodName.Run);

    private async void Run()
    {
        try
        {
            _directory = ProjectSettings.GlobalizePath("res://.godot/ts-283/round2/mixed");
            System.IO.Directory.CreateDirectory(_directory);
            if (DisplayServer.GetName() != "headless")
            {
                Engine.MaxFps = 60;
                AddChild(new DirectionalLight3D { RotationDegrees = new(-60, -25, 0), LightEnergy = 1.5f });
                _camera = new Camera3D { Current = true, PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off }; AddChild(_camera);
            }
            foreach (string scenario in new[] { "barrier-push", "barrier-touch", "terrain-touch", "world-only" })
            { await Exercise(scenario); }
            GD.Print("Mixed contact checks passed."); GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError(exception.ToString()); GetTree().Quit(1); }
    }

    private async Task Exercise(string scenario)
    {
        var fixture = new Node3D(); AddChild(fixture);
        bool terrain = scenario == "terrain-touch", alone = scenario == "world-only";
        var floor = Box(new(0, -1, 0), new(400, 2, 400)); floor.AddToGroup("landing_terrain"); fixture.AddChild(floor);
        if (!terrain) { fixture.AddChild(Box(new(2.32f, 3, 0), new(2, 8, 400))); }
        var world = new Core.Simulation.Simulation(new(60));
        var bodies = new Dictionary<ulong, VehicleBody>();
        var tuning = new VehicleConfiguration();
        var rotation = terrain ? N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitX, -.6f) : N.Quaternion.Identity;
        for (ulong id = 1; id <= 2; id++)
        {
            var position = new N.Vector3(id == 1 ? 0 : alone ? -100 : -2.64f, terrain ? 2.1f : VehicleDimensions.RideHeight, 0);
            var velocity = new N.Vector3(alone ? 6 : scenario == "barrier-push" && id == 2 ? 6 : .02f, terrain ? -8 : 0, -15);
            var pose = new VehiclePhysicsState(position, rotation, velocity, N.Vector3.Zero);
            var damage = new DamageConfiguration { MaxHP = 1000, CollisionScale = 5 };
            world.AddVehicle(id, tuning, damage, pose);
            var body = new VehicleBody { VehicleId = id, Configuration = tuning, DamageConfiguration = damage,
                Position = VehicleBody.ToGodot(position), Quaternion = VehicleBody.ToGodot(rotation), LinearVelocity = VehicleBody.ToGodot(velocity),
                Paint = id == 1 ? new("2fd4df") : new("f0ab3c") };
            body.Initialize(world); fixture.AddChild(body); bodies.Add(id, body);
        }
        if (_camera is not null)
        {
            Vector3 center = VehicleBody.ToGodot(world.GetVehicle(1).Movement.Physics.Position);
            _camera.Position = center + new Vector3(-8, 7, 10); _camera.LookAt(center);
        }
        var trace = new List<object>();
        int mixed = 0, quiet = 0, worldFrames = 0;
        float torque = 0, slowdown = 0, maximumError = 0, peakYaw = 0;
        for (int frame = 0; frame < 300; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var input = new InputFrame(world.State.Tick + 1, 0, 0, 0, 0, 0, 0);
            // Both captures read the SAME native boundary without advancing physics/Core.
            var raw = bodies.Values.Select(body => body.Capture(input)).ToDictionary(request => request.VehicleId);
            var captured = VehicleBody.CaptureBatch(bodies.Values, _ => input);
            bool pairContact = raw.Values.Any(r => r.Observation.Contacts.Any(c => c.OtherVehicleId != 0));
            bool worldContact = raw.Values.Any(r => r.Observation.Contacts.Any(c => c.OtherVehicleId == 0));
            if (worldContact && (pairContact || alone))
            {
                worldFrames++;
                if (pairContact) { mixed++; }
                var expected = VehicleCollision.ResolveContacts(raw.ToDictionary(p => p.Key, p => p.Value.Observation), _ => tuning);
                foreach (var request in captured)
                {
                    var native = raw[request.VehicleId].Observation;
                    var prior = world.GetVehicle(request.VehicleId).Movement.Physics;
                    var result = request.Observation.Physics;
                    var target = expected[request.VehicleId].Physics;
                    float error = N.Vector3.Distance(result.LinearVelocity, target.LinearVelocity) + N.Vector3.Distance(result.AngularVelocity, target.AngularVelocity);
                    maximumError = Math.Max(maximumError, error);
                    Require(error < .00001f, "batch must preserve complete native world result before one residual pair solve");
                    Require(request.Observation.Contacts.SequenceEqual(native.Contacts), "raw damage contacts remain unchanged");
                    foreach (var contact in native.Contacts.Where(c => c.OtherVehicleId == 0))
                    {
                        var beforeTangent = prior.LinearVelocity - contact.Normal * N.Vector3.Dot(prior.LinearVelocity, contact.Normal);
                        var afterTangent = native.Physics.LinearVelocity - contact.Normal * N.Vector3.Dot(native.Physics.LinearVelocity, contact.Normal);
                        slowdown = Math.Max(slowdown, beforeTangent.Length() - afterTangent.Length());
                        torque = Math.Max(torque, N.Vector3.Distance(prior.AngularVelocity, native.Physics.AngularVelocity));
                    }
                    if (pairContact && native.Contacts.Any(c => c.OtherVehicleId != 0 && Math.Abs(N.Vector3.Dot(c.RelativeVelocity, c.Normal)) < .1f)) { quiet++; }
                }
                if (_camera is not null && (mixed == 1 || worldFrames == 20))
                {
                    Vector3 center = VehicleBody.ToGodot(raw[1].Observation.Physics.Position);
                    _camera.Position = center + new Vector3(-8, 7, 10); _camera.LookAt(center);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using var image = GetViewport().GetTexture().GetImage(); image.SavePng(System.IO.Path.Combine(_directory, $"{scenario}-{worldFrames}.png"));
                }
            }
            foreach (var result in world.Step(input, captured)) { bodies[result.Snapshot.VehicleId].Apply(result); }
            peakYaw = Math.Max(peakYaw, Math.Abs(world.GetVehicle(1).Movement.Physics.AngularVelocity.Y));
            trace.Add(new { frame, pairContact, worldContact, physics = captured.Select(r => new { r.VehicleId, raw = raw[r.VehicleId].Observation.Physics, solved = r.Observation.Physics }) });
        }
        string summary = $"{scenario}: mixed={mixed} worldFrames={worldFrames} lowClosing={quiet} tangentLoss={slowdown:F6} angularChange={torque:F6} batchError={maximumError:F8} peakYaw={peakYaw:F4}";
        GD.Print(summary); System.IO.File.AppendAllText(System.IO.Path.Combine(_directory, "evidence.txt"), summary + "\n");
        System.IO.File.WriteAllText(System.IO.Path.Combine(_directory, scenario + ".json"), JsonSerializer.Serialize(trace, new JsonSerializerOptions { IncludeFields = true }));
        Require(worldFrames > 0 && (alone || mixed > 0), "fixture must exercise simultaneous vehicle and world contacts");
        Require(slowdown > .0001f && torque > .0001f, "fixture must observe native tangential slowdown and angular response");
        if (scenario.EndsWith("touch", StringComparison.Ordinal)) { Require(quiet > 0, "fixture must include low-closing vehicle touching during world contact"); }
        fixture.QueueFree(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static StaticBody3D Box(Vector3 position, Vector3 size)
    {
        var body = new StaticBody3D { Position = position };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        body.AddChild(VehicleBody.Box(size, Vector3.Zero, new("947454"))); return body;
    }
    private static void Require(bool condition, string message) { if (!condition) { throw new InvalidOperationException(message); } }
}
