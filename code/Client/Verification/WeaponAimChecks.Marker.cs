using Godot;
using Trackstorm.Core.Items;
using Trackstorm.Client.Networking;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

public sealed partial class WeaponAimChecks
{
    private async Task CheckTargetMarker()
    {
        var arena = _arenas[1]; var host = _arenas[0].Driver.Host!;
        var marker = arena.TargetIndicator;
        var tag = new RemoteVehicleTag(); arena.AddChild(tag);
        var settings = new Settings.PlayerSettingsController();
        settings.Initialize(_input.Adapter, System.IO.Path.Combine(_output, "marker-settings.json")); AddChild(settings);
        arena.CameraSettings = settings;
        float floor = Ground + 35;
        var platforms = new List<StaticBody3D>();
        var surfaces = new List<StandardMaterial3D>();
        foreach (var peer in _arenas)
        {
            var material = new StandardMaterial3D { AlbedoColor = new(.3f, .32f, .34f), Roughness = 1 };
            var platform = new StaticBody3D { Position = new(0, floor - 1.65f, 0), CollisionLayer = 1 };
            platform.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(500, 1, 500) } });
            platform.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new(500, 1, 500) }, MaterialOverride = material });
            peer.AddChild(platform); platforms.Add(platform); surfaces.Add(material);
        }
        Vector3 Center() => arena.Bodies[1].VisualTransform * new Vector3(0, .3f, 0);
        var widths = new Dictionary<(float Distance, int Variant), float>();
        foreach (float distance in new[] { 6f, 35f, 200f })
        foreach (int variant in new[] { 0, 1 })
        {
            settings.UpdateSettings(new() { CameraFov = variant == 0 ? 50 : 90 });
            _views[1].Size = variant == 0 ? new(1280, 800) : new(1920, 1080);
            foreach (var material in surfaces) { material.AlbedoColor = variant == 0 ? new(.065f, .07f, .08f) : new(.8f, .78f, .7f); }
            host.Items.RemovePlayer(Shooter);
            Position(new(0, floor, distance), new(0, floor, 0), new(70, floor, 0));
            _heldTargets = (new(0, floor, 0), new(70, floor, 0));
            Camera.ResetFollow(); await Frames(90); await AimAtCurrent(Center);
            host.Items.Grant(host.World, Shooter, HeldItem.MachineGun);
            await Until(() => marker.Target == 1, "Only the locally assisted rival receives the hybrid marker");
            Require(!marker.Engaged, "Acquired/deploying uses the white state from actual not-ready aim");
            CheckTagLayout(); await Capture($"marker-{distance}-{variant}-acquired");
            if (distance > 6) { await Until(() => marker.Engaged, "Accepted readiness selects the warm engaged marker"); }
            else { await Frames(120); Require(marker.Engaged == arena.Driver.AcceptedAims.First(a => a.Vehicle == Shooter).Ready,
                "Close marker colour preserves actual self-clearance readiness instead of forcing an engaged state"); }
            await Frames(30);
            Require(marker.RingVisible && marker.ChevronVisible, "Supported target shows segmented footprint and top chevron");
            Require(_arenas.Where(a => a != arena).All(a => a.TargetIndicator.Target == 0), "Other peer cameras do not inherit the local target indicator");
            Require(arena.AimOverlay.Bounds.HasValue, "Unexpanded accuracy reference remains available independently of marker geometry");
            CheckMarkerSupport();
            widths[(distance, variant)] = marker.ChevronWidth / _views[1].Size.Y;
            GD.Print($"MARKER_SCALE distance={distance} variant={variant} widthPixels={marker.ChevronWidth:F3}");
            GD.Print($"MARKER distance={distance} measured={arena.Bodies[Shooter].VisualPosition.DistanceTo(arena.Bodies[1].VisualPosition):F3} fov={Camera.Fov} size={_views[1].Size} ringPoints={marker.SurfacePoints.Count} tip={Camera.UnprojectPosition(marker.ChevronTip)}");
            CheckTagLayout(); await Capture($"marker-{distance}-{variant}-" + (marker.Engaged ? "engaged" : "restricted"));
            if (distance == 35 && variant == 0 && DisplayServer.GetName() != "headless")
            {
                Vector2 foot = Camera.UnprojectPosition(arena.Bodies[1].VisualPosition - Vector3.Up * 1.145f);
                var region = new Rect2I((Vector2I)(foot - new Vector2(65, 8)), new(130, 18));
                int lit = await WarmPixels(region);
                marker.Hide(); int unmarked = await WarmPixels(region); marker.Show();
                GD.Print($"MARKER_RING_PIXELS lit={lit} unmarked={unmarked} contribution={lit - unmarked}");
                Require(lit - unmarked > 40, "Ground ring makes a measurable luminous contribution around the supported footprint");
            }
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false }); await Frames(2);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(marker.Target == 0 && !marker.RingVisible && !marker.ChevronVisible, "RMB release clears both hybrid elements immediately");
            CheckTagLayout();
        }
        foreach (int variant in new[] { 0, 1 })
        {
            Require(widths[(6, variant)] > widths[(35, variant)] * 1.25f && widths[(35, variant)] > widths[(200, variant)] * 1.25f,
                "Chevron visibly shrinks from near to intermediate to far at each FOV instead of keeping a fixed pixel size");
        }
        settings.UpdateSettings(new()); _views[1].Size = new(1280, 800);
        foreach (var material in surfaces) { material.AlbedoColor = new(.3f, .32f, .34f); }
        Position(new(0, floor, 35), new(0, floor, 0), new(6, floor, 0));
        _heldTargets = (new(0, floor, 0), new(6, floor, 0));
        Camera.ResetFollow(); await Frames(90); await AimAtCurrent(Center); await Frames(30);
        foreach (Vector2I size in new[] { new Vector2I(640, 360), new Vector2I(2560, 1080) })
        {
            _views[1].Size = size; await Frames(20);
            Require(marker.Target == 1 && marker.ChevronVisible && marker.RingVisible, "Hybrid marker survives small and ultrawide viewport changes");
            Require(new Rect2(Vector2.Zero, size).HasPoint(Camera.UnprojectPosition(marker.ChevronTip)), "Chevron remains anchored onscreen after resizing");
            CheckTagLayout(); await Capture($"marker-size-{size.X}-{size.Y}");
        }
        _views[1].Size = new(1280, 800); await Frames(20);
        if (DisplayServer.GetName() != "headless")
        {
            Vector2 tip = Camera.UnprojectPosition(marker.ChevronTip);
            var region = new Rect2I((Vector2I)(tip - new Vector2(25, 30)), new(50, 34));
            int before = await WarmPixels(region);
            float depth = -Camera.ToLocal(marker.ChevronTip).Z * .5f;
            float unit = Camera.ProjectPosition(Vector2.Right, depth).DistanceTo(Camera.ProjectPosition(Vector2.Zero, depth));
            var blocker = new MeshInstance3D { Mesh = new BoxMesh { Size = new(52 * unit, 36 * unit, .05f) },
                MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new(.12f, .13f, .14f) } };
            arena.AddChild(blocker); blocker.GlobalTransform = new(Camera.GlobalBasis, Camera.ProjectPosition(tip - new Vector2(0, 13), depth));
            await Frames(4); int after = await WarmPixels(region);
            GD.Print($"MARKER_DEPTH warmPixels={before}->{after} retained={marker.Target}");
            Require(marker.Target == 1 && before > 15 && after < before * .2f,
                "Opaque rendered scenery hides the top glyph without requiring target loss or drawing through it");
            CheckTagLayout(); await Capture("marker-glyph-occluded"); blocker.Free();
        }
        var slopes = new List<StaticBody3D>();
        foreach (var peer in _arenas)
        {
            var slope = new StaticBody3D { Position = new(0, floor - .65f, 0), Rotation = new(.12f, 0, .08f), CollisionLayer = 1 };
            slope.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(12, .5f, 12) } });
            slope.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new(12, .5f, 12) },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new(.45f, .4f, .3f) } }); peer.AddChild(slope); slopes.Add(slope);
        }
        _targetOrientation = N.Quaternion.CreateFromYawPitchRoll(0, .12f, .08f);
        _heldTargets = (new(0, floor + .75f, 0), new(6, floor, 0)); await Frames(45); await AimAtCurrent(Center); await Frames(30);
        Require(marker.RingVisible, "Sloped support keeps the ring attached to the footprint");
        CheckMarkerSupport(); CheckTagLayout(); await Capture("marker-slope");
        foreach (var slope in slopes) { slope.Free(); } _targetOrientation = N.Quaternion.Identity;
        _heldTargets = (new(0, floor, 0), new(6, floor, 0)); await Frames(45); await AimAtCurrent(Center);
        for (int i = 0; i < 90; i++)
        {
            _targetOrientation = N.Quaternion.CreateFromYawPitchRoll(i * .008f, MathF.Sin(i * .1f) * .06f, MathF.Sin(i * .12f) * .08f);
            _heldTargets = (new(i * .025f, floor, 0), new(6 + i * .025f, floor, 0)); await Frames(1);
            RequireShot(marker.Target == 1 && marker.ChevronVisible, "Moving/turning car keeps the marker on its assisted identity");
            Vector2 gap = Camera.UnprojectPosition(marker.ChevronTip) - Camera.UnprojectPosition(arena.Bodies[1].VisualPosition + Vector3.Up * 1.6f);
            RequireShot(Math.Abs(gap.X) < .01f && gap.Y < -2 && gap.Y > -20,
                "Chevron stays upright at its world-up anchor through chassis pitch and roll");
        }
        Require(true, "Turning and moving target retained for 90 native frames");
        CheckTagLayout(); await Capture("marker-moving-pair");
        _targetOrientation = N.Quaternion.Identity;
        _heldTargets = (new(2.25f, floor + 6, 0), new(8.25f, floor, 0)); await Frames(30); await AimAtCurrent(Center); await Frames(30);
        Require(marker.Target == 1 && marker.ChevronVisible && !marker.RingVisible, "Airborne target keeps its chevron without a ground or floating ring");
        CheckTagLayout(); await Capture("marker-airborne");
        _heldTargets = (new(2.25f, floor, 0), new(8.25f, floor, 0)); await Frames(60); await AimAtCurrent(Center); await Frames(30);
        Require(marker.RingVisible, "Landing restores the supported footprint");
        CheckTagLayout(); await Capture("marker-landed");
        ulong other = arena.Bodies.Keys.Single(id => id != Shooter && id != 1);
        await AimAtCurrent(() => arena.Bodies[other].VisualTransform * new Vector3(0, .3f, 0)); await Frames(30);
        Require(marker.Target == other && arena.AssistedCar == other, "Intentional switching transfers both elements to the other nearby car");
        CheckTagLayout(); await Capture("marker-switched");
        var cover = new StaticBody3D { Position = Camera.GlobalPosition.Lerp(arena.Bodies[other].VisualPosition, .65f), CollisionLayer = 1 };
        cover.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(12, 12, 1) } });
        cover.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new(12, 12, 1) } }); arena.AddChild(cover);
        await Frames(5);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        CheckTagLayout(); await Capture("marker-covered");
        Require(marker.Target == 0 && !marker.RingVisible && !marker.ChevronVisible, "Real full cover clears both elements");
        cover.Free();
        _heldTargets = null; _targetOrientation = N.Quaternion.Identity;
        await CheckAimDeath();
        Require(marker.Target == 0 && !marker.ChevronVisible && !marker.RingVisible, "Actual death/respawn cannot restore an old hybrid marker");
        arena.CameraSettings = null; settings.QueueFree();
        foreach (var platform in platforms) { platform.QueueFree(); }

        void CheckTagLayout()
        {
            ulong id = marker.Target == 0 ? 1 : marker.Target;
            Vector3 position = arena.Bodies[id].VisualPosition;
            var state = arena.Driver.Latest!.Vehicles.First(v => v.State.VehicleId == id).State;
            // This UDP fixture has no lobby roster. Supply only the name explicitly;
            // production tag rendering and accepted health remain the tested path.
            tag.Present("Élodie 車 Player", state, position, Camera, marker.ChevronVisible ? marker.ChevronTop : null);
            if (marker.ChevronVisible)
            {
                float bottom = Camera.UnprojectPosition(tag.GlobalPosition - Camera.GlobalBasis.Y * .08f).Y;
                float gap = marker.ChevronTop.Y - bottom;
                Require(gap >= 6 * _views[1].Size.Y / 1080 - .02f, "Name/HP stack clears the chevron glow at this distance/FOV/resolution");
                Require(Camera.UnprojectPosition(tag.GetNode<Label3D>("PlayerName").GlobalPosition).Y < bottom,
                    "Name remains above HP, with both above the chevron");
                GD.Print($"MARKER_TAG target={id} gapPixels={gap:F3}");
            }
            else
            {
                Require(tag.GlobalPosition.IsEqualApprox(position + Vector3.Up * 2.4f), "Without assistance the tag returns to its ordinary height");
            }
        }

        async Task<int> WarmPixels(Rect2I area)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = _views[1].GetTexture().GetImage();
            int count = 0;
            for (int y = area.Position.Y; y < area.End.Y; y++)
            for (int x = area.Position.X; x < area.End.X; x++)
            {
                Color color = image.GetPixel(x, y);
                if (color.R > .6f && color.R - color.G > .16f && color.G > color.B) { count++; }
            }
            return count;
        }

        void CheckMarkerSupport()
        {
            foreach (Vector3 point in marker.SurfacePoints)
            {
                using var query = PhysicsRayQueryParameters3D.Create(point + Vector3.Up * .1f, point + Vector3.Down * .2f, 1);
                using var hit = arena.GetWorld3D().DirectSpaceState.IntersectRay(query);
                RequireShot(hit.Count > 0 && point.DistanceTo(hit["position"].AsVector3()) < .075f, "Every ring vertex follows support within the small render/collision clearance allowance");
            }
            Require(marker.SurfacePoints.Count > 0, "Ground ring uses sampled native support rather than a floating flat disk");
        }
    }
}
