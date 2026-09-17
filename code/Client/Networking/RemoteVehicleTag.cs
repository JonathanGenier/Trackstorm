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
    internal void Present(string? displayName, VehicleSnapshot? state, Vector3 position, Camera3D camera)
    {
        Visible = displayName is not null && state?.CanInteract == true;
        _name.Text = displayName ?? string.Empty;
        float fraction = state is null ? 0 : Math.Clamp(state.Damage.CurrentHP / state.Damage.MaxHP, 0, 1);
        _fill.Visible = fraction > 0;
        _fill.Scale = new Vector3(Math.Max(fraction, 0.0001f), 1, 1);
        _fill.Position = new Vector3((fraction - 1) * Width / 2, 0, 0.005f);
        // Camera axes keep the complete tag upright in screen space, even when the car rolls.
        GlobalTransform = new Transform3D(camera.GlobalBasis.Orthonormalized(), position + (Vector3.Up * 2.4f));
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
