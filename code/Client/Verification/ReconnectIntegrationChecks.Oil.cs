using Godot;
using Trackstorm.Client.Items;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Verification;

public sealed partial class ReconnectIntegrationChecks
{
    private static bool OilFiringRecovery => OS.GetCmdlineUserArgs().Contains("--oil-firing-recovery");
    private readonly Dictionary<ulong, OilPatch[]> _oilBoundaries = new();
    private bool _oilResumeFire;
    private int _oilResumes;
    private ulong _oilDeadline;
    private ulong _oilToken;
    private bool _oilMeterObserved;

    private void CaptureOilBoundary()
    {
        var host = _arenas[0].Driver.Host!;
        _oilBoundaries[host.World.State.Tick] = host.Items.Patches.ToArray();
        if (_oilBoundaries.Count > 256) { _oilBoundaries.Remove(_oilBoundaries.Keys.Min()); }
    }

    private void StartOilAtReconnect()
    {
        if (!_oilResumeFire) { return; }
        Require(_arenas[0].Driver.RequestItemUse(), "Host begins ready Oil while observer reconnects.");
        _oilResumeFire = false;
    }

    private void VerifyOilResume()
    {
        var state = _arenas[1].Driver.ItemState!;
        Require(_oilBoundaries.TryGetValue(state.World.Tick, out var exact) && state.Patches.SequenceEqual(exact),
            $"Oil resume installs exact hazard, spring, flight, geometry and deadline at tick {state.World.Tick}.");
        var patch = state.Patches.Single();
        Require(patch.Pour is { Cancelled: false } && state.Events.Count == 0, "Resume retains emission without replaying use.");
        if (_oilResumes == 0)
            Require(state.World.Tick < patch.Pour!.StartTick + 60 && patch.Pour.Emitted is > 0 and < 40,
                "Observer reconnect checkpoint is genuinely inside the one-second firing window.");
        else
            Require(patch.Id == _oilToken && patch.ExpiresAtTick == _oilDeadline,
                "Returning firing owner keeps its original hazard and deadline.");
        _oilResumes++;
        GD.Print($"Oil firing reconnect {_oilResumes}: exact tick={state.World.Tick}, start={patch.Pour!.StartTick}, emitted={patch.Pour.Emitted}, flight={patch.Pour.Drops.Length}, deadline={patch.ExpiresAtTick}; no replay.");
    }

    private void InterruptOilPeer()
    {
        _gateways[0].Disconnect(_host.Authority!.Peers.Keys.Single());
        _gateways[1].Disconnect(_client.ServerPeer);
        _host.Pump(0); _client.Pump(0);
    }

    private void AdvanceOilRecovery()
    {
        var host = _arenas[0].Driver.Host!;
        if (_stage == 500)
        {
            Require(host.Items.Grant(host.World, 1, HeldItem.Oil), "Oil granted before observer recovery.");
            _stage = 501;
        }
        else if (_stage == 501 && _arenas[0].Bodies[1].Rack.IsOilReady(host.Items.Slots.Single(s => s.Vehicle == 1)))
        {
            _oilResumeFire = true;
            InterruptOilPeer();
            _stage = 502;
        }
        else if (_stage == 502 && _oilResumes == 1 && _arenas[1].Driver.IsActive)
        {
            var patch = host.Items.Patches.Single();
            _oilDeadline = patch.ExpiresAtTick; _oilToken = patch.Id;
            var jack = _arenas[1].Bodies[1].FindChildren("*", "", true, false).OfType<OilJack>().SingleOrDefault();
            if (jack is null || jack.MeterLevel is <= .02f or >= .98f) { return; }
            float expected = 1 - Math.Clamp((_arenas[1].Driver.Latest!.Tick - patch.Pour!.StartTick) / 60f, 0, 1);
            Require(Math.Abs(jack.MeterLevel - expected) < .15f && jack.Extension > .95f, "Recovered displayed meter and exposed head follow accepted burst.");
            _oilMeterObserved = true;
            GD.Print($"Oil recovered native meter: {jack.MeterLevel:F3}, accepted={expected:F3}, extension={jack.Extension:F3}.");
            _stage = 503;
        }
        else if (_stage is 503 or 507)
        {
            if (host.World.State.Tick < _oilDeadline)
            {
                var patch = host.Items.Patches.Single();
                Require(patch.Id == _oilToken && patch.ExpiresAtTick == _oilDeadline, "Recovery never changes Oil identity or expiry.");
                if (host.World.State.Tick > patch.Pour!.StartTick + 65)
                    Require(patch.Pour.Emitted == 40 && !patch.Pour.Cancelled, "Recovery emits forty parcels exactly once.");
                return;
            }
            Require(host.Items.Patches.Count == 0, "Authority removes the entire recovered hazard at its deadline.");
            if (host.World.State.Tick < _oilDeadline + 180) { return; }
            Require(_arenas.All(a => a.Driver.ItemState!.Patches.Count == 0 &&
                !a.FindChildren("*", "", true, false).OfType<OilHazardVisual>().Any()), "All peers remove Oil and bounded visual fade.");
            if (_stage == 503) { _stage = 504; return; }
            Require(_oilResumes == 2 && _oilMeterObserved, "Both active observer and firing-owner reconnect cases executed.");
            GD.Print("Oil firing recovery passed: mid-burst exact checkpoint and displayed meter, owner disconnect during emission, forty parcels per use, one unchanged 30-second hazard per activation and synchronized fade cleanup.");
            Cleanup(); _finished = true;
        }
        else if (_stage == 504)
        {
            Require(host.Items.Grant(host.World, _player, HeldItem.Oil), "Returning player gets Oil for owner interruption.");
            _stage = 505;
        }
        else if (_stage == 505 && _arenas[1].Driver.LocalItem is { } slot && _arenas[1].Bodies[_player].Rack.IsOilReady(slot))
        {
            Require(_arenas[1].Driver.RequestItemUse(), "Remote owner fires through production request.");
            _stage = 506;
        }
        else if (_stage == 506 && host.Items.Patches.SingleOrDefault()?.Pour is { Emitted: >= 8 and < 40 })
        {
            var patch = host.Items.Patches.Single(); _oilToken = patch.Id; _oilDeadline = patch.ExpiresAtTick;
            InterruptOilPeer(); _stage = 507;
        }
    }
}
