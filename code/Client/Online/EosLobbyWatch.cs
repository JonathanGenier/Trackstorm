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
    /// <param name="changed">Consumer of updated membership or closure.</param>
    /// <param name="retired">Service-confirmed departed members; cached snapshots cannot produce this signal.</param>
    internal EosLobbyWatch(LobbyInterface lobbies, ProductUserId user, object owner, string id, Action<Action> enqueue, Func<OnlineLobby?> read, Action<OnlineLobby?> changed, Action<OnlineProductUserId>? retired)
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
                        changed(read());
                    }
                });
            }
        });
        var member = default(AddNotifyLobbyMemberStatusReceivedOptions);
        _member = lobbies.AddNotifyLobbyMemberStatusReceived(ref member, owner, (ref LobbyMemberStatusReceivedCallbackInfo info) =>
        {
            bool closed = info.CurrentStatus == LobbyMemberStatus.Closed || (info.TargetUserId.Equals(user) && info.CurrentStatus is LobbyMemberStatus.Left or LobbyMemberStatus.Kicked or LobbyMemberStatus.Disconnected);
            string subject = info.TargetUserId.ToString();
            bool departed = info.CurrentStatus is LobbyMemberStatus.Left or LobbyMemberStatus.Kicked or LobbyMemberStatus.Disconnected;
            if (info.LobbyId.ToString() == id)
            {
                enqueue(() =>
                {
                    if (!_disposed)
                    {
                        changed(closed ? null : read());
                        if (departed)
                        {
                            retired?.Invoke(new OnlineProductUserId(subject));
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
}
