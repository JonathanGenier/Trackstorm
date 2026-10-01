using System.Numerics;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

/// <summary>One persistent identity and health pool; attachment capability may change at a retained respawn.</summary>
public sealed record TombstoneState(ulong Id, ulong Owner, ulong Life, ulong Token, TombstoneStage Stage, float HP)
{
    /// <summary>Health captured once on acquisition; vehicle tuning never changes it.</summary>
    public const float DefaultHP = 1000;
    /// <summary>Last accepted host damage observation, retained through recovery to reject duplicates.</summary>
    public ulong DamageSequence { get; init; }
    /// <summary>Authoritative installed wall position; zero while attached.</summary>
    public Vector3 Position { get; init; }
    /// <summary>Authoritative installed wall orientation; identity while attached.</summary>
    public Quaternion Orientation { get; init; } = Quaternion.Identity;
    /// <summary>Whether inventory/life ownership still applies.</summary>
    public bool Attached => Stage != TombstoneStage.WorldWall;

    /// <summary>Rejects invalid live state without manufacturing or refilling health.</summary>
    public void Validate()
    {
        if (Id == 0 || Owner == 0 || !Enum.IsDefined(Stage) || !float.IsFinite(HP) || HP is <= 0 or > DefaultHP ||
            ((HP == DefaultHP) != (DamageSequence == 0)) ||
            (Attached ? Life == 0 || Token == 0 || Position != Vector3.Zero || Orientation != Quaternion.Identity : Life != 0 || Token != 0) ||
            !VehiclePhysicsState.IsFinite(Position) || !float.IsFinite(Orientation.X) || !float.IsFinite(Orientation.Y) ||
            !float.IsFinite(Orientation.Z) || !float.IsFinite(Orientation.W) || Math.Abs(Orientation.LengthSquared() - 1) > 0.001f)
        { throw new ArgumentException("Invalid Tombstone continuation."); }
    }
}
