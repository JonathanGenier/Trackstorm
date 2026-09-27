using Godot;
using Trackstorm.Client.Hud;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;
using Trackstorm.Core.Settings;
using Trackstorm.Core.Vehicles;
using Numerics = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Rendered production arena and native HUD checks across settings, source states and resolutions.</summary>
public sealed partial class HudIntegrationChecks : Node
{
    /// <inheritdoc/>
    public override void _Ready() => CallDeferred(MethodName.Run);

    /// <summary>Checks native labels/materials and exports comparable viewport captures.</summary>
    public async void Run()
    {
        try
        {
            string output = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--hud-output=", StringComparison.Ordinal))?[13..] ?? ProjectSettings.GlobalizePath("res://.godot/hud-checks");
            System.IO.Directory.CreateDirectory(output);
            var viewport = new SubViewport { Size = new Vector2I(1280, 720), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            AddChild(viewport);
            var arena = new VehicleArena();
            viewport.AddChild(arena);
            VehicleSnapshot state = arena.Player.Snapshot;
            Require(state.Damage.MaxHP == 1000, "Production practice capacity");
            ItemSlot? slot = null;
            var input = new Input.PlayerInput();
            AddChild(input);
            input.SetPhysicsProcess(false);
            var settings = new Settings.PlayerSettingsController();
            settings.Initialize(input.Adapter, System.IO.Path.Combine(output, "settings.json"));
            AddChild(settings);
            var hud = new CombatHud { Vehicle = () => state, Slot = () => slot, Units = () => settings.Current.SpeedUnit };
            viewport.AddChild(hud);
            var timerMatch = new Core.Matches.MatchState(180, 1, 5, Core.Matches.MatchPhase.Active, null, null,
                [new Core.Matches.PlayerScore(1, 0, 0, 0, 0)], activeStartedAtTick: 180);
            ulong timerTick = 180;
            hud.Match = () => timerMatch;
            hud.AuthoritativeTick = () => timerTick;
            foreach (var sample in new[] { (180ul, "10:00"), (240ul, "09:59"), (36120ul, "00:01"), (36180ul, "00:00") })
            {
                timerTick = sample.Item1;
                hud.Refresh();
                Require(hud.FindChildren("TimerValue", "Label", true, false).OfType<Label>().Single().Text == sample.Item2, "Existing top timer label reads the authoritative time");
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using Image timerFrame = viewport.GetTexture().GetImage();
                Require(timerFrame.SavePng(System.IO.Path.Combine(output, $"timer-{sample.Item2.Replace(':', '-')}.png")) == Error.Ok, "Timer screenshot");
            }
            hud.Match = () => null;
            state = new VehicleSnapshot(state.VehicleId,state.LifeId,state.Movement,state.Damage,state.ObservedPhysics,outOfBounds:true);
            hud.Refresh();
            Require(hud.Displayed!.OutOfBounds && hud.FindChild("OutOfBounds",true,false) is Label { Visible:true, Text: "OUT OF BOUNDS\nARENA HAZARD • LOSING HEALTH" }, "Authoritative OOB warning renders");
            state = Sample(state,0,0); hud.Refresh();
            Require(!hud.Displayed!.OutOfBounds && hud.FindChild("OutOfBounds",true,false) is Label { Visible:false }, "Death clears OOB warning");
            state = Sample(state,1000,0); hud.Refresh();

            var preferences = new Settings.SettingsPanel();
            preferences.Initialize(settings, input.Adapter);
            viewport.AddChild(preferences);
            settings.UpdateSettings(settings.Current with { ShowFps = true, ShowPing = true });
            preferences.SetConnectionTelemetry(new(Networking.ConnectionDiagnosticState.Reconnecting, default));
            var pixels = new List<(int Health, int Speed)>();
            foreach (var sample in new[] { (1000f, 200 / 3.6f, HeldItem.None), (500f, 100 / 3.6f, HeldItem.Wrench), (0f, 0f, HeldItem.None), (850f, 200 / 3.6f, HeldItem.Missile), (850f, 200 / 3.6f, HeldItem.Oil), (850f, 200 / 3.6f, HeldItem.Nitro), (850f, 200 / 3.6f, HeldItem.ProxyMine), (850f, 200 / 3.6f, HeldItem.Salvo) })
            {
                state = Sample(state, sample.Item1, sample.Item2);
                slot = new ItemSlot(state.VehicleId, state.LifeId, 1, sample.Item3);
                hud.Refresh();
                Require(hud.HealthText == $"{sample.Item1:0}/1000", "Native health label");
                Require(Math.Abs(hud.HealthFill - (sample.Item1 / 1000.0)) < 0.00001, "Native health material");
                Require(Math.Abs(hud.SpeedFill - CombatHudView.NormalizeSpeed(sample.Item2)) < 0.00001, "Native speed material");
                Require(hud.Displayed!.Item == sample.Item3, "Native item mapping");
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using Image frame = viewport.GetTexture().GetImage();
                pixels.Add((RedPixels(frame, ScreenArea(hud, "Health", new Rect2(115, 62, 275, 14))), RedPixels(frame, ScreenArea(hud, "ItemAssembly", new Rect2(94, 61, 169, 92)))));
                Require(frame.SavePng(System.IO.Path.Combine(output, $"state-{sample.Item1:0}-{sample.Item3}.png")) == Error.Ok, "State screenshot");
            }

            Require(pixels[0].Health > pixels[1].Health && pixels[1].Health > pixels[2].Health && pixels[2].Health < pixels[0].Health / 10, "Rendered health fill decreases to empty");
            Require(pixels[0].Speed > pixels[1].Speed && pixels[1].Speed > pixels[2].Speed && pixels[2].Speed < pixels[0].Speed / 5, "Rendered speed arc decreases to empty");

            foreach (int shots in new[] { 5, 4, 1, 0 })
            {
                slot = new ItemSlot(state.VehicleId, state.LifeId, 1, shots > 0 ? HeldItem.Salvo : HeldItem.None)
                { SalvoShots = shots, SecondToken = 2, SecondItem = HeldItem.Salvo, SecondSalvoShots = 3 };
                hud.Refresh();
                Require(hud.Displayed!.ItemName == (shots > 0 ? $"SALVO {shots}" : "EMPTY"), "Salvo remaining shots and exhaustion");
                Require(hud.Displayed.SecondItemName == "SALVO 3", "Independent second-slot ammunition");
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using Image frame = viewport.GetTexture().GetImage();
                Require(frame.SavePng(System.IO.Path.Combine(output, $"salvo-{shots}.png")) == Error.Ok, "Ammunition screenshot");
            }
            var resourcePixels = new List<(int First, int Second)>();
            foreach (double charge in new[] { 100.0, 37.5, 0.1, 0.0 })
            {
                slot = new ItemSlot(state.VehicleId, state.LifeId, 1, charge == 0 ? HeldItem.None : HeldItem.Nitro)
                { NitroCharge = charge, SecondToken = 2, SecondItem = HeldItem.Nitro, SecondNitroCharge = 65, ActiveSlot = 1 };
                hud.Refresh();
                Require(hud.Displayed!.ItemName == (charge == 0 ? "EMPTY" : $"NITRO {Math.Ceiling(charge):0}%"), "Authoritative charge label and exhaustion");
                Require(hud.Displayed.SecondItemName == "NITRO 65%", "Independent second-slot charge");
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using Image frame = viewport.GetTexture().GetImage();
                resourcePixels.Add((BoostPixels(frame, ScreenArea(hud, "FirstSlot", new Rect2(9, 40, 96, 14))), BoostPixels(frame, ScreenArea(hud, "SecondSlot", new Rect2(9, 40, 96, 14)))));
                Require(frame.SavePng(System.IO.Path.Combine(output, $"nitro-{charge:0.0}.png")) == Error.Ok, "Charge screenshot");
            }
            Require(resourcePixels[0].First > resourcePixels[1].First && resourcePixels[1].First > resourcePixels[3].First, "Rendered resource meter drains and disappears at exhaustion");
            Require(resourcePixels.All(value => value.Second == resourcePixels[0].Second), "Other physical slot meter remains unchanged while the first drains");
            Require(resourcePixels[0].Second > 0, "Boost has a visible slot-local thrust meter");
            slot = new ItemSlot(state.VehicleId, state.LifeId, 1, HeldItem.MachineGun)
            { Ammo = new(187, 500), SecondToken = 2, SecondItem = HeldItem.Nitro, SecondNitroCharge = 42, ActiveSlot = 0 };
            hud.Refresh();
            CheckSlot(hud, "FirstSlot", "MACHINE GUN", "38%", true);
            CheckSlot(hud, "SecondSlot", "NITRO", "42%", false);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using (Image frame = viewport.GetTexture().GetImage())
            {
                Require(BoostPixels(frame, ScreenArea(hud, "SecondSlot", new Rect2(9, 40, 96, 14))) > 0, "Second slot renders Boost identity independently of first-slot ammunition");
                Require(frame.SavePng(System.IO.Path.Combine(output, "boost-second-with-machine-gun.png")) == Error.Ok, "Second-slot Boost screenshot");
            }
            slot = new ItemSlot(state.VehicleId, state.LifeId, 1, HeldItem.MachineGun)
            { Ammo = new(187, 500), SecondToken = 2, SecondItem = HeldItem.MachineGun, SecondAmmo = new(1, 500) };
            hud.Refresh();
            Require(hud.Displayed!.ItemName == "MACHINE GUN 38%" && hud.Displayed.SecondItemName == "MACHINE GUN 1%", "Independent discrete ammo percentages");
            CheckSlot(hud, "FirstSlot", "MACHINE GUN", "38%", true);
            CheckSlot(hud, "SecondSlot", "MACHINE GUN", "1%", false);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using (Image frame = viewport.GetTexture().GetImage()) { frame.SavePng(System.IO.Path.Combine(output, "machine-gun-partial.png")); }
            // Exercise replacement/clear through the production binding, including deliberately stale resource fields.
            foreach (bool second in new[] { false, true })
            {
                foreach (HeldItem replacement in new[] { HeldItem.None, HeldItem.Wrench, HeldItem.Oil, HeldItem.Salvo, HeldItem.Nitro, HeldItem.MachineGun, HeldItem.None })
                {
                    slot = second ? slot! with { SecondItem = replacement, SecondSalvoShots = 3, SecondNitroCharge = 62.5, ActiveSlot = 1 }
                        : slot! with { Item = replacement, SalvoShots = 3, NitroCharge = 62.5, ActiveSlot = 0 };
                    hud.Refresh();
                    string? resource = replacement switch { HeldItem.Salvo => "3", HeldItem.Nitro => "63%", HeldItem.MachineGun => second ? "1%" : "38%", _ => null };
                    CheckSlot(hud, second ? "SecondSlot" : "FirstSlot", ItemRegistry.Find(replacement)?.DisplayName.ToUpperInvariant() ?? "EMPTY", resource, true);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                }
            }
            slot = slot! with { Item = HeldItem.Nitro, NitroCharge = 37.5, SecondItem = HeldItem.Nitro, SecondNitroCharge = 65, ActiveSlot = 1 };
            hud.Refresh();
            CheckSlot(hud, "FirstSlot", "NITRO", "38%", false);
            CheckSlot(hud, "SecondSlot", "NITRO", "65%", true);
            ItemSlot validInventory = slot;
            foreach (ItemSlot? invalidInventory in new[] { slot with { Life = slot.Life + 1 }, slot with { Vehicle = slot.Vehicle + 1 }, null })
            {
                slot = invalidInventory;
                hud.Refresh();
                CheckSlot(hud, "FirstSlot", "EMPTY", null, true);
                CheckSlot(hud, "SecondSlot", "EMPTY", null, false);
            }
            slot = validInventory;
            VehicleSnapshot before = state;
            slot = slot! with { SecondToken = 2, SecondItem = HeldItem.Missile, ActiveSlot = 1, SelectionRevision = 1 };
            hud.Refresh();
            Require(hud.Displayed!.SecondItem == HeldItem.Missile && hud.Displayed.ActiveSlot == 1, "Both slots and selected second slot render simultaneously");
            settings.UpdateSettings(settings.Current with { SpeedUnit = SpeedUnit.MilesPerHour });
            hud.Refresh();
            Require(hud.Displayed!.Speed == "124" && hud.Displayed.Unit == "mph", "Preferred units");
            Require(ReferenceEquals(before, state), "Unit change is presentation only");
            slot = validInventory with { Item = HeldItem.MachineGun, Ammo = new(187, 500), SecondItem = HeldItem.Nitro, SecondNitroCharge = 42 };
            hud.Refresh();
            foreach (Vector2I size in new[] { new Vector2I(640, 360), new Vector2I(960, 540), new Vector2I(1280, 720), new Vector2I(1600, 900), new Vector2I(1920, 1080), new Vector2I(2560, 1440), new Vector2I(3840, 2160), new Vector2I(1024, 768), new Vector2I(2560, 1080) })
            {
                viewport.Size = size;
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                Require(hud.Displayed!.Standing == "--" && hud.Displayed.Timer == "--:--", "Placeholders");
                Require(preferences.DiagnosticsBounds.Position.X >= size.X / 2.0f && preferences.DiagnosticsBounds.End.X <= size.X, "Diagnostics fit the upper-right region");
                var assembly = (Control)hud.FindChild("ItemAssembly", true, false);
                var health = (Control)hud.FindChild("Health", true, false);
                Require(assembly.GetGlobalRect().Position.X > health.GetGlobalRect().End.X, "Separate HP and item assembly never overlap");
                Require(assembly.GetGlobalRect().End.X <= size.X && assembly.GetGlobalRect().End.Y <= size.Y && assembly.GetGlobalRect().Position.Y >= 0, "Unified assembly fits the viewport");
                using Image frame = viewport.GetTexture().GetImage();
                Require(frame.SavePng(System.IO.Path.Combine(output, $"hud-{size.X}x{size.Y}.png")) == Error.Ok, "Resolution screenshot");
            }

            // A transparent native render of the real component makes reference comparison reviewable.
            var isolated = new SubViewport { Size = new Vector2I(1280, 720), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            AddChild(isolated);
            VehicleSnapshot referenceState = Sample(state, 1000, 2 / 3.6f);
            var referenceSlot = new ItemSlot(state.VehicleId, state.LifeId, 1, HeldItem.Nitro)
            { NitroCharge = 78, SecondToken = 2, SecondItem = HeldItem.MachineGun, SecondAmmo = new(320, 500) };
            var referenceHud = new CombatHud { Vehicle = () => referenceState, Slot = () => referenceSlot };
            isolated.AddChild(referenceHud);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using (Image frame = isolated.GetTexture().GetImage())
            using (Image composition = frame.GetRegion(ScreenArea(referenceHud, "ItemAssembly", new Rect2(0, 0, 600, 200))))
            {
                Require(composition.SavePng(System.IO.Path.Combine(output, "foundation-reference-composition.png")) == Error.Ok, "Reference comparison native component capture");
            }
            isolated.QueueFree();
            hud.Vehicle = () => null;
            hud.Refresh();
            Require(!hud.Visible && hud.Displayed is null, "No stale HUD outside match");
            viewport.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            settings.QueueFree();
            input.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
            GD.Print("HUD integration passed: state changes, units, 1000 HP, placeholders, nine resolutions, teardown visibility.");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
    }

    private static VehicleSnapshot Sample(VehicleSnapshot previous, float hp, float speed)
    {
        VehiclePhysicsState pose = new(previous.ObservedPhysics.Position, previous.ObservedPhysics.Orientation, new Numerics.Vector3(speed, 0, 0), Numerics.Vector3.Zero);
        return new VehicleSnapshot(previous.VehicleId, previous.LifeId, new VehicleState(0, pose, false, false, 0, 0), new VehicleDamageState(1000, hp, null, null), pose);
    }

    private static void CheckSlot(CombatHud hud, string node, string name, string? resource, bool active)
    {
        var slot = (Control)hud.FindChild(node, true, false);
        Require(((Label)slot.FindChild("ItemName", true, false)).Text == name, "Physical slot identity label");
        var value = (Label)slot.FindChild("ResourceValue", true, false);
        Require(value.Visible == (resource is not null) && value.Text == (resource ?? string.Empty), "Optional slot-local resource clears on replacement");
        Require(((Label)slot.FindChild("Selection", true, false)).Text.Contains("ACTIVE", StringComparison.Ordinal) == active, "Confirmed physical selection");
        Require(((TextureRect)slot.FindChild("ItemIcon", true, false)).Visible == (name != "EMPTY"), "Empty slots clear their icons");
        Require(((Label)slot.FindChild("BoostIdentity", true, false)).Visible == (name == "NITRO"), "Boost-specific art follows physical slot ownership");
    }

    private static Rect2I ScreenArea(CombatHud hud, string node, Rect2 localArea)
    {
        var control = (Control)hud.FindChild(node, true, false);
        return new Rect2I((Vector2I)(control.GlobalPosition + localArea.Position * control.GetGlobalTransform().Scale),
            (Vector2I)(localArea.Size * control.GetGlobalTransform().Scale));
    }

    private static int RedPixels(Image frame, Rect2I area)
    {
        int count = 0;
        for (int y = area.Position.Y; y < area.End.Y; y++)
        {
            for (int x = area.Position.X; x < area.End.X; x++)
            {
                Color pixel = frame.GetPixel(x, y);
                if (pixel.R > 0.25f && pixel.R > pixel.G * 2 && pixel.R > pixel.B * 2)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int BoostPixels(Image frame, Rect2I area)
    {
        int count = 0;
        for (int y = area.Position.Y; y < area.End.Y; y++)
        {
            for (int x = area.Position.X; x < area.End.X; x++)
            {
                Color pixel = frame.GetPixel(x, y);
                if (pixel.B > .4f && pixel.G > .25f && pixel.B > pixel.R * 1.6f) count++;
            }
        }
        return count;
    }

    private static void Require(bool value, string label)
    {
        if (!value)
        {
            throw new InvalidOperationException(label);
        }
    }
}
