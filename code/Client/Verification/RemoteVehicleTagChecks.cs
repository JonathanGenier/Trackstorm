using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Verification;

/// <summary>Native assertions shared by production lobby and reconnect scenarios.</summary>
internal static class RemoteVehicleTagChecks
{
    /// <summary>Checks current production tags against accepted roster and vehicle state.</summary>
    /// <param name="arena">Native peer arena.</param>
    /// <param name="lobby">That peer's identity source.</param>
    internal static void Verify(NetworkVehicleArena arena, LobbyNetworkDriver lobby)
    {
        arena._Process(0);
        var camera = arena.GetNode<Camera3D>("ChaseCamera");
        foreach (var pair in arena.Bodies)
        {
            var tags = pair.Value.GetChildren().OfType<RemoteVehicleTag>().ToArray();
            Require(tags.Length == (pair.Key == arena.Driver.LocalVehicleId ? 0 : 1), "Exactly one remote tag and no local tag.");
            if (tags.Length == 0)
            {
                continue;
            }

            var tag = tags[0];
            var player = lobby.State!.Players.SingleOrDefault(player => player.Id == pair.Key);
            var state = arena.Driver.Latest!.Vehicles.Single(vehicle => vehicle.State.VehicleId == pair.Key).State;
            Require(tag.Visible == (arena.Driver.IsActive && player?.Connected == true && state.CanInteract), "Visibility follows existing participation and connection.");
            if (tag.Visible)
            {
                Require(tag.GetNode<Label3D>("PlayerName").Text == player!.Name, "Canonical name matches vehicle identity.");
            }

            Require(tag.GlobalPosition.IsEqualApprox(pair.Value.VisualPosition + (Vector3.Up * 2.4f)), "Tag follows the rendered vehicle.");
            Require(tag.GlobalBasis.IsEqualApprox(camera.GlobalBasis.Orthonormalized()), "Complete tag faces local camera upright.");
            VerifyFill(tag, state.Damage.CurrentHP / state.Damage.MaxHP);
        }
    }

    /// <summary>Exercises render boundaries with explicit immutable snapshot fixtures.</summary>
    /// <param name="arena">Arena containing a remote presentation.</param>
    internal static void VerifyBoundaries(NetworkVehicleArena arena)
    {
        var body = arena.Bodies.First(pair => pair.Key != arena.Driver.LocalVehicleId).Value;
        var tag = body.GetNode<RemoteVehicleTag>("PlayerTag");
        var camera = arena.GetNode<Camera3D>("ChaseCamera");
        var originalCamera = camera.GlobalTransform;
        var originalBody = body.GlobalTransform;
        ulong instance = tag.GetInstanceId();
        var state = arena.Driver.Latest!.Vehicles.First(vehicle => vehicle.State.VehicleId == body.VehicleId).State;
        foreach (float fraction in new[] { 1f, 0.25f, 0.75f, 0f, 1f })
        {
            var boundary = new VehicleSnapshot(state.VehicleId, fraction == 1 ? state.LifeId + 1 : state.LifeId, state.Movement, new VehicleDamageState(state.Damage.MaxHP, state.Damage.MaxHP * fraction, null, null), state.ObservedPhysics);
            body.Rotation = new Vector3(1.2f, 2.3f, 2.8f);
            camera.Rotation = new Vector3(-0.5f, fraction * 5, 0.2f);
            tag.Present("Élodie 車 Player", boundary, new Vector3(5, 3, -7), camera);
            Require(tag.GetInstanceId() == instance, "Damage, healing and respawn reuse the tag.");
            Require(tag.Visible == (fraction > 0), "Death hides the tag and respawn restores it.");
            Require(tag.GlobalBasis.IsEqualApprox(camera.GlobalBasis.Orthonormalized()), "Vehicle pitch/roll cannot rotate the tag away from camera.");
            Require(tag.GetNode<Label3D>("PlayerName").Position.Y > tag.GetNode<MeshInstance3D>("HealthFill").Position.Y, "HP is below the name.");
            Require(!tag.GetNode<Label3D>("PlayerName").NoDepthTest && !((StandardMaterial3D)tag.GetNode<MeshInstance3D>("HealthFill").MaterialOverride).NoDepthTest, "Tags retain world occlusion.");
            VerifyFill(tag, fraction);
        }

        camera.GlobalTransform = originalCamera;
        body.GlobalTransform = originalBody;
        GD.Print("Remote tag native boundaries passed: full/damaged/healed/zero/respawn, Unicode, rotations, depth testing and stable node identity (synthetic snapshots).");
    }

    private static void VerifyFill(RemoteVehicleTag tag, float fraction)
    {
        var fill = tag.GetNode<MeshInstance3D>("HealthFill");
        Require(fill.Visible == (fraction > 0), "Zero HP renders empty fill.");
        Require(Math.Abs(fill.Scale.X - Math.Max(fraction, 0.0001f)) < 0.0001f, "Fill reflects authoritative current/max HP.");
        Require(Math.Abs(fill.Position.X - ((fraction - 1) * 0.9f)) < 0.0001f, "Health fill remains left anchored.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
