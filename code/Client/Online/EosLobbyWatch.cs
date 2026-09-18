using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;

namespace Trackstorm.Client.Online;

/// <summary>One membership's native subscriptions; queued notifications become inert immediately on disposal.</summary>
internal sealed class EosLobbyWatch : IDisposable
{
    private readonly LobbyInterface _lobbies;
    private readonly ulong _update;
    private readonly ulong _member;
    private bool _disposed;

    /// <summary>Registers lobby and member notifications for one logical membership lifetime.</summary>
    /// <param name="lobbies">Provider results or the owned native lobby interface.</param>
    /// <param name="user">Authenticated native product user.</param>
    /// <param name="owner">Platform callback registration owner.</param>
    /// <param name="id">Logical EOS lobby identity.</param>
    /// <param name="enqueue">Main-thread queue drained after native platform Tick.</param>
    /// <param name="read">Copies current native membership into plain Client data.</param>
    /// <param name="changed">Consumer of metadata, ownership or service-confirmed membership changes.</param>
    /// <param name="retired">Service-confirmed departed members; cached snapshots cannot produce this signal.</param>
    internal EosLobbyWatch(LobbyInterface lobbies, ProductUserId user, object owner, string id, Action<Action> enqueue, Func<OnlineLobby?> read, Action<OnlineLobby?, OnlineLobbyUpdate> changed, Action<OnlineProductUserId>? retired)
    {
        _lobbies = lobbies;
        var update = default(AddNotifyLobbyUpdateReceivedOptions);
        _update = lobbies.AddNotifyLobbyUpdateReceived(ref update, owner, (ref LobbyUpdateReceivedCallbackInfo info) =>
        {
            if (info.LobbyId.ToString() == id)
            {
                enqueue(() =>
                {
                    if (!_disposed)
                    {
                        changed(read(), new(OnlineLobbyUpdateKind.Metadata));
                    }
                });
            }
        });
        var member = default(AddNotifyLobbyMemberStatusReceivedOptions);
        _member = lobbies.AddNotifyLobbyMemberStatusReceived(ref member, owner, (ref LobbyMemberStatusReceivedCallbackInfo info) =>
        {
            string subject = info.TargetUserId.ToString();
            var target = new OnlineProductUserId(subject);
            OnlineLobbyUpdateKind kind = Classify(info.CurrentStatus, info.TargetUserId.Equals(user));
            bool departed = IsDeparture(info.CurrentStatus);
            if (info.LobbyId.ToString() == id)
            {
                enqueue(() =>
                {
                    if (!_disposed)
                    {
                        changed(kind == OnlineLobbyUpdateKind.Closure ? null : read(), new(kind, target));
                        if (departed)
                        {
                            retired?.Invoke(target);
                        }

                    }
                });
            }
        });
        if (_update == 0 || _member == 0)
        {
            Dispose();
            throw new InvalidOperationException("EOS lobby notifications unavailable. Leave and retry.");
        }
    }

    /// <summary>Removes this disposed subscription from its provider owner.</summary>
    internal Action? Removed { get; set; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_update != 0)
        {
            _lobbies.RemoveNotifyLobbyUpdateReceived(_update);
        }

        if (_member != 0)
        {
            _lobbies.RemoveNotifyLobbyMemberStatusReceived(_member);
        }

        Removed?.Invoke();
    }

    /// <summary>Classifies EOS status callbacks without granting ownership promotion membership authority.</summary>
    /// <param name="status">Native status transition.</param>
    /// <param name="local">Whether the transition targets the subscribed local user.</param>
    /// <returns>The narrowest authority the callback has over local state.</returns>
    internal static OnlineLobbyUpdateKind Classify(LobbyMemberStatus status, bool local) => status switch
    {
        LobbyMemberStatus.Closed => OnlineLobbyUpdateKind.Closure,
        LobbyMemberStatus.Left or LobbyMemberStatus.Kicked or LobbyMemberStatus.Disconnected when local => OnlineLobbyUpdateKind.Closure,
        LobbyMemberStatus.Promoted => OnlineLobbyUpdateKind.Ownership,
        LobbyMemberStatus.Joined => OnlineLobbyUpdateKind.Joined,
        LobbyMemberStatus.Left or LobbyMemberStatus.Kicked or LobbyMemberStatus.Disconnected => OnlineLobbyUpdateKind.Departed,
        _ => OnlineLobbyUpdateKind.Metadata,
    };

    /// <summary>Only actual departure statuses provide service retirement evidence.</summary>
    /// <param name="status">Native status transition.</param>
    /// <returns>Whether the member actually left the EOS lobby.</returns>
    internal static bool IsDeparture(LobbyMemberStatus status) => status is LobbyMemberStatus.Left or LobbyMemberStatus.Kicked or LobbyMemberStatus.Disconnected;
}
