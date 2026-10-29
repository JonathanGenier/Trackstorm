using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

public sealed partial class SalvoExplosionChecks
{
    private N.Quaternion _shieldOrientation = N.Quaternion.Identity;

    private async Task ShieldInterception()
    {
        foreach (var gateway in _gateways) { gateway.ConfigureSimulation(new(60, 12, 3, 0, 0)); }
        Check(Host.TryConfigure(0, new Dictionary<string, double> { ["items.salvo_blast_radius"] = 5 }, out _), "Five-metre Salvo fixture exposes both hull and armor to one blast");
        Focus(VehicleBody.ToGodot(_positions[2]), new(8, 6, 10));
        foreach (var item in new[] { HeldItem.Salvo, HeldItem.Missile })
        {
            foreach (float yaw in new[] { 0f, 1.2f })
            {
                _shieldOrientation = N.Quaternion.CreateFromYawPitchRoll(yaw, yaw == 0 ? 0 : .2f, 0);
                foreach (var local in new[] { new N.Vector3(0, .25f, 4), new(0, .25f, -3), new(0, 3, 3.6f) })
                {
                    Host.Items.RemovePlayer(2);
                    Check(Host.Items.Grant(Host.World, 2, HeldItem.Shield), "Fresh ordinary selected Shield");
                    await Frames(140);
                    var shield = Host.Items.Shields.Single();
                    float hp = Host.World.GetVehicle(2).Damage.CurrentHP;
                    N.Vector3 center = _positions[2] + N.Vector3.Transform(local, _shieldOrientation);
                    // A small native static box determines the blast point outside the
                    // armor. Seeded flight is explicit; collision and damage are native.
                    var obstacle = new StaticBody3D { Position = VehicleBody.ToGodot(center), CollisionLayer = 1 };
                    obstacle.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = Vector3.One * .08f } });
                    _arenas[0].AddChild(obstacle); await Frames(2);
                    SeedShieldRound(item, center);
                    await Until(() => Host.Items.Missiles.Count == 0, "Native projectile detonated", 120);
                    obstacle.QueueFree();
                    bool rear = local.Z > 0 && local.Y < 1;
                    float after = Host.World.GetVehicle(2).Damage.CurrentHP;
                    Check(rear ? after == hp : after < hp, $"{item}, yaw {yaw}, blast {local}: {(rear ? "covered car unchanged" : "exposed car damaged")}");
                    if (local.Z > 0) { Check(Host.Items.Shields.Single().HP < shield.HP, "Rear/upper blast damages armor once"); }
                    await Frames(90);
                    Check(_arenas.All(a => a.Driver.ItemState!.Shields.SequenceEqual(Host.Items.Shields)), "Exact shield HP and damage watermark converge across three impaired UDP peers");
                    Check(_arenas.All(a => a.Driver.Latest!.Vehicles.Single(v => v.State.VehicleId == 2).State.Damage.CurrentHP == after), "Vehicle HP converges without duplicate damage");
                    await Capture($"shield-{item}-{yaw}-{local.Z}-{local.Y}");
                    // Health reset is an explicit fixture, preserving authoritative identity.
                    var state = Host.World.State;
                    Host.World.Restore(new(state.Tick, state.LastInput, state.Vehicles.Select(v => v.VehicleId != 2 ? v :
                        new VehicleSnapshot(v.VehicleId, v.LifeId, v.Movement,
                            new(v.Damage.MaxHP, v.Damage.MaxHP, v.Damage.LastDamage, v.Damage.LastCollisionTick), v.ObservedPhysics)), state.Match));
                }
            }
        }
        _shieldOrientation = N.Quaternion.Identity;
        await ShieldContactWeapons();
        Host.Items.RemovePlayer(2); await Frames(60);
        Check(Host.Items.Grant(Host.World, 2, HeldItem.Shield), "Deployed wall fixture gets ordinary shield");
        var armor = Host.Items.Shields.Single();
        Check(Host.Items.TransitionShield(Host.World, armor.Id, ShieldStage.RearShield, ShieldStage.WorldWall,
            new(_positions[2] + new N.Vector3(0, .25f, 4), N.Quaternion.Identity, default, default)), "Install world-space wall fixture");
        Host.Items.DamageShield(Host.World, armor.Id, 1, 999, new("test", 1, "fixture"));
        await Frames(60);
        float before = Host.World.GetVehicle(2).Damage.CurrentHP;
        SeedShieldRound(HeldItem.Missile, _positions[2] + new N.Vector3(0, .25f, 4.1f));
        await Until(() => Host.Items.Missiles.Count == 0, "Missile hits damaged deployed wall");
        Check(Host.Items.Shields.Count == 0 && Host.World.GetVehicle(2).Damage.CurrentHP == before, "Lethal wall hit removes armor while shielding the car");
        await Frames(90);
        Check(_arenas.All(a => a.Driver.ItemState!.Shields.Count == 0), "Destroyed wall disappears on every peer");
        SeedShieldRound(HeldItem.Missile, _positions[2] + new N.Vector3(0, .25f, 2));
        await Until(() => Host.Items.Missiles.Count == 0, "Next missile reaches exposed chassis");
        Check(Host.World.GetVehicle(2).Damage.CurrentHP < before, "No stale native wall interception after destruction");
        await Frames(600);
        Check(_arenas.All(a => a.Driver.ItemState!.Shields.Count == 0), "Ten seconds of sustained post-destruction gameplay retain cleanup");
    }

    private async Task ShieldContactWeapons()
    {
        Host.Items.RemovePlayer(2);
        Check(Host.Items.Grant(Host.World, 2, HeldItem.Shield), "Contact weapons use selected rear armor");
        await Frames(120);
        var items = Host.Items;
        foreach (var local in new[] { new N.Vector3(0, .25f, 4), new(-2.4f, .25f, 2.5f), new(2.4f, .25f, 2.5f) })
        {
            var shield = items.Shields.Single(); float hp = Host.World.GetVehicle(2).Damage.CurrentHP;
            ulong token = items.TokenHighWater;
            var shots = Enumerable.Range(0, 2).Select(_ => new MissileState(++token, 1, _positions[2] + local,
                N.Vector3.Normalize(new N.Vector3(0, .25f, 2.5f) - local) * 120, 300)
                { EmpClaw = new(Host.World.GetVehicle(1).LifeId, 2, Host.World.GetVehicle(2).LifeId, Host.World.State.Tick + 300) }).ToArray();
            items.Restore(new(items.Revision + 1, Host.Snapshot(), items.Slots, shots, [], shields: items.Shields,
                discardRevision: items.DiscardRevision), items.Revision + 1, token);
            await Until(() => items.Missiles.Count == 0, "Two simultaneous EMP rounds hit native armor");
            await Frames(90);
            Check(items.Shields.Single() == shield && Host.World.GetVehicle(2).Damage.CurrentHP == hp &&
                _arenas.All(a => a.Driver.ItemState!.EmpAttachments.Count == 0), $"EMP denied at {local} with no HP loss or replicated attachment");
        }
        foreach (float z in new[] { 3.8f, -3f })
        {
            float hp = Host.World.GetVehicle(2).Damage.CurrentHP, armorHp = items.Shields.Single().HP;
            var mine = new ProxyMineState(items.TokenHighWater + 1, 1, _positions[2] + new N.Vector3(0, -.2f, z),
                new(0, 0, z > 0 ? -8 : 8), N.Vector3.UnitY, 0);
            items.Restore(new(items.Revision + 1, Host.Snapshot(), items.Slots, [], [], mines: [mine], shields: items.Shields,
                discardRevision: items.DiscardRevision), items.Revision + 1, mine.Id);
            await Until(() => items.Mines.Count == 0, "Mine detonates through native contact", 180);
            await Frames(90);
            Check(z > 0 ? Host.World.GetVehicle(2).Damage.CurrentHP == hp && items.Shields.Single().HP == armorHp - 250 :
                Host.World.GetVehicle(2).Damage.CurrentHP == hp - 250 && items.Shields.Single().HP == armorHp,
                $"Mine {(z > 0 ? "rear armor" : "exposed front")} receives exactly one 250 HP application");
            Check(_arenas.All(a => a.Driver.ItemState!.Mines.Count == 0 && a.Driver.ItemState.Shields.SequenceEqual(items.Shields)),
                "Mine removal and armor damage converge across impaired peers");
        }
    }

    private void SeedShieldRound(HeldItem item, N.Vector3 target)
    {
        var items = Host.Items; ulong token = items.TokenHighWater + 1;
        var arc = new SalvoFlight(target + N.Vector3.UnitY * 2, target, 1, 2, 1, Host.World.GetVehicle(1).LifeId);
        var round = item == HeldItem.Salvo
            ? new MissileState(token, 1, arc.At(1), arc.VelocityAt(1), SalvoFlight.LifetimeTicks - 1) { Arc = arc }
            : new MissileState(token, 1, target + N.Vector3.UnitZ * 1.5f, -N.Vector3.UnitZ * 120, 300);
        items.Restore(new(items.Revision + 1, Host.Snapshot(), items.Slots, [round], [], Host.Spawns?.States, items.Patches,
            items.OilContacts, Host.Spawns?.Balances, items.Mines, items.Shields, items.DiscardRevision, items.EmpAttachments), items.Revision + 1, token);
    }
}
