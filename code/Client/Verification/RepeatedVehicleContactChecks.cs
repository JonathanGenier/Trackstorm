using System.Text.Json;
using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Sustained rear contact through the production batched native solver.</summary>
public sealed partial class RepeatedVehicleContactChecks : Node3D
{
    public override void _Ready() => CallDeferred(MethodName.Run);

    private async void Run()
    {
        try
        {
            var floor = new StaticBody3D(); floor.AddToGroup("landing_terrain");
            floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(2000, 2, 2000) }, Position = new(0, -1, 0) });
            AddChild(floor);
            var world = new Core.Simulation.Simulation(new(60));
            var bodies = new Dictionary<ulong, NetworkVehicleBody>();
            for (ulong id = 1; id <= 2; id++)
            {
                world.AddVehicle(id, new(), new() { MaxHP = 1000, CollisionScale = 5 },
                    new(new(0, VehicleDimensions.RideHeight, id == 1 ? 0 : 5.2f), N.Quaternion.Identity, new(0, 0, -10), N.Vector3.Zero));
                var body = new NetworkVehicleBody { VehicleId = id }; AddChild(body); body.Apply(world.GetVehicle(id)); bodies.Add(id, body);
            }
            var trace = new List<object>();
            float maximumTravelError = 0, maximumSpeedChange = 0;
            int contactFrames = 0;
            for (int frame = 0; frame < 900; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                var before = world.State.Vehicles.ToDictionary(v => v.VehicleId);
                var observations = NetworkVehicleBody.ObserveBatch(bodies, world.State.Vehicles);
                InputFrame Input(ulong id) => new(world.State.Tick + 1, 0,
                    (ushort)(ushort.MaxValue * (frame < 660 ? (id == 1 ? 0.25f : 0.4f) : 0)), 0, 0, 0, 0);
                var results = world.Step(Input(1), observations.Select(pair => new VehicleStepRequest(pair.Key, Input(pair.Key), pair.Value)).ToArray());
                foreach (var result in results) { bodies[result.Snapshot.VehicleId].Apply(result.Snapshot); }
                var lead = observations[1]; var follow = observations[2];
                int contacts = follow.Contacts.Count(c => c.OtherVehicleId != 0) + lead.Contacts.Count(c => c.OtherVehicleId != 0);
                float travel = follow.Physics.Position.Z - before[2].Movement.Physics.Position.Z;
                float expected = before[2].Movement.Physics.LinearVelocity.Z / 60;
                float travelError = Math.Abs(travel - expected);
                float speedChange = Math.Abs(follow.Physics.LinearVelocity.Z - before[2].ObservedPhysics.LinearVelocity.Z);
                if (contacts > 0 && frame > 60 && frame < 600)
                {
                    contactFrames++;
                    maximumTravelError = Math.Max(maximumTravelError, travelError);
                    maximumSpeedChange = Math.Max(maximumSpeedChange, speedChange);
                }
                trace.Add(new { frame, leadZ = lead.Physics.Position.Z, followZ = follow.Physics.Position.Z,
                    leadSpeed = lead.Physics.LinearVelocity.Z, followSpeed = follow.Physics.LinearVelocity.Z,
                    followY = follow.Physics.Position.Y, contacts, travelError, speedChange,
                    angular = follow.Physics.AngularVelocity.Length(), hp = world.GetVehicle(2).Damage.CurrentHP });
            }
            string directory = ProjectSettings.GlobalizePath("res://.godot/ts-280"); System.IO.Directory.CreateDirectory(directory);
            string label = OS.GetCmdlineUserArgs().FirstOrDefault() ?? "repeated-vehicles";
            System.IO.File.WriteAllText(System.IO.Path.Combine(directory, label + ".json"), JsonSerializer.Serialize(trace));
            GD.Print($"Repeated vehicle contact: frames={contactFrames}, travelError={maximumTravelError:F6}, speedChange={maximumSpeedChange:F6}");
            if (contactFrames < 120 || maximumTravelError > 0.04f || maximumSpeedChange > 0.75f)
                throw new InvalidOperationException("Sustained following contact must preserve smooth common motion.");
            GD.Print("Repeated vehicle contact checks passed.");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError(exception.ToString()); GetTree().Quit(1); }
    }
}
