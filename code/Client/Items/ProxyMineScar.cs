using Godot;

namespace Trackstorm.Client.Items;

/// <summary>Cosmetic terrain mark owned by the local match scene, with no collision or gameplay state.</summary>
internal sealed partial class ProxyMineScar : MeshInstance3D
{
    internal const string ShaderPath = "res://assets/effects/ProxyMineScar.gdshader";
    internal const float Duration = 60f;

    private ShaderMaterial _material = null!;
    private float _age;

    public override void _Ready()
    {
        Mesh = new PlaneMesh { Size = new Vector2(3.8f, 3.8f) };
        CastShadow = ShadowCastingSetting.Off;
        _material = new ShaderMaterial
        {
            Shader = Networking.MatchResourceLoader.LoadResource<Shader>(ShaderPath),
        };
        MaterialOverride = _material;
    }

    public override void _Process(double delta)
    {
        _age += (float)delta;
        if (_age >= Duration) { QueueFree(); return; }
        // Hold most of the minute, then fade without leaving a permanent terrain change.
        float opacity = 1 - Mathf.SmoothStep(42f, Duration, _age);
        _material.SetShaderParameter("opacity", opacity);
    }

    public override void _ExitTree()
    {
        MaterialOverride = null;
        _material.Dispose();
    }
}
