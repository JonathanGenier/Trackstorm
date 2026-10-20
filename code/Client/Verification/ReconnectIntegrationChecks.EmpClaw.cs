using Godot;
using Trackstorm.Client.Items;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Verification;

public sealed partial class ReconnectIntegrationChecks
{
    private static bool EmpRecovery => OS.GetCmdlineUserArgs().Contains("--emp-recovery");
    private EmpClawAttachment[] _empOriginal = [];
    private int _empResumes;

    private EmpClawAttachments EmpViews(int peer) => _arenas[peer].FindChildren("*", "Node", true, false).OfType<EmpClawAttachments>().Single();

    private void VerifyEmpResume()
    {
        var state = _arenas[1].Driver.ItemState!;
        _empResumes++;
        Require(state.Events.Count == 0 && _arenas[1].Bodies[_player] == _originalBody, "EMP resume reuses the body without replaying impacts.");
        Require(state.EmpAttachments.SequenceEqual(_empResumes == 1 ? _empOriginal : []), "EMP resume keeps exact anchors and original individual deadlines, or their offline expiry.");
        foreach (var vehicle in state.World.Vehicles)
        {
            Require(vehicle.State.Movement.EnginePowerScale == (_empResumes == 1 ? .2f : 1), "Checkpoint restores the correct power state before prediction.");
        }
        GD.Print($"EMP reconnect {_empResumes}: tick={state.World.Tick}, attachments={state.EmpAttachments.Count}, remaining=[{string.Join(',', state.EmpAttachments.Select(c => c.ExpiresAtTick - state.World.Tick))}]; exact anchors/deadlines, no replay.");
    }

    private void InterruptEmpPeer(double seconds)
    {
        _resumeAt = _elapsed + seconds;
        _gateways[0].Disconnect(_host.Authority!.Peers.Keys.Single());
        _gateways[1].Disconnect(_client.ServerPeer);
        _host.Pump(0); _client.Pump(0);
    }

    private void AdvanceEmpRecovery()
    {
        var host = _arenas[0].Driver.Host!;
        if (_stage == 600)
        {
            foreach (var gateway in _gateways) { gateway.ConfigureSimulation(new(100, 15, 3)); }
            EmpClawVerificationState.Seed(host, [1, _player], 3);
            _empOriginal = host.Items.EmpAttachments.ToArray();
            _stage = 601;
        }
        else if (_stage == 601 && EmpViews(1).Views.Count == 6 && _arenas[1].Driver.LocalState!.Movement.EnginePowerScale == .2f)
        {
            InterruptEmpPeer(5); _stage = 602;
        }
        else if (_stage == 602 && _empResumes == 1 && _arenas[1].Driver.IsActive && EmpViews(1).Views.Count == 6)
        {
            Require(EmpViews(1).Views.All(v => v.Inflation > 2.5f), "Reconnect rebuilds inflated active noses without entry replay.");
            Require(_arenas[1].Driver.LocalState!.Movement.EnginePowerScale == .2f, "Resumed client prediction remains suppressed.");
            InterruptEmpPeer(32); _stage = 603;
        }
        else if (_stage == 603 && _empResumes == 2 && _arenas[1].Driver.IsActive)
        {
            Require(host.Items.EmpAttachments.Count == 0 && _arenas[1].Driver.LocalState!.Movement.EnginePowerScale == 1, "Offline expiry leaves no permanent debuff.");
            Require(EmpViews(1).Views.Count == 0 && EmpViews(1).ReleasedCount == 0 && EmpViews(0).Views.Count == 0, "Reconnect after expiry leaves no stale visual resources.");
            GD.Print("EMP recovery passed: six staggered attachments, five-second interruption, 32-second offline expiry, two impaired UDP resyncs and exact native cleanup. Initial attachments and trusted identity are fixtures.");
            Cleanup(); _finished = true;
        }
    }
}
