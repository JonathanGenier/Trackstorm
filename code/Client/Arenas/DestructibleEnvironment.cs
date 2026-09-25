using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Arenas;

namespace Trackstorm.Client.Arenas;

/// <summary>Reconstructable staged meshes and batched cover; authority contains no Godot resources.</summary>
internal sealed class DestructibleEnvironment
{
    private readonly Node3D _map;
    private readonly Node3D[] _rocks;
    private readonly (MultiMeshInstance3D Batch, int Index, Transform3D Pose)[] _plants;
    private readonly EnvironmentLayout _layout;
    private readonly Dictionary<int, (MeshInstance3D Visual, StaticBody3D Support)> _pieces = new();
    private readonly Mesh _pieceMesh;
    private readonly byte[] _stages;
    private readonly bool[] _cleared;
    private readonly ulong[] _clearAt;
    private readonly Dictionary<MultiMeshInstance3D, float[]> _buffers = new();
    private readonly System.Numerics.Vector3[] _offsets;
    private readonly float _pieceDiameter;

    internal DestructibleEnvironment(Node3D map)
    {
        _map = map;
        _rocks = Rocks(map);
        _layout = ReadLayout(map)!;
        _plants = Plants(map);
        _stages = new byte[_layout.Rocks.Count];
        _cleared = new bool[_plants.Length];
        _clearAt = new ulong[_plants.Length];
        _offsets = new System.Numerics.Vector3[_layout.Rocks.Count];
        foreach (var batch in _plants.Select(p => p.Batch).Distinct())
        {
            batch.Multimesh = (MultiMesh)batch.Multimesh.Duplicate();
            _buffers.Add(batch, batch.Multimesh.Buffer);
        }
        var template = GD.Load<PackedScene>("res://assets/environment/models/BoulderLow.glb").Instantiate<Node3D>();
        _pieceMesh = template.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().First().Mesh;
        _pieceDiameter = Diameter(_pieceMesh.GetAabb().Size);
        template.Free();
        for (int i = 0; i < _rocks.Length; i++)
        {
            var rock = _rocks[i];
            foreach (var collider in rock.FindChildren("*", "StaticBody3D", true, false).OfType<StaticBody3D>()) { collider.SetMeta("environment_rock", i * EnvironmentLayout.PiecesPerRock + 1); }
        }
    }

    internal static EnvironmentLayout? ReadLayout(Node3D map)
    {
        if (!map.HasNode("EnvironmentDressing")) { return null; }
        var rocks = Rocks(map);
        float Size(Node3D rock)
        {
            var mesh = rock.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().First();
            return Diameter(mesh.Mesh.GetAabb().Size * MapPose(mesh, map).Basis.Scale);
        }
        float smallest = rocks.Where(r => r.Name.ToString().StartsWith("BoulderLow_", StringComparison.Ordinal)).Select(Size).DefaultIfEmpty(0.5f).Min();
        return new(rocks.Select(r => VehicleBody.ToCore(MapPose(r, map).Origin)), Plants(map).Select(p => VehicleBody.ToCore((MapPose(p.Batch, map) * p.Pose).Origin)),
            rocks.Select(r => Math.Max(smallest, Size(r))), smallest);
    }

    private static float Diameter(Vector3 size) => MathF.Cbrt(Math.Abs(size.X * size.Y * size.Z));

    internal static ushort RockId(GodotObject? collider) => collider is Node node && node.HasMeta("environment_rock") ? (ushort)node.GetMeta("environment_rock").AsInt32() : (ushort)0;

    internal void Apply(EnvironmentSnapshot snapshot, bool reseed = false)
    {
        if (snapshot.Rocks.Count != _layout.Rocks.Count || snapshot.Plants.Count != _plants.Length) { throw new ArgumentException("Native environment layout mismatch."); }
        for (int i = 0; i < _layout.Rocks.Count; i++)
        {
            var state = snapshot.Rocks[i];
            var original = _rocks[i / EnvironmentLayout.PiecesPerRock];
            bool changed = _stages[i] != state.Stage || _offsets[i] != state.Offset || reseed;
            _offsets[i] = state.Offset;
            if (_stages[i] != state.Stage)
            {
                _stages[i] = state.Stage;
                if (i % EnvironmentLayout.PiecesPerRock == 0)
                {
                    original.Visible = state.Stage == 1;
                    foreach (var collider in original.FindChildren("*", "StaticBody3D", true, false).OfType<StaticBody3D>())
                    {
                        collider.CollisionLayer = state.Stage == 1 ? 1u : 0u;
                        collider.CollisionMask = state.Stage == 1 ? 1u : 0u;
                    }
                }
                if (state.Stage > 1 && !_pieces.ContainsKey(i))
                {
                    var visual = new MeshInstance3D { Name = $"BrokenRock{i}", Mesh = _pieceMesh };
                    var weaponTarget = new StaticBody3D { Name = "WeaponTarget", CollisionLayer = 16, CollisionMask = 0 };
                    Aabb bounds = _pieceMesh.GetAabb();
                    weaponTarget.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = bounds.Size }, Position = bounds.GetCenter() });
                    visual.AddChild(weaponTarget);
                    _map.AddChild(visual);
                    // A shallow wheel-only envelope yields to the chassis instead of applying rigid crash impulses.
                    var support = new StaticBody3D { Name = $"RockSupport{i}", CollisionLayer = 8, CollisionMask = 0 };
                    support.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 0.5f }, Scale = new(1, 0.24f, 1) });
                    _map.AddChild(support);
                    _pieces.Add(i, (visual, support));
                }
                if (_pieces.TryGetValue(i, out var piece))
                {
                    piece.Visual.Visible = state.Stage > 1;
                    piece.Visual.GetNode<StaticBody3D>("WeaponTarget").CollisionLayer = state.Stage > 1 ? 16u : 0u;
                    piece.Support.CollisionLayer = state.Stage > 1 ? 8u : 0u;
                }
            }
            if (state.Stage < 2 || !changed) { continue; }
            Vector3 position = MapPose(original, _map).Origin + VehicleBody.ToGodot(state.Offset);
            using var ray = PhysicsRayQueryParameters3D.Create(position + Vector3.Up * 3, position + Vector3.Down * 8, 1);
            var hit = _map.GetWorld3D().DirectSpaceState.IntersectRay(ray);
            // Practice cars share map layer 1; a rock must stay on terrain while
            // the car passes over it, rather than being projected onto its roof.
            var excluded = new Godot.Collections.Array<Rid>();
            while (hit.Count > 0 && hit["collider"].AsGodotObject() is VehicleBody vehicle)
            {
                excluded.Add(vehicle.GetRid());
                ray.Exclude = excluded;
                hit = _map.GetWorld3D().DirectSpaceState.IntersectRay(ray);
            }
            if (hit.Count > 0) { position.Y = hit["position"].AsVector3().Y; }
            float scale = _layout.Size(i, state.Stage) / _pieceDiameter;
            _pieces[i].Visual.Transform = new Transform3D(Basis.FromEuler(new Vector3(0, original.Rotation.Y + (i % EnvironmentLayout.PiecesPerRock) * 2.39996f, 0)).Scaled(Vector3.One * scale), position);
            _pieces[i].Support.Position = position;
        }
        var dirty = new HashSet<MultiMeshInstance3D>();
        for (int i = 0; i < _plants.Length; i++)
        {
            bool elapsed = _clearAt[i] != 0 && snapshot.Tick >= _clearAt[i];
            if (_cleared[i] == snapshot.Plants[i] && !reseed && !elapsed) { continue; }
            bool flatten = snapshot.Plants[i] && !_cleared[i] && !reseed;
            _cleared[i] = snapshot.Plants[i];
            var plant = _plants[i];
            // Flatten on the accepted boundary, then clear. Restore never replays flatten feedback.
            Transform3D pose = plant.Pose;
            _clearAt[i] = flatten ? snapshot.Tick + 9 : 0;
            if (_cleared[i])
            {
                pose.Basis = flatten ? pose.Basis.Scaled(new Vector3(1.15f, 0.02f, 1.15f)) : Basis.Identity.Scaled(Vector3.Zero);
            }
            WritePose(_buffers[plant.Batch], plant.Index, plant.Batch.Multimesh, pose);
            dirty.Add(plant.Batch);
        }
        // Bulk buffers retain exact CPU-authored transforms across zero-scale clearing/reset
        // and headless rendering; at most one upload per changed batch and boundary.
        foreach (var batch in dirty)
        {
            if (reseed)
            {
                var old = batch.Multimesh;
                batch.Multimesh = new MultiMesh
                {
                    TransformFormat = old.TransformFormat, UseColors = old.UseColors, UseCustomData = old.UseCustomData,
                    Mesh = old.Mesh, InstanceCount = old.InstanceCount, Buffer = _buffers[batch],
                };
            }
            else { batch.Multimesh.Buffer = _buffers[batch]; }
        }
    }

    private static void WritePose(float[] buffer, int index, MultiMesh mesh, Transform3D pose)
    {
        int offset = index * (12 + (mesh.UseColors ? 4 : 0) + (mesh.UseCustomData ? 4 : 0));
        buffer[offset] = pose.Basis.X.X; buffer[offset + 1] = pose.Basis.Y.X; buffer[offset + 2] = pose.Basis.Z.X; buffer[offset + 3] = pose.Origin.X;
        buffer[offset + 4] = pose.Basis.X.Y; buffer[offset + 5] = pose.Basis.Y.Y; buffer[offset + 6] = pose.Basis.Z.Y; buffer[offset + 7] = pose.Origin.Y;
        buffer[offset + 8] = pose.Basis.X.Z; buffer[offset + 9] = pose.Basis.Y.Z; buffer[offset + 10] = pose.Basis.Z.Z; buffer[offset + 11] = pose.Origin.Z;
    }

    private static Node3D[] Rocks(Node3D map) => map.GetNodeOrNull<Node3D>("EnvironmentDressing")?.FindChildren("*", "Node3D", true, false).OfType<Node3D>()
        .Where(n => new[] { "BoulderTall_", "BoulderLow_", "RockSlab_", "RockCluster_", "RockLedge_" }.Any(prefix => n.Name.ToString().StartsWith(prefix, StringComparison.Ordinal)))
        .OrderBy(n => map.GetPathTo(n).ToString(), StringComparer.Ordinal).ToArray() ?? [];

    private static (MultiMeshInstance3D Batch, int Index, Transform3D Pose)[] Plants(Node3D map) => map.GetNodeOrNull<Node3D>("EnvironmentDressing/GroundCover")?.GetChildren().OfType<MultiMeshInstance3D>()
        .OrderBy(n => n.Name.ToString(), StringComparer.Ordinal).SelectMany(b => Enumerable.Range(0, b.Multimesh.InstanceCount).Select(i => (b, i, b.Multimesh.GetInstanceTransform(i)))).ToArray() ?? [];

    private static Transform3D MapPose(Node3D node, Node3D map)
    {
        Transform3D pose = node.Transform;
        for (Node? parent = node.GetParent(); parent != map && parent is Node3D spatial; parent = parent.GetParent()) { pose = spatial.Transform * pose; }
        return pose;
    }
}
