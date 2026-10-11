using Godot;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Networking;

/// <summary>Body-owned world presentation of the existing session name and confirmed vehicle health.</summary>
internal sealed partial class RemoteVehicleTag : Node3D
{
    private const float Width = 1.8f;
    private readonly Label3D _name = new()
    {
        Name = "PlayerName",
        FontSize = 48,
        PixelSize = 0.008f,
        OutlineSize = 10,
        Position = new Vector3(0, 0.34f, 0),
        NoDepthTest = false,
    };
    private readonly MeshInstance3D _fill = Bar("HealthFill", new Color("e34d59"), 0.10f);

    /// <inheritdoc/>
    public override void _Ready()
    {
        TopLevel = true;
        AddChild(_name);
        AddChild(Bar("HealthBackground", new Color("17202b"), 0.16f));
        AddChild(_fill);
        _fill.Position = new Vector3(0, 0, 0.005f);
    }

    /// <summary>Updates render properties without retaining a second identity or health model.</summary>
    /// <param name="displayName">Current canonical session name, or null when unavailable/disconnected.</param>
    /// <param name="state">Latest accepted authoritative vehicle boundary.</param>
    /// <param name="position">Interpolated vehicle presentation position.</param>
    /// <param name="camera">Local viewport camera.</param>
    /// <param name="markerTop">Optional screen-space top of the assisted target glyph, including its glow.</param>
    internal void Present(string? displayName, VehicleSnapshot? state, Vector3 position, Camera3D camera, Vector2? markerTop = null)
    {
        Visible = displayName is not null && state?.CanInteract == true;
        _name.Text = displayName ?? string.Empty;
        float fraction = state is null ? 0 : Math.Clamp(state.Damage.CurrentHP / state.Damage.MaxHP, 0, 1);
        _fill.Visible = fraction > 0;
        _fill.Scale = new Vector3(Math.Max(fraction, 0.0001f), 1, 1);
        _fill.Position = new Vector3((fraction - 1) * Width / 2, 0, 0.005f);
        // Camera axes keep the complete tag upright in screen space, even when the car rolls.
        Vector3 anchor = position + Vector3.Up * 2.4f;
        float depth = -camera.ToLocal(anchor).Z;
        if (markerTop is { } top && depth > camera.Near)
        {
            // Move the complete stack together, retaining native depth and world-sized text.
            // The bar's bottom edge clears the glyph's full glow extent at every FOV/distance.
            Vector2 screen = camera.UnprojectPosition(anchor);
            float halfBar = screen.DistanceTo(camera.UnprojectPosition(anchor - camera.GlobalBasis.Y * .08f));
            float gap = 6 * camera.GetViewport().GetVisibleRect().Size.Y / 1080;
            screen.Y = Math.Min(screen.Y, top.Y - gap - halfBar);
            anchor = camera.ProjectPosition(screen, depth);
        }
        GlobalTransform = new Transform3D(camera.GlobalBasis.Orthonormalized(), anchor);
    }

    private static MeshInstance3D Bar(string name, Color color, float height) => new()
    {
        Name = name,
        Mesh = new QuadMesh { Size = new Vector2(Width, height) },
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = color,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        },
    };
}
