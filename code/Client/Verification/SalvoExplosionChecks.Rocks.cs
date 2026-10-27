using Godot;
using Trackstorm.Client.Arenas;
using Trackstorm.Client.Items;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Arenas;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

public sealed partial class SalvoExplosionChecks
{
    private async Task RockDamage()
    {
        // Imported native geometry and the real host environment transaction. The
        // broader destruction harness separately covers replicated environment state.
        var fixture = new Node3D(); _arenas[0].AddChild(fixture);
        var dressing = new Node3D { Name = "EnvironmentDressing" }; fixture.AddChild(dressing);
        var tall = GD.Load<PackedScene>("res://assets/environment/models/BoulderTall.glb").Instantiate<Node3D>();
        tall.Name = "BoulderTall_000"; tall.Position = new(0, 100, 8); tall.Scale = Vector3.One * 4;
        tall.RotationDegrees = new(0, 35, 0); dressing.AddChild(tall);
        var small = GD.Load<PackedScene>("res://assets/environment/models/BoulderLow.glb").Instantiate<Node3D>();
        small.Name = "BoulderLow_001"; small.Position = new(10, 100, 8); small.Scale = Vector3.One * .25f; dressing.AddChild(small);
        var layout = DestructibleEnvironment.ReadLayout(fixture)!;
        int root = Array.IndexOf(layout.Rocks.ToArray(), VehicleBody.ToCore(tall.Position));
        var bounds = layout.Bounds![root / EnvironmentLayout.PiecesPerRock];
        var arena = new ArenaConfiguration(new(-200, 0, -200), new(200, 200, 200),
            Enumerable.Range(0, 8).Select(i => new ArenaSpawn("player" + i, new(-90 + i * 10, 101.2f, 80), 0)),
            [], [SurfaceType.Asphalt], layout);
        var source = new HostVehicleSession(214, arena: arena, matchConfiguration: new() { MinimumPlayers = 1, CountdownTicks = 1 });
        var view = new DestructibleEnvironment(fixture);
        var effects = new ItemPresentation(); fixture.AddChild(effects);
        var impacts = new List<ItemEvent>();
        void Step()
        {
            source.Step(default, v => new(v.Movement.Physics, N.Vector3.UnitY), collide: _arenas[0].Driver.CollideMissile);
            impacts.AddRange(source.Items.Events.Where(e => e.Impact));
            view.Apply(source.Environment!.Snapshot(source.SessionId, source.World.State.Tick));
            effects.Apply(new(source.Items.Revision + 1, source.Snapshot(), [], source.Items.Missiles, source.Items.Events));
        }
        Step(); Step(); await Frames(2);
        // Cross the imported mesh, not just its enclosing box's highest corner.
        N.Vector3 target = bounds.Center;
        var arc = new SalvoFlight(target + N.Vector3.UnitY * (bounds.Size.Y + 8), target, 1, 2, 1, 1);
        var round = new MissileState(1, 1, arc.At(1), arc.VelocityAt(1), SalvoFlight.LifetimeTicks - 1) { Arc = arc };
        source.Items.Restore(new(source.Items.Revision + 1, source.Snapshot(), [], [round], []), source.Items.Revision + 1, 1);
        Focus(VehicleBody.ToGodot(bounds.Center), new(9, 6, 10));
        await Frames(1); Step();
        Check(impacts.Count == 1 && impacts[0].Item == HeldItem.Salvo && source.Items.Missiles.Count == 0,
            "Imported tall rock receives one real swept Salvo impact");
        Check(N.Vector3.Distance(impacts[0].Position, layout.Rocks[root]) > source.Items.Configuration.SalvoBlastRadius,
            "Native impact is outside ground-origin radius, exercising authored rock bounds");
        var state = source.Environment!.Snapshot(source.SessionId, source.World.State.Tick);
        Check(state.Rocks[root].Stage == 2 && state.Rocks.Skip(root).Take(4).Count(r => r.Stage > 0) == 2,
            "One native Salvo explosion fractures imported rock once into two pieces");
        Check(!tall.Visible && fixture.GetNodeOrNull<MeshInstance3D>("BrokenRock" + root) is { Visible: true },
            "Authoritative fracture replaces intact native geometry with visible pieces");
        await Capture("07-native-rock-fracture");
        for (int i = 0; i < 30; i++) { await Frames(1); Step(); }
        Check(impacts.Count == 1 && source.Environment.Snapshot(source.SessionId, source.World.State.Tick).Rocks[root].Stage == 2,
            "Three rendered bursts never repeat the rock destruction transaction");
        var piece = fixture.GetNode<MeshInstance3D>("BrokenRock" + root);
        var box = piece.Mesh.GetAabb();
        target = VehicleBody.ToCore(piece.GlobalTransform * (box.Position + box.Size * new Vector3(.96f, .5f, .96f)));
        arc = new SalvoFlight(target + N.Vector3.UnitY * 12, target, 1, 2, 1, 1);
        round = new MissileState(2, 1, arc.At(1), arc.VelocityAt(1), SalvoFlight.LifetimeTicks - 1) { Arc = arc };
        source.Items.Restore(new(source.Items.Revision + 1, source.Snapshot(), [], [round], []), source.Items.Revision + 1, 2);
        await Frames(1); Step();
        Check(impacts.Count == 2 && source.Items.Missiles.Count == 0 &&
            source.Environment.Snapshot(source.SessionId, source.World.State.Tick).Rocks[root].Stage == 3,
            "Native Salvo impact on a large rotated debris edge advances destruction exactly one stage");
        await Capture("08-native-debris-edge");
        fixture.QueueFree(); await Frames(2);
    }
}
