using Godot;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Vehicles;

/// <summary>Bounded, silent Kenney destruction bursts; observes confirmed lives and never mutates gameplay.</summary>
internal sealed partial class VehicleDestructionEffects : Node3D
{
    private readonly Dictionary<ulong, ulong> _deaths = new();
    private readonly List<(Node3D Node, float Remaining)> _bursts = new();

    /// <summary>Number of confirmed death bursts submitted to the renderer.</summary>
    internal int BurstCount { get; private set; }
    /// <summary>Currently owned transient bursts, exposed for native cleanup verification.</summary>
    internal int ActiveBursts => _bursts.Count;

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        for (int i = _bursts.Count - 1; i >= 0; i--)
        {
            var burst = _bursts[i];
            float remaining = burst.Remaining - (float)delta;
            if (remaining <= 0)
            {
                burst.Node.QueueFree();
                _bursts.RemoveAt(i);
            }
            else
            {
                _bursts[i] = (burst.Node, remaining);
            }
        }
    }

    /// <summary>Consumes committed boundaries once per vehicle life, independently of movement packet arrival.</summary>
    /// <param name="vehicles">Complete confirmed roster.</param>
    internal void Apply(IEnumerable<VehicleSnapshot> vehicles)
    {
        VehicleSnapshot[] states = vehicles.ToArray();
        foreach (ulong id in _deaths.Keys.Except(states.Select(state => state.VehicleId)).ToArray())
        {
            _deaths.Remove(id);
        }

        foreach (VehicleSnapshot state in states.Where(state => state.Lifecycle == VehicleLifecycle.Dead))
        {
            if (_deaths.GetValueOrDefault(state.VehicleId) >= state.LifeId)
            {
                continue;
            }

            _deaths[state.VehicleId] = state.LifeId;
            // Also bound queued visuals during catch-up after a long reliable-delivery stall.
            if (_bursts.Count == 24)
            {
                _bursts[0].Node.QueueFree();
                _bursts.RemoveAt(0);
            }

            var burst = new Node3D { Position = VehicleBody.ToGodot(state.Movement.Physics.Position) };
            AddChild(burst);
            burst.AddChild(Items.ItemPresentation.Particles("fire_01", true, 0.35f));
            burst.AddChild(Items.ItemPresentation.Particles("smoke_01", true, 1.3f));
            burst.AddChild(Items.ItemPresentation.Particles("spark_01", true, 0.65f));
            _bursts.Add((burst, 1.6f));
            BurstCount++;
        }
    }

}
