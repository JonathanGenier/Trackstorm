using Godot;
using Trackstorm.Client.Items;

namespace Trackstorm.Client.Verification;

public sealed partial class MissilePresentationChecks
{
    private async Task InspectBody()
    {
        var model = new MissileVisual();
        _arenas[0].AddChild(model);
        model.GlobalPosition = _arenas[0].Bodies[1].GlobalPosition + Vector3.Up * 4;
        float minimumOverlap = float.MaxValue;
        for (int step = 0; step <= 100; step++)
        {
            float extension = step / 100f;
            model.SetDeployment(extension, extension);
            var chain = new[] {
                BodyBounds(model, "ForwardBody_Missile_Crimson"),
                BodyBounds(model, "ForwardSleeve"),
                BodyBounds(model, "MiddleSleeve"),
                BodyBounds(model, "RearSleeve"),
                BodyBounds(model, "TailExtension_Missile_Crimson") };
            for (int i = 1; i < chain.Length; i++)
            {
                float overlap = Math.Min(chain[i - 1].End.Z, chain[i].End.Z) - Math.Max(chain[i - 1].Position.Z, chain[i].Position.Z);
                minimumOverlap = Math.Min(minimumOverlap, overlap);
                Require(overlap >= .045f, $"Closed body overlap at extension {extension:F2}, join {i}: {overlap:F3}m", false);
            }
            if (step % 25 == 0)
            {
                _cameras[0].GlobalPosition = model.GlobalPosition + new Vector3(2.4f, .12f, 0);
                _cameras[0].LookAt(model.GlobalPosition);
                await Capture($"polish-body-{step:000}-side");
                _cameras[0].GlobalPosition = model.GlobalPosition + new Vector3(2.1f, .7f, 1.2f);
                _cameras[0].LookAt(model.GlobalPosition);
                await Capture($"polish-body-{step:000}-angle");
            }
        }
        Require(minimumOverlap >= .045f, $"All five body sections overlap across 101 extension poses; minimum {minimumOverlap:F3}m");
        model.SetDeployment(0, 0);
        var compact = BodyBounds(model, null);
        Require(compact.Position.X >= -.37f && compact.End.X <= .37f && compact.Position.Z >= -.58f && compact.End.Z <= .49f,
            "Compact missile remains inside original horizontal envelope");
        model.QueueFree();
        await Frames(2);
    }

    private static Aabb BodyBounds(Node3D model, string? name)
    {
        Node root = name is null ? model : model.FindChild(name, true, false);
        var meshes = Descendants(root).OfType<MeshInstance3D>().ToList();
        if (root is MeshInstance3D meshRoot) { meshes.Add(meshRoot); }
        bool first = true;
        Aabb result = default;
        foreach (var mesh in meshes)
        {
            Transform3D transform = model.GlobalTransform.AffineInverse() * mesh.GlobalTransform;
            Aabb bounds = transform * mesh.GetAabb();
            result = first ? bounds : result.Merge(bounds);
            first = false;
        }
        if (first) { throw new InvalidOperationException("Missing authored body geometry: " + name); }
        return result;
    }

    private async Task CaptureMountedBody(string label)
    {
        var model = Descendants(_arenas[0].Bodies[1]).OfType<MissileVisual>().Single();
        var chassis = _arenas[0].Bodies[1];
        _cameras[0].GlobalPosition = model.GlobalPosition + chassis.GlobalBasis * new Vector3(2.4f, .45f, .6f);
        _cameras[0].LookAt(model.GlobalPosition);
        await Capture(label);
    }
}
