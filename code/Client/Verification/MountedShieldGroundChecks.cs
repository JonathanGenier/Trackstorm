using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Native regression for armor ground skimming without losing chassis or obstacle contact.</summary>
public sealed partial class MountedShieldGroundChecks : Node3D
{
    private NetworkVehicleBody _body = null!;
    private StaticBody3D _ground = null!, _obstacle = null!;
    private int _frames;

    public override void _Ready()
    {
        _ground = Box(new(100, 1, 100), new(0, -.5f, 0), true);
        _obstacle = Box(new(8, 5, .4f), new(30, 1, 3.7f), false);
        _body = new NetworkVehicleBody { VehicleId = 1 };
        AddChild(_body);
    }

    private StaticBody3D Box(Vector3 size, Vector3 position, bool terrain)
    {
        var body = new StaticBody3D { Position = position, CollisionLayer = 1 };
        if (terrain) { body.AddToGroup("landing_terrain"); }
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        AddChild(body);
        return body;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (++_frames != 3) { return; }
        try
        {
            int cases = 0, previouslySnagged = 0, chassisContacts = 0;
            using var legacy = new VehicleMotionQuery(_body, _body.GetChildren().OfType<CollisionShape3D>().ToArray());
            using var parameters = new PhysicsTestMotionParameters3D { Margin = .005f, RecoveryAsCollision = true, MaxCollisions = 4 };
            using var result = new PhysicsTestMotionResult3D();
            foreach (float slope in new[] { -25f, 0, 25f })
            foreach (float pitch in new[] { -20f, 0, 20f })
            foreach (float roll in new[] { -15f, 0, 15f })
            foreach (float height in new[] { .9f, 1.6f })
            {
                var surface = new Basis(Vector3.Right, Mathf.DegToRad(slope));
                _ground.Transform = new(surface, surface * new Vector3(0, -.5f, 0));
                var basis = surface * new Basis(Vector3.Right, Mathf.DegToRad(pitch)) * new Basis(Vector3.Back, Mathf.DegToRad(roll));
                var rotation = basis.GetRotationQuaternion();
                var pose = new VehiclePhysicsState(VehicleBody.ToCore(surface * new Vector3(0, height, 0)),
                    new(rotation.X, rotation.Y, rotation.Z, rotation.W), VehicleBody.ToCore(surface * new Vector3(0, -2, -12)), N.Vector3.Zero);
                var snapshot = Snapshot(pose);
                _body.Apply(pose);
                _body.ObserveTombstones(snapshot, []);
                var baseline = _body.Observe(snapshot);
                _body.ObserveTombstones(snapshot, [new(1, 1, 1, 1, TombstoneStage.RearShield, 1000)]);
                parameters.From = new(basis, VehicleBody.ToGodot(pose.Position));
                parameters.Motion = VehicleBody.ToGodot(pose.LinearVelocity) / 60;
                if (legacy.Test(parameters, result) && Enumerable.Range(0, result.GetCollisionCount()).Any(i => result.GetCollisionLocalShape(i) > 0)) { previouslySnagged++; }
                var shielded = _body.Observe(snapshot);
                Check(N.Vector3.Distance(baseline.Physics.Position, shielded.Physics.Position) < .001f &&
                    N.Vector3.Distance(baseline.Physics.LinearVelocity, shielded.Physics.LinearVelocity) < .001f &&
                    N.Vector3.Distance(baseline.Physics.AngularVelocity, shielded.Physics.AngularVelocity) < .001f,
                    $"Ground response differs at slope/pitch/roll/height {slope}/{pitch}/{roll}/{height}.");
                Check(shielded.Contacts.All(c => !TombstoneGeometry.Contains(c.LocalPosition)), "Ground contact reached mounted armor damage routing.");
                chassisContacts += shielded.Contacts.Count;
                cases++;
            }
            Check(previouslySnagged > 0 && chassisContacts > 0, "Fixture must reproduce old armor contacts and retain chassis impacts.");
            _ground.Transform = new(Basis.Identity, new(0, -.5f, 0));
            // The low, pitched shield overlaps terrain while reversing toward a
            // distinct solid obstacle. Filtering ground must still find that wall.
            var impact = new VehiclePhysicsState(new(30, 1.6f, 0), N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitX, Mathf.DegToRad(20)), new(0, 0, 20), N.Vector3.Zero);
            _body.Apply(impact);
            var wallHit = _body.Observe(Snapshot(impact));
            Check(wallHit.Contacts.Any(c => c.StaticObstacle && TombstoneGeometry.Contains(c.LocalPosition)), "Ground filtering lost a solid rear obstacle.");
            Check(wallHit.Contacts.All(c => !c.Terrain || !TombstoneGeometry.Contains(c.LocalPosition)), "Mixed obstacle contact reintroduced armor ground damage.");
            GD.Print($"Mounted shield ground passed: {cases} slope/pitch/roll/height comparisons; {previouslySnagged} old armor snags; {chassisContacts} retained chassis contacts; mixed ground/solid-obstacle protection.");
            _body.Free(); _ground.Free(); _obstacle.Free();
            GetTree().Quit();
        }
        catch (Exception error) { GD.PrintErr(error); GetTree().Quit(1); }
    }

    private static VehicleSnapshot Snapshot(VehiclePhysicsState pose) => new(1, 1, new(1, pose, true, false, 0, 0), new(1000, 1000, null, null), pose);
    private static void Check(bool condition, string message) { if (!condition) { throw new InvalidOperationException(message); } }
}
