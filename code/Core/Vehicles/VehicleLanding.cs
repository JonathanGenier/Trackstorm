using System.Numerics;

namespace Trackstorm.Core.Vehicles;

/// <summary>Terrain-relative landing classification, independent of heading and travel direction.</summary>
internal static class VehicleLanding
{
    internal static bool Landable(Quaternion orientation, Vector3 normal)
    {
        if (normal == Vector3.Zero) { return false; }
        Vector3 local = Vector3.Transform(normal, Quaternion.Conjugate(orientation));
        // Separate roll/pitch envelopes preserve sideways landings on banks.
        return local.Y > 0 && Math.Abs(MathF.Atan2(local.X, local.Y)) <= 50 * MathF.PI / 180 &&
            Math.Abs(MathF.Atan2(local.Z, local.Y)) <= 40 * MathF.PI / 180;
    }

    internal static bool SafeContact(VehicleObservation observed, VehicleContact contact) =>
        contact.Terrain && contact.OtherVehicleId == 0 && contact.LocalPosition.Y < -0.15f &&
        Landable(observed.Physics.Orientation, contact.Normal);

    internal static LandingState Step(LandingState previous, VehicleObservation observed)
    {
        bool supported = Landable(observed.Physics.Orientation, observed.TerrainSupport);
        bool terrain = observed.Contacts.Any(contact => contact.Terrain && contact.OtherVehicleId == 0);
        bool unsafeContact = observed.Contacts.Any(contact => contact.Terrain && !SafeContact(observed, contact));
        byte unsupported = supported ? (byte)0 : (byte)Math.Min(3, previous.UnsupportedTicks + 1);
        Vector3 angular = Vector3.Transform(observed.Physics.AngularVelocity, Quaternion.Conjugate(observed.Physics.Orientation));
        byte stable = supported && new Vector2(angular.X, angular.Z).Length() < 1.5f ? (byte)Math.Min(6, previous.StableTicks + 1) : (byte)0;
        LandingPhase phase = previous.Phase;
        byte remaining = previous.RecoveryTicks;
        if (phase == LandingPhase.Crash)
        {
            return stable == 6 && !unsafeContact ? default : new(phase, unsupported, 0, stable);
        }

        if (phase is LandingPhase.Recovery or LandingPhase.Recovered)
        {
            if (unsafeContact || (observed.TerrainSupport != Vector3.Zero && !supported))
            {
                return new(LandingPhase.Crash, unsupported, 0, 0);
            }
            remaining = (byte)Math.Max(0, remaining - 1);
            if (remaining == 0) { return supported ? default : new(LandingPhase.Airborne, unsupported, 0, 0); }
            return new(stable == 6 ? LandingPhase.Recovered : phase, unsupported, remaining, stable);
        }

        if (phase == LandingPhase.Airborne)
        {
            if (terrain || supported)
            {
                return unsafeContact ? new(LandingPhase.Crash, unsupported, 0, 0) : new(LandingPhase.Recovery, unsupported, 60, stable);
            }
        }
        else if (unsupported == 3 && !terrain)
        {
            phase = LandingPhase.Airborne;
        }
        return new(phase, unsupported, 0, stable);
    }

    internal static bool Forgives(LandingState state, VehicleObservation observed, VehicleContact contact) =>
        state.Phase is LandingPhase.Recovery or LandingPhase.Recovered && SafeContact(observed, contact);
}
