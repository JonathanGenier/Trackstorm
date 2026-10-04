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
    /// <summary>Last damaging native collision tick, retained across recovery.</summary>
    public ulong? LastCollisionTick { get; init; }
    /// <summary>Authoritative installed wall position; zero while attached.</summary>
    public Vector3 Position { get; init; }
    /// <summary>Authoritative installed wall orientation; identity while attached.</summary>
    public Quaternion Orientation { get; init; } = Quaternion.Identity;
    /// <summary>Committed host rigid-body continuation, retained through authority changes.</summary>
    public Vector3 LinearVelocity { get; init; }
    /// <summary>World-space angular velocity in radians per second.</summary>
    public Vector3 AngularVelocity { get; init; }
    /// <summary>Dimensions captured on deployment; live tuning never resizes an installed collider.</summary>
    public Vector3 WallSize { get; init; } = new(6.6f, 2.5f, 0.6f);
    /// <summary>Physical mass captured on deployment in kilograms.</summary>
    public float WallMass { get; init; } = 250;
    /// <summary>Captured absolute simulation deadline; zero only while held or attached.</summary>
    public ulong ExpiresAtTick { get; init; }
    /// <summary>A strong impact released pitch/roll constraints; retained through recovery.</summary>
    public bool Tipping { get; init; }
    /// <summary>Whether inventory/life ownership still applies.</summary>
    public bool Attached => Stage != TombstoneStage.WorldWall;

    /// <summary>Rejects invalid live state without manufacturing or refilling health.</summary>
    public void Validate()
    {
        if (Id == 0 || Owner == 0 || !Enum.IsDefined(Stage) || !float.IsFinite(HP) || HP is <= 0 or > DefaultHP ||
            ((HP == DefaultHP) != (DamageSequence == 0)) || (LastCollisionTick.HasValue && DamageSequence == 0) ||
            (Attached ? ExpiresAtTick != 0 || Tipping : ExpiresAtTick == 0) ||
            (Attached ? Life == 0 || Token == 0 || Position != Vector3.Zero || Orientation != Quaternion.Identity : Life != 0 || Token != 0) ||
            !VehiclePhysicsState.IsFinite(LinearVelocity) || !VehiclePhysicsState.IsFinite(AngularVelocity) ||
            (Attached && (LinearVelocity != Vector3.Zero || AngularVelocity != Vector3.Zero)) ||
            !VehiclePhysicsState.IsFinite(WallSize) || WallSize.X is < 3 or > 12 || WallSize.Y is < 2 or > 6 || WallSize.Z is < 0.3f or > 2 ||
            !float.IsFinite(WallMass) || WallMass is < 50 or > 2000 ||
            !VehiclePhysicsState.IsFinite(Position) || !float.IsFinite(Orientation.X) || !float.IsFinite(Orientation.Y) ||
            !float.IsFinite(Orientation.Z) || !float.IsFinite(Orientation.W) || Math.Abs(Orientation.LengthSquared() - 1) > 0.001f)
        { throw new ArgumentException("Invalid Tombstone continuation."); }
    }
}
