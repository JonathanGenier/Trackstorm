using System.Diagnostics;
using System.Text.Json;
using Godot;
using Trackstorm.Client.Vehicles;

namespace Trackstorm.Client.Verification;

public sealed partial class TunnelScrapeChecks
{
    private async Task VerifyQueries()
    {
        var owner = new StaticBody3D { CollisionLayer = 2, CollisionMask = 1 };
        var chassis = VehicleVisual.CreateCollision(); owner.AddChild(chassis); AddChild(owner);
        using var query = new VehicleMotionQuery(owner, chassis);
        using var parameters = new PhysicsTestMotionParameters3D { Margin = 0.005f, MaxCollisions = 4, RecoveryAsCollision = true };
        using var reference = new PhysicsTestMotionResult3D();
        using var actual = new PhysicsTestMotionResult3D();
        using var probes = JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://scenes/verification/tunnel_scrape_probes.json"));
        var timings = new List<object>();
        double originalTotal = 0, boundedTotal = 0;
        foreach (var probe in probes.RootElement.EnumerateArray())
        {
            var p = probe.GetProperty("position"); var q = probe.GetProperty("orientation"); var m = probe.GetProperty("motion");
            parameters.From = new Transform3D(new Basis(new Quaternion(q[0].GetSingle(), q[1].GetSingle(), q[2].GetSingle(), q[3].GetSingle())), new(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle()));
            parameters.Motion = new(m[0].GetSingle(), m[1].GetSingle(), m[2].GetSingle());
            owner.GlobalTransform = parameters.From;
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            bool expectedHit = PhysicsServer3D.BodyTestMotion(owner.GetRid(), parameters, reference);
            bool actualHit = query.Test(parameters, actual);
            Check(actualHit == expectedHit && actual.GetTravel().DistanceTo(reference.GetTravel()) < 0.005f && actual.GetRemainder().DistanceTo(reference.GetRemainder()) < 0.005f,
                "captured tilted scrape retains native collision/travel within 5 mm");
            var original = new List<double>(); var bounded = new List<double>();
            for (int batch = 0; batch < 5; batch++)
            {
                // Alternate ordering; medians of batches reduce scheduling/GC noise.
                foreach (bool legacy in batch % 2 == 0 ? new[] { true, false } : new[] { false, true })
                {
                    long start = Stopwatch.GetTimestamp();
                    for (int repeat = 0; repeat < 8; repeat++)
                    {
                        if (legacy) { PhysicsServer3D.BodyTestMotion(owner.GetRid(), parameters, reference); }
                        else { query.Test(parameters, actual); }
                    }
                    (legacy ? original : bounded).Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds / 8);
                }
            }
            double before = original.Order().ElementAt(2), after = bounded.Order().ElementAt(2);
            originalTotal += before; boundedTotal += after;
            timings.Add(new { position = p, originalMs = before, boundedMs = after });
        }
        Check(boundedTotal < originalTotal * 0.8, "captured sweep corpus reduces native query cost by at least 20 percent");
        GD.Print($"QUERY corpus: original={originalTotal:F3}ms bounded={boundedTotal:F3}ms ratio={boundedTotal / originalTotal:F3}");
        System.IO.File.WriteAllText(System.IO.Path.Combine(_directory, "query-comparison.json"), JsonSerializer.Serialize(timings));
        query.Dispose();
        owner.QueueFree(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        // Use an isolated world to prove the identity-positioned query has no
        // phantom collision and shares mask, self-exclusion and shield behavior.
        var viewport = new SubViewport { OwnWorld3D = true }; AddChild(viewport);
        var root = new Node3D(); viewport.AddChild(root);
        owner = new StaticBody3D { CollisionLayer = 2, CollisionMask = 1 };
        chassis = VehicleVisual.CreateCollision(); owner.AddChild(chassis);
        var shield = new CollisionShape3D { Shape = new BoxShape3D { Size = new(2, 1, 1) }, Position = new(0, 0, 3), Disabled = true };
        owner.AddChild(shield); root.AddChild(owner);
        var wall = new StaticBody3D { Position = new(0, 0, 8) };
        wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(20, 20, 1) } }); root.AddChild(wall);
        using var isolated = new VehicleMotionQuery(owner, chassis, shield);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        parameters.From = Transform3D.Identity; parameters.Motion = new(0, 0, 10);
        Check(isolated.Test(parameters, actual), "query reaches solid obstacle");
        float bare = actual.GetTravel().Z;
        shield.Disabled = false;
        Check(isolated.Test(parameters, actual) && actual.GetTravel().Z < bare - 0.5f, "enabled rear shape participates in native motion");
        shield.Disabled = true;
        Check(isolated.Test(parameters, actual) && Math.Abs(actual.GetTravel().Z - bare) < 0.001f, "disabled rear shape is removed from motion");
        owner.CollisionMask = 0;
        Check(!isolated.Test(parameters, actual), "query follows owner collision mask");
        owner.CollisionMask = 3; parameters.Motion = new(3, 0, 0);
        Check(!isolated.Test(parameters, actual), "query excludes its real owner and other query bodies");
        owner.Position = new(100, 0, 0);
        var falling = new RigidBody3D { Position = new(0, 3, 0), CollisionLayer = 1, CollisionMask = 3 };
        falling.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 0.3f } }); root.AddChild(falling);
        for (int frame = 0; frame < 100; frame++) { await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
        Check(falling.Position.Y < -3, "native rigid body falls through inactive query location without phantom support");
        isolated.Dispose(); isolated.Dispose();
        Check(chassis.Shape.GetRid().IsValid && owner.GetRid().IsValid, "disposing query preserves owner's native resources");
        viewport.QueueFree(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void Check(bool condition, string message)
    {
        if (condition) { return; }
        _failures++; GD.Print("FAIL: " + message);
    }
}
