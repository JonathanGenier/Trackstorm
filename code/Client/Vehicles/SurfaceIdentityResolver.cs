using Godot;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Vehicles;

/// <summary>Reads collision-authored identities and local X/Z material fields without map or handling rules.</summary>
internal static class SurfaceIdentityResolver
{
    private static readonly Dictionary<string, Image> Fields = new(StringComparer.Ordinal);

    internal static SurfaceIdentity? Resolve(GodotObject? collider, Vector3 worldPosition)
    {
        if (collider is not Node3D node) { return null; }
        if (node.HasMeta("surface_field"))
        {
            string path = node.GetMeta("surface_field").AsString();
            if (!Fields.TryGetValue(path, out var image))
            {
                image = GD.Load<Texture2D>(path).GetImage();
                if (image.IsCompressed()) { image.Decompress(); }
                Fields.Add(path, image);
            }

            Vector4 bounds = node.GetMeta("surface_bounds").AsVector4();
            Vector3 point = node.ToLocal(worldPosition);
            // Same normalized texture coordinates and bilinear texel centers as the shader.
            float x = Math.Clamp(((point.X - bounds.X) / bounds.Z * image.GetWidth()) - 0.5f, 0, image.GetWidth() - 1);
            float y = Math.Clamp(((point.Z - bounds.Y) / bounds.W * image.GetHeight()) - 0.5f, 0, image.GetHeight() - 1);
            int ix = (int)x, iy = (int)y;
            int nx = Math.Min(ix + 1, image.GetWidth() - 1), ny = Math.Min(iy + 1, image.GetHeight() - 1);
            Color c = image.GetPixel(ix, iy).Lerp(image.GetPixel(nx, iy), x - ix).Lerp(image.GetPixel(ix, ny).Lerp(image.GetPixel(nx, ny), x - ix), y - iy);
            return SurfaceIdentityField.Resolve(c.R, c.G, c.B, c.A);
        }

        if (node.HasMeta("surface_identity") && Enum.TryParse<SurfaceIdentity>(node.GetMeta("surface_identity").AsString(), out var identity) && Enum.IsDefined(identity)) { return identity; }
        return null;
    }

    internal static string Describe(SurfaceIdentity? identity) => identity is SurfaceIdentity.DeepMud ? "Deep Mud" : identity?.ToString() ?? "Unavailable (airborne or unauthored contact)";
}
