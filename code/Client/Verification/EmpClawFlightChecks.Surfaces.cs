using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

public sealed partial class EmpClawFlightChecks
{
    private async Task SurfaceContacts()
    {
        _positions[1] = new(20, 105, 0); _positions[2] = new(0, 106, -12); _positions[3] = new(40, 105, 30);
        await Frames(3);
        var body = _arenas[0].Bodies[2];
        using var surface = new VehicleEmpSurface();
        float largestGap = 0;
        foreach (float x in new[] { -.9f, 0, .9f })
        foreach (float z in new[] { -2.3f, -1.8f, -1.3f, -.8f, 0, .6f })
        {
            var start = body.GlobalTransform * new Vector3(x, 3, z);
            var end = body.GlobalTransform * new Vector3(x, -1, z);
            var coarse = _arenas[0].Driver.RaycastWeapon!(1, VehicleBody.ToCore(start), VehicleBody.ToCore(end));
            var exact = _arenas[0].Driver.RaycastEmpClaw!(1, VehicleBody.ToCore(start), VehicleBody.ToCore(end), Host.World.State.Vehicles.Select(v => new VehicleStepRequest(v.VehicleId, default, new(v.Movement.Physics, N.Vector3.Zero))).ToArray());
            Check(exact?.Vehicle == 2 && coarse?.Vehicle == 2, "Both sweeps encounter the intended car.");
            float gap = (exact!.Fraction - coarse!.Fraction) * start.DistanceTo(end);
            largestGap = Math.Max(largestGap, gap);
            Vector3 local = body.GlobalTransform.AffineInverse() * start.Lerp(end, exact.Fraction);
            var triangle = surface.Intersect(local + Vector3.Up * .01f, local - Vector3.Up * .01f);
            Check(triangle.Count > 0 && triangle["position"].AsVector3().DistanceTo(local) < .001f, "Accepted contact lies on visible rigid geometry.");
            Record(new { scenario = "surface-vs-hull", x, z, gapMetres = gap, local });
        }
        Check(largestGap > .25f, "Fixture reproduces the old above-hood hull gap.");
        // Replay short flight segments into specific uneven surfaces. Only initial flight
        // conditions are seeded; production sweep, authority and replication create the claw.
        foreach (var spot in new[] { (Name: "hood-edge", X: .85f, Z: -1.8f), (Name: "windshield", X: -.7f, Z: -.8f), (Name: "moving-hood", X: .6f, Z: -1.8f) })
        {
            var hit = surface.Intersect(new(spot.X, 3, spot.Z), new(spot.X, -1, spot.Z));
            Vector3 local = hit["position"].AsVector3();
            Vector3 normal = Vector3.Up;
            if (spot.Name == "moving-hood")
            {
                _positions.Remove(2);
                var boundary = Host.World.State;
                Host.World.Restore(new(boundary.Tick, boundary.LastInput, boundary.Vehicles.Select(v =>
                {
                    if (v.VehicleId != 2) { return v; }
                    var motion = new VehiclePhysicsState(v.Movement.Physics.Position, v.Movement.Physics.Orientation, new(8, 3, -12), new(.8f, 1.5f, .6f));
                    return new VehicleSnapshot(v.VehicleId, v.LifeId, new(boundary.Tick, motion, false, false, 0, 0), v.Damage, motion);
                }), boundary.Match));
            }
            ulong id = Host.Items.TokenHighWater + 1;
            var pose = body.GlobalTransform;
            var flight = new MissileState(id, 1, VehicleBody.ToCore(pose * (local + normal * .3f)), VehicleBody.ToCore(pose.Basis * -normal * 100), 480)
            { EmpClaw = new(Host.World.GetVehicle(1).LifeId, 2, Host.World.GetVehicle(2).LifeId, Host.World.State.Tick + 480) };
            var publication = new ItemPublication(Host.Items.Revision + 1, Host.Snapshot(), Host.Items.Slots, [flight], [],
                Host.Spawns?.States, Host.Items.Patches, Host.Items.OilContacts, Host.Spawns?.Balances, Host.Items.Mines,
                Host.Items.Shields, Host.Items.DiscardRevision, Host.Items.EmpAttachments);
            Host.Items.Restore(publication, publication.Revision, id);
            await Until(() => Host.Items.EmpAttachments.Any(c => c.Id == id), "Uneven surface flight makes a real attachment.");
            var claw = Host.Items.EmpAttachments.Single(c => c.Id == id);
            Vector3 point = VehicleBody.ToGodot(claw.LocalPosition), outward = VehicleBody.ToGodot(claw.LocalNormal);
            var actual = surface.Intersect(point + outward * .02f, point - outward * .02f);
            Check(actual.Count > 0 && actual["position"].AsVector3().DistanceTo(point) < .002f, "Authoritative local anchor is on the contacted triangle.");
            await Until(() => _arenas.All(a => a.Driver.ItemState!.EmpAttachments.Any(c => c.Id == id)), "Surface contact replicated.");
            _positions[2] = new(0, 106, -12);
            await Frames(30);
            await Capture(spot.Name, body.GlobalPosition + new Vector3(5, 3, -6), body.GlobalPosition + new Vector3(0, .5f, -1.4f));
            Record(new { scenario = spot.Name, claw.LocalPosition, claw.LocalNormal });
            Host.Items.RemoveEmpAttachment(id);
            await Until(() => Enumerable.Range(0, 3).All(p => Attachments(p).Views.Count == 0 && Attachments(p).ReleasedCount == 0), "Surface fixture cleanup.");
        }
        GD.Print($"EMP authored surface checks: maximum old hull gap {largestGap:F4} m; 18 grid contacts and three native attachment flights, including moving/rotating impact.");
    }
}
