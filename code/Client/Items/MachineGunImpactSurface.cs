using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Items;

/// <summary>Local cosmetic interpretation only; never changes a confirmed endpoint or damage.</summary>
internal readonly record struct MachineGunImpactSurface(Vector3 Normal, MachineGunImpactSurface.Kind Material)
{
    internal enum Kind { Neutral, Metal, Stone, Soil, Wet, Water }

    internal static MachineGunImpactSurface Sample(World3D world, Vector3 point, Vector3 incoming)
    {
        // Outcomes contain neither target identity nor material. Sample only near the
        // accepted endpoint, never cast a second weapon ray or invent a new contact.
        using var ray = PhysicsRayQueryParameters3D.Create(point - incoming * .6f, point + incoming * .6f, 3);
        using var hit = world.DirectSpaceState.IntersectRay(ray);
        if (hit.Count == 0) { return new(-incoming, Kind.Neutral); }
        var body = hit["collider"].AsGodotObject();
        Vector3 normal = hit["normal"].AsVector3();
        if (normal.LengthSquared() < .5f) { normal = -incoming; }
        return new(normal.Normalized(), Classify(body, point));
    }

    internal static Kind Classify(GodotObject? body, Vector3 point)
    {
        if (body is NetworkVehicleBody or VehicleBody) { return Kind.Metal; }
        var identity = SurfaceIdentityResolver.Resolve(body, point);
        if (identity is not null)
        {
            return identity switch
            {
                SurfaceIdentity.Asphalt or SurfaceIdentity.Concrete or SurfaceIdentity.Rock => Kind.Stone,
                SurfaceIdentity.Dirt or SurfaceIdentity.Grass => Kind.Soil,
                SurfaceIdentity.Mud or SurfaceIdentity.DeepMud => Kind.Wet,
                SurfaceIdentity.Water => Kind.Water,
                _ => Kind.Neutral,
            };
        }
        // Existing authored PBR metal on simple props can supply cosmetic identity.
        // Do not search arbitrary descendants or guess a material from a node name.
        if (body is Node node)
        {
            foreach (var mesh in node.GetChildren().OfType<MeshInstance3D>())
            {
                if (mesh.GetActiveMaterial(0) is BaseMaterial3D { Metallic: >= .5f }) { return Kind.Metal; }
            }
        }
        return Kind.Neutral;
    }
}
