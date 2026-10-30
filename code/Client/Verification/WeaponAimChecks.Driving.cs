using Godot;
using Trackstorm.Core.Items;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

public sealed partial class WeaponAimChecks
{
    private async Task CheckDrivingFire()
    {
        var host = _arenas[0].Driver.Host!;
        var driver = _arenas[1].Driver;
        var events = new List<ItemEvent>();
        _arenas[0].Driver.ItemsReceived += state => events.AddRange(state.Events);
        foreach (var item in new[] { HeldItem.MachineGun, HeldItem.Missile, HeldItem.EmpClaw, HeldItem.Salvo })
        {
            host.Items.RemovePlayer(Shooter);
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
            Camera.ResetFollow();
            _heldShooter = new(0, Ground, 35);
            _heldTargets = (new(70, Ground, -70), new(-70, Ground, -70));
            Position(_heldShooter.Value, _heldTargets.Value.First, _heldTargets.Value.Second);
            await Frames(180);
            Require(host.Items.Grant(host.World, Shooter, item), item + ": ordinary grant");
            await Frames(240);
            var slot = driver.LocalItem!;
            var aim = host.Items.AcceptedAim(Shooter, slot.Life, slot.Active.Token);
            Require(!_input.Adapter.CameraAimActive && aim?.Ready == true, item + ": driving ready without camera aim");
            Require(N.Vector3.Dot(aim!.Direction, Trackstorm.Client.Vehicles.DrivingWeaponAim.Direction(-N.Vector3.UnitZ, N.Vector3.UnitY)) > .9999f, item + ": authority accepts road-forward direction");
            var marker = _arenas[1].AimOverlay.Marker!.Value;
            Vector3 point = Trackstorm.Client.Vehicles.VehicleBody.ToGodot(aim.Origin + aim.Direction * 60);
            Require(Camera.UnprojectPosition(point).DistanceTo(marker) < 2, item + ": driving square projects the accepted firing line");
            RequireVisibleCursor(item + ": raised driving square visible");
            await Capture("driving-" + item);
            events.Clear();
            if (item == HeldItem.MachineGun)
            {
                Require(driver.RequestItemUse(), item + ": ordinary trigger engagement");
                _firingPeers.Add(Shooter);
                await Frames(35);
                _firingPeers.Remove(Shooter);
            }
            else
            {
                Require(driver.RequestItemUse(), item + ": ordinary firing request");
                await Frames(180);
            }
            Require(events.Any(e => e.Item == item), item + ": authoritative firing produces outcomes");
            Require(driver.LocalItem?.Active.Item == HeldItem.None || driver.LocalItem?.Active.Token != slot.Active.Token ||
                driver.LocalItem.Active.Ammo?.Remaining < slot.Active.Ammo?.Remaining,
                item + ": firing consumes ammunition");
        }
        _heldShooter = null;
        _heldTargets = null;
    }
}
