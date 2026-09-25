using Godot;
using Trackstorm.Core.Settings;

namespace Trackstorm.Client.Vehicles;

/// <summary>One arena-owned fixed allocation and draw batch shared by all vehicles; no per-mark nodes or messages.</summary>
internal sealed partial class TireMarkBatch : Node3D
{
    internal const int MaximumCapacity = 65536;
    private readonly float[] _expires = new float[MaximumCapacity];
    private readonly Color[] _data = new Color[MaximumCapacity];
    private readonly MultiMeshInstance3D _view = new() { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    private MultiMesh _instances = null!;
    private ShaderMaterial _material = null!;
    private PlaneMesh _mesh = null!;
    private int _next;
    private int _count;
    private int _budget;
    private float _lastExpiry;
    private double _clock;
    internal Func<TireEffectSettings> SettingsSource { get; set; } = () => TireEffectSettings.Defaults;
    internal TireEffectSettings Settings => SettingsSource();
    internal int Submitted => _count;
    internal int Budget => _budget;
    internal long Written { get; private set; }
    internal long Recycled { get; private set; }
    internal float Now => (float)_clock;
    internal int Alive => _expires.Take(_count).Count(expiry => expiry > Now);
    internal Transform3D LastTransform => _instances.GetInstanceTransform((_next + _budget - 1) % _budget);
    internal Color LastColor => _instances.GetInstanceColor((_next + _budget - 1) % _budget);
    internal Color LastData => _instances.GetInstanceCustomData((_next + _budget - 1) % _budget);

    internal static TireMarkBatch For(Node arena)
    {
        if (arena.HasMeta("tire_mark_batch")) { return (TireMarkBatch)arena.GetMeta("tire_mark_batch").AsGodotObject(); }
        var batch = new TireMarkBatch { Name = "TireMarks" };
        batch.SettingsSource = arena switch
        {
            VehicleArena practice => () => practice.CameraSettings?.Current.TireEffects ?? TireEffectSettings.Defaults,
            Networking.NetworkVehicleArena network => () => network.CameraSettings?.Current.TireEffects ?? TireEffectSettings.Defaults,
            _ => () => TireEffectSettings.Defaults,
        };
        arena.SetMeta("tire_mark_batch", batch);
        // Vehicle _Ready can run while the arena is adding children. Reserve the owner immediately,
        // but defer tree attachment; other vehicles reuse this exact instance in the same frame.
        arena.CallDeferred(Node.MethodName.AddChild, batch);
        return batch;
    }

    public override void _Ready()
    {
        TopLevel = true;
        GlobalTransform = Transform3D.Identity;
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/effects/TireTrack.gdshader") };
        _mesh = new PlaneMesh { Size = Vector2.One, Material = _material };
        _instances = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, UseCustomData = true, Mesh = _mesh, InstanceCount = MaximumCapacity, VisibleInstanceCount = 0 };
        _view.Multimesh = _instances;
        AddChild(_view);
        _budget = (int)Settings["tire.budget"];
    }

    public override void _PhysicsProcess(double delta)
    {
        _clock += delta;
        if (_clock >= 3600)
        {
            // Keep shader timestamps precise across arbitrarily long sessions without growing history.
            _clock -= 3600;
            _lastExpiry -= 3600;
            for (int i = 0; i < _count; i++)
            {
                _expires[i] -= 3600;
                _data[i] = new Color(_data[i].R - 3600, _data[i].G, _data[i].B, _data[i].A);
                _instances.SetInstanceCustomData(i, _data[i]);
            }
        }
        int budget = (int)Settings["tire.budget"];
        if (budget != _budget)
        {
            // Budget edits are rare developer actions. Retire the existing batch instead of reallocating it.
            Clear();
            _budget = budget;
        }
        if (_count > 0 && Now >= _lastExpiry) { Clear(); }
        _material.SetShaderParameter("elapsed", Now);
        _material.SetShaderParameter("visibility_distance", Settings["tire.distance"]);
    }

    internal void Write(Transform3D transform, Color color, float duration, float fade, float roughness, bool soft)
    {
        if (!IsNodeReady()) { return; }
        if (_count == _budget) { Recycled++; }
        _instances.SetInstanceTransform(_next, transform);
        _instances.SetInstanceColor(_next, color);
        // Sign carries the tread identity; magnitude preserves the original material roughness.
        var data = new Color(Now, duration, fade, soft ? roughness : -roughness);
        _data[_next] = data;
        _expires[_next] = Now + duration;
        _instances.SetInstanceCustomData(_next, data);
        _lastExpiry = Math.Max(_lastExpiry, Now + duration);
        _next = (_next + 1) % _budget;
        _count = Math.Min(_budget, _count + 1);
        _instances.VisibleInstanceCount = _count;
        Written++;
    }

    private void Clear()
    {
        _count = _next = 0;
        _lastExpiry = 0;
        _instances.VisibleInstanceCount = 0;
    }

    public override void _ExitTree()
    {
        _view.Multimesh = null;
        _instances.Dispose();
        _mesh.Dispose();
        _material.Dispose();
    }
}
