using Godot;

namespace Trackstorm.Client.Vehicles;

/// <summary>Confirmed body flash; presentation never changes gameplay state.</summary>
internal sealed partial class VehicleFeedback : Node3D
{
    private StandardMaterial3D _material = null!;
    private Color _paint;
    private float _flashSeconds;
    private bool _destroyed;

    /// <summary>Number of confirmed visual feedback cues, for runtime checks.</summary>
    internal int CueCount { get; private set; }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        _flashSeconds = Math.Max(0, _flashSeconds - (float)delta);
        _material.AlbedoColor = _flashSeconds > 0 ? new Color("fff0bf") : _destroyed ? new Color("35383d") : _paint;
    }

    /// <summary>Receives the owned chassis material and its normal paint.</summary>
    /// <param name="material">Visual surface only.</param>
    /// <param name="paint">Undamaged body color.</param>
    internal void Initialize(StandardMaterial3D material, Color paint)
    {
        _material = material;
        _paint = paint;
    }

    /// <summary>Shows a confirmed visual effect; arena audio consumes the same authoritative boundary separately.</summary>
    /// <param name="destroyed">Current authoritative wreck state.</param>
    internal void Present(bool destroyed)
    {
        _destroyed = destroyed;
        _flashSeconds = 0.18f;
        CueCount++;
    }

    /// <summary>Clears presentation on explicit respawn.</summary>
    internal void Reset()
    {
        _destroyed = false;
        _flashSeconds = 0;
    }
}
