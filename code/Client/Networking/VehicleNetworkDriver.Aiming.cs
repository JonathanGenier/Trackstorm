using System.Numerics;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Networking.Transport;

namespace Trackstorm.Client.Networking;

internal sealed partial class VehicleNetworkDriver
{
    private IReadOnlyList<WeaponAimSolution> _acceptedAims = Array.Empty<WeaponAimSolution>();
    private ulong _aimSequence;
    private ulong _aimPublicationTick;
    private double _aimReceivedAt;
    private bool _publishedAims;

    /// <summary>Latest local camera intent, sampled through the existing fixed item/input path.</summary>
    internal Vector3? DesiredAim { get; set; }
    /// <summary>Current trusted solutions only; late/stalled samples cannot retain a firing marker.</summary>
    internal IReadOnlyList<WeaponAimSolution> AcceptedAims => _seconds() - _aimReceivedAt <= .3 ? _acceptedAims : Array.Empty<WeaponAimSolution>();

    private void SubmitAim()
    {
        if (!AllowsParticipation || DesiredAim is not Vector3 direction || !WeaponAim.IsDirection(direction) || LocalItem is not { } inventory || !WeaponAim.Supports(inventory.Active.Item)) { return; }
        ulong sequence = checked(++_aimSequence);
        if (Host is not null)
        {
            Host.AimItem(0, _session, inventory.Life, inventory.Active.Token, inventory.SelectionRevision, sequence, direction);
        }
        else if (sequence % HostVehicleSession.SnapshotInterval == 0)
        {
            Send(new(ServerPeer, ItemCodec.EncodeAim(_session, inventory.Life, inventory.Active.Token, inventory.SelectionRevision, sequence, direction), TransportDelivery.Unreliable));
        }
    }

    private void PublishAims()
    {
        _acceptedAims = Host!.Items.Aims;
        _aimReceivedAt = _seconds();
        if (Host.World.State.Tick % HostVehicleSession.SnapshotInterval != 0) { return; }
        if (_acceptedAims.Count == 0 && !_publishedAims) { return; }
        _publishedAims = _acceptedAims.Count > 0;
        byte[] bytes = ItemCodec.EncodeAims(_session, Host.World.State.Tick, Configuration.Revision, _acceptedAims);
        foreach (ulong peer in _assigned) { Send(new(peer, bytes, TransportDelivery.Unreliable)); }
    }

    private void ReceiveAim(TransportMessage message)
    {
        if (message.Delivery != TransportDelivery.Unreliable) { throw new ArgumentException("Aim requires replaceable delivery."); }
        if (Host is not null)
        {
            var request = ItemCodec.DecodeAim(message.Payload.Span);
            if (!Host.AimItem(message.RemotePeerId, request.Session, request.Life, request.Token, request.Selection, request.Sequence, request.Direction)) { RejectedPackets++; }
            return;
        }
        if (message.RemotePeerId != ServerPeer || !_receivedConfiguration || History is null || _awaitingCheckpoint) { throw new ArgumentException("Aim requires synchronized host authority."); }
        var publication = ItemCodec.DecodeAims(message.Payload.Span);
        if (publication.Session != _session || publication.Configuration != Configuration.Revision || publication.Tick <= _aimPublicationTick) { throw new ArgumentException("Stale aim publication."); }
        _aimPublicationTick = publication.Tick;
        _aimReceivedAt = _seconds();
        _acceptedAims = publication.Aims;
    }

    private void ResetAiming()
    {
        DesiredAim = null;
        _acceptedAims = Array.Empty<WeaponAimSolution>();
        _aimSequence = _aimPublicationTick = 0;
        _aimReceivedAt = double.NegativeInfinity;
        _publishedAims = false;
    }
}
