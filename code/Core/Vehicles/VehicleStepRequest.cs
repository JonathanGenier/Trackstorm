using Trackstorm.Core.Input;

namespace Trackstorm.Core.Vehicles;

/// <summary>One vehicle's input/observations and unprocessed intents for a global simulation tick.</summary>
public sealed class VehicleStepRequest
{
    /// <summary>Copies requests; no authoritative state changes occur during construction.</summary>
    /// <param name="vehicleId">Registered vehicle identity.</param>
    /// <param name="input">Input tagged with the next global tick.</param>
    /// <param name="observation">Solved native data.</param>
    /// <param name="effects">Combat requests for this tick.</param>
    /// <param name="reset">Explicit new-life pose, or null to continue the current life.</param>
    /// <param name="repair">Repair request for a living vehicle.</param>
    /// <param name="repairCause">Allowlisted source of repair.</param>
    /// <param name="oilSpin">Signed entry spin requested by item authority.</param>
    public VehicleStepRequest(ulong vehicleId, InputFrame input, VehicleObservation observation, IEnumerable<VehicleEffectRequest>? effects = null, VehiclePhysicsState? reset = null, float repair = 0, string repairCause = "repair", float oilSpin = 0)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (vehicleId == 0 || !float.IsFinite(repair))
        {
            throw new ArgumentException("Invalid vehicle identity or repair request.");
        }

        if (reset is VehiclePhysicsState physics)
        {
            _ = new VehiclePhysicsState(physics.Position, physics.Orientation, physics.LinearVelocity, physics.AngularVelocity);
        }

        VehicleEffectRequest[] copy = effects?.ToArray() ?? [];
        foreach (VehicleEffectRequest effect in copy)
        {
            ArgumentNullException.ThrowIfNull(effect);
        }

        if (!float.IsFinite(oilSpin) || Math.Abs(oilSpin) > 3) { throw new ArgumentException("Invalid oil spin."); }
        OilSpin = oilSpin;
        VehicleId = vehicleId;
        Input = input;
        Observation = observation;
        Effects = Array.AsReadOnly(copy);
        Reset = reset;
        Repair = repair;
        RepairCause = repairCause == "Wrench" ? "Wrench" : "repair";
    }

    /// <summary>Authoritative entry yaw impulse; zero continues the existing timer.</summary>
    public float OilSpin { get; }
    /// <summary>Registered identity.</summary>
    public ulong VehicleId { get; }
    /// <summary>Ordered logical input.</summary>
    public InputFrame Input { get; }
    /// <summary>Native observations.</summary>
    public VehicleObservation Observation { get; }
    /// <summary>Unprocessed effects.</summary>
    public IReadOnlyList<VehicleEffectRequest> Effects { get; }
    /// <summary>Explicit reset intent.</summary>
    public VehiclePhysicsState? Reset { get; }
    /// <summary>Repair intent.</summary>
    public float Repair { get; }
    /// <summary>Allowlisted source of the repair request.</summary>
    public string RepairCause { get; }
}
