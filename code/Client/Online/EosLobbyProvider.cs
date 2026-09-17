using System.Globalization;
using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;

namespace Trackstorm.Client.Online;

/// <summary>Official EOS Lobbies adapter. SDK handles remain here and consumer callbacks run after platform Tick.</summary>
internal sealed class EosLobbyProvider : IOnlineLobbyProvider
{
    private readonly LobbyInterface _lobbies;
    private readonly ProductUserId _user;
    private readonly object _callbackOwner;
    private readonly Action<Action> _enqueue;
    private readonly EosPendingHandles _handles;
    private readonly HashSet<EosLobbyWatch> _watches = new();
    private bool _disposed;

    /// <summary>Adapts the authenticated platform lobby interface and main-thread callback queue.</summary>
    /// <param name="lobbies">Provider results or the owned native lobby interface.</param>
    /// <param name="user">Authenticated native product user.</param>
    /// <param name="callbackOwner">Platform owner used for native callback cleanup.</param>
    /// <param name="enqueue">Main-thread queue drained after native platform Tick.</param>
    /// <param name="handles">Platform-owned pending native resources.</param>
    internal EosLobbyProvider(LobbyInterface lobbies, ProductUserId user, object callbackOwner, Action<Action> enqueue, EosPendingHandles handles)
    {
        _lobbies = lobbies;
        _user = user;
        _callbackOwner = callbackOwner;
        _enqueue = enqueue;
        _handles = handles;
    }

    /// <inheritdoc />
    public void Search(Action<IReadOnlyList<OnlineLobby>, string?> completed)
    {
        Find(null, (search, failure) =>
        {
            var rows = new List<OnlineLobby>();
            if (search is not null)
            {
                var count = default(LobbySearchGetSearchResultCountOptions);
                for (uint i = 0; i < search.GetSearchResultCount(ref count); i++)
                {
                    var options = new LobbySearchCopySearchResultByIndexOptions { LobbyIndex = i };
                    if (search.CopySearchResultByIndex(ref options, out var details) == Result.Success)
                    {
                        try
                        {
                            var lobby = Read(details);
                            if (lobby is not null)
                            {
                                rows.Add(lobby);
                            }
                        }
                        finally
                        {
                            details.Release();
                        }
                    }
                }
            }

            completed(rows, failure);
        });
    }

    /// <inheritdoc />
    public void Create(OnlineLobby lobby, Action<OnlineLobby?, string?> completed)
    {
        if (_disposed)
        {
            completed(null, "Online services unavailable.");
            return;
        }

        // Creation is initially hidden; publishing the complete schema is one update before advertising.
        var options = new CreateLobbyOptions
        {
            LocalUserId = _user,
            MaxLobbyMembers = 8,
            PermissionLevel = LobbyPermissionLevel.Inviteonly,
            BucketId = OnlineLobby.CurrentProtocol,
            DisableHostMigration = false,
            AllowInvites = false,
            PresenceEnabled = false,
            EnableRTCRoom = false,
            EnableJoinById = false,
            RejoinAfterKickRequiresInvite = true,
        };
        _lobbies.CreateLobby(ref options, _callbackOwner, (ref CreateLobbyCallbackInfo info) =>
        {
            string id = info.LobbyId?.ToString() ?? string.Empty;
            Result result = info.ResultCode;
            Dispatch(() =>
            {
                if (result != Result.Success)
                {
                    completed(null, Failure(result));
                    return;
                }

                Write(lobby with { Id = id }, true, (created, failure) =>
                {
                    if (failure is not null)
                    {
                        // Preserve the created identity so the coordinator can retry a failed destroy.
                        completed(lobby with { Id = id }, failure);
                    }
                    else
                    {
                        completed(created, null);
                    }
                });
            });
        });
    }

    /// <inheritdoc />
    public void Join(string id, Action<OnlineLobby?, string?> completed) => JoinKnown(id, false, completed);

    /// <inheritdoc />
    public void Update(OnlineLobby lobby, Action<OnlineLobby?, string?> completed) => Write(lobby, false, completed);

    /// <inheritdoc />
    public void ConfirmMembership(string id, Action<bool> completed)
    {
        var options = new UpdateLobbyModificationOptions { LocalUserId = _user, LobbyId = id };
        if (_disposed || _lobbies.UpdateLobbyModification(ref options, out var modification) != Result.Success)
        {
            completed(false);
            return;
        }

        var lease = _handles.Retain(modification.Release);
        var attribute = new LobbyModificationAddMemberAttributeOptions
        {
            Attribute = new AttributeData { Key = "coordination", Value = new AttributeDataValue { AsUtf8 = Guid.NewGuid().ToString("N") } },
            Visibility = LobbyAttributeVisibility.Private,
        };
        if (modification.AddMemberAttribute(ref attribute) != Result.Success)
        {
            lease.Dispose();
            completed(false);
            return;
        }

        var update = new UpdateLobbyOptions { LobbyModificationHandle = modification };
        _lobbies.UpdateLobby(ref update, _callbackOwner, (ref UpdateLobbyCallbackInfo info) =>
        {
            bool accepted = info.ResultCode == Result.Success;
            Dispatch(lease, () => completed(accepted));
        });
    }

    /// <inheritdoc />
    public void Resume(string id, Action<OnlineLobby?, string?> completed)
    {
        if (_disposed)
        {
            completed(null, "Session unavailable");
            return;
        }

        var current = ReadCurrent(id);
        if (current?.MemberIds.Contains(new OnlineProductUserId(_user.ToString())) == true)
        {
            ConfirmMembership(id, accepted =>
            {
                if (accepted)
                {
                    completed(ReadCurrent(id), null);
                }
                else
                {
                    // A cached member list can outlive service membership after a network loss.
                    JoinKnown(id, true, completed);
                }
            });
            return;
        }

        JoinKnown(id, true, completed);
    }

    /// <inheritdoc />
    public void SetJoinable(string id, bool open, Action<OnlineLobby?, string?> completed)
    {
        var current = _disposed ? null : ReadCurrent(id);
        if (current is null)
        {
            completed(null, "Lobby closed or not found.");
            return;
        }

        Write(current with { Open = open }, false, completed, true);
    }

    /// <inheritdoc />
    public void Leave(string id, bool destroy, Action<string?> completed)
    {
        if (_disposed)
        {
            completed("Online services unavailable.");
            return;
        }

        if (destroy)
        {
            var options = new DestroyLobbyOptions { LocalUserId = _user, LobbyId = id };
            _lobbies.DestroyLobby(ref options, _callbackOwner, (ref DestroyLobbyCallbackInfo info) =>
            {
                Result result = info.ResultCode;
                Dispatch(() => completed(result is Result.Success or Result.NotFound ? null : Failure(result)));
            });
        }
        else
        {
            var options = new LeaveLobbyOptions { LocalUserId = _user, LobbyId = id };
            _lobbies.LeaveLobby(ref options, _callbackOwner, (ref LeaveLobbyCallbackInfo info) =>
            {
                Result result = info.ResultCode;
                Dispatch(() => completed(result is Result.Success or Result.NotFound ? null : Failure(result)));
            });
        }
    }

    /// <inheritdoc />
    public IDisposable Watch(string id, Action<OnlineLobby?, OnlineLobbyUpdate> changed, Action<OnlineProductUserId>? retired = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var watch = new EosLobbyWatch(_lobbies, _user, _callbackOwner, id, _enqueue, () => ReadCurrent(id), changed, retired);
        _watches.Add(watch);
        watch.Removed = () => _watches.Remove(watch);
        return watch;
    }

    /// <inheritdoc />
    public void Promote(string id, OnlineProductUserId member, Action<string?> completed)
    {
        var options = new PromoteMemberOptions { LobbyId = id, LocalUserId = _user, TargetUserId = ProductUserId.FromString(member.Value) };
        _lobbies.PromoteMember(ref options, _callbackOwner, (ref PromoteMemberCallbackInfo info) =>
        {
            Result result = info.ResultCode;
            _enqueue(() =>
            {
                if (!_disposed)
                {
                    completed(result == Result.Success ? null : Failure(result));
                }
            });
        });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var watch in _watches.ToArray())
        {
            watch.Dispose();
        }

        // In-flight handles belong to the platform, not this logical provider.
        // Queued completions release them even when consumer delivery is canceled.
    }

    private static string? Attribute(LobbyDetails details, string key)
    {
        var options = new LobbyDetailsCopyAttributeByKeyOptions { AttrKey = key };
        return details.CopyAttributeByKey(ref options, out var attribute) == Result.Success ? attribute?.Data?.Value.AsUtf8?.ToString() : null;
    }

    private static OnlineLobby? Read(LobbyDetails details)
    {
        var options = default(LobbyDetailsCopyInfoOptions);
        if (details.CopyInfo(ref options, out var copied) != Result.Success || copied is not { } info || info.MaxMembers != 8 || info.AvailableSlots > 8)
        {
            return null;
        }

        string name = Attribute(details, "name") ?? string.Empty;
        if (!ulong.TryParse(Attribute(details, "session"), NumberStyles.None, CultureInfo.InvariantCulture, out ulong session))
        {
            return null;
        }

        string? access = Attribute(details, "access");
        if (access is not ("Public" or "Locked"))
        {
            return null;
        }

        var countOptions = default(LobbyDetailsGetMemberCountOptions);
        var members = new List<OnlineProductUserId>();
        for (uint i = 0; i < details.GetMemberCount(ref countOptions); i++)
        {
            var memberOptions = new LobbyDetailsGetMemberByIndexOptions { MemberIndex = i };
            var member = details.GetMemberByIndex(ref memberOptions);
            if (member?.IsValid() == true)
            {
                members.Add(new OnlineProductUserId(member.ToString()));
            }
        }

        return new OnlineLobby(info.LobbyId.ToString(), name, new OnlineProductUserId(info.LobbyOwnerUserId.ToString()), session, access == "Locked" ? LobbyAccess.Locked : LobbyAccess.Public, (int)(info.MaxMembers - info.AvailableSlots), 8, info.BucketId.ToString(), Attribute(details, "open") == "1", access == "Locked" ? LobbyCredential.Parse(Attribute(details, "verifier") ?? string.Empty) : null) { MemberIds = members.ToArray(), GameplayHost = new OnlineProductUserId(Attribute(details, "gameHost") ?? info.LobbyOwnerUserId.ToString()), AuthorityEpoch = ulong.TryParse(Attribute(details, "epoch"), out ulong epoch) && epoch > 0 ? epoch : 1 };
    }

    private static string Failure(Result result) => result switch
    {
        Result.LobbyTooManyPlayers => "Lobby full.",
        Result.NotFound => "Lobby closed or not found.",
        _ => $"EOS lobby service failure ({result}). Check connectivity and Lobby client-policy permissions; refresh or retry.",
    };

    private void Dispatch(Action action) => Dispatch(null, action);

    private void Dispatch(IDisposable? handle, Action action) => _enqueue(() =>
    {
        try
        {
            if (!_disposed)
            {
                action();
            }
        }
        finally
        {
            handle?.Dispose();
        }
    });

    private OnlineLobby? ReadCurrent(string id)
    {
        var options = new CopyLobbyDetailsHandleOptions { LocalUserId = _user, LobbyId = id };
        if (_lobbies.CopyLobbyDetailsHandle(ref options, out var details) != Result.Success)
        {
            return null;
        }

        try
        {
            return Read(details);
        }
        finally
        {
            details.Release();
        }
    }

    private void Find(string? id, Action<LobbySearch?, string?> completed)
    {
        if (_disposed)
        {
            completed(null, "Online services unavailable.");
            return;
        }

        var create = new CreateLobbySearchOptions { MaxResults = LobbyInterface.MAX_SEARCH_RESULTS };
        Result result = _lobbies.CreateLobbySearch(ref create, out var search);
        if (result != Result.Success)
        {
            completed(null, Failure(result));
            return;
        }

        if (id is not null)
        {
            var byId = new LobbySearchSetLobbyIdOptions { LobbyId = id };
            result = search.SetLobbyId(ref byId);
        }
        else
        {
            var parameter = new LobbySearchSetParameterOptions { Parameter = new AttributeData { Key = LobbyInterface.SEARCH_BUCKET_ID, Value = new AttributeDataValue { AsUtf8 = OnlineLobby.CurrentProtocol } }, ComparisonOp = ComparisonOp.Equal };
            result = search.SetParameter(ref parameter);
            if (result == Result.Success)
            {
                var available = new LobbySearchSetParameterOptions { Parameter = new AttributeData { Key = "open", Value = new AttributeDataValue { AsUtf8 = "1" } }, ComparisonOp = ComparisonOp.Equal };
                result = search.SetParameter(ref available);
            }
        }

        if (result != Result.Success)
        {
            search.Release();
            completed(null, Failure(result));
            return;
        }

        var lease = _handles.Retain(search.Release);
        try
        {
            var find = new LobbySearchFindOptions { LocalUserId = _user };
            search.Find(ref find, _callbackOwner, (ref LobbySearchFindCallbackInfo info) =>
            {
                Result found = info.ResultCode;
                Dispatch(lease, () => completed(found == Result.Success ? search : null, found == Result.Success ? null : Failure(found)));
            });
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    private void JoinKnown(string id, bool resume, Action<OnlineLobby?, string?> completed)
    {
        Find(id, (search, failure) =>
        {
            if (search is null)
            {
                completed(null, failure);
                return;
            }

            var copy = new LobbySearchCopySearchResultByIndexOptions { LobbyIndex = 0 };
            if (search.CopySearchResultByIndex(ref copy, out var details) != Result.Success)
            {
                completed(null, "Lobby closed or not found.");
                return;
            }

            var lease = _handles.Retain(details.Release);
            bool started = false;
            try
            {
                var lobby = Read(details);
                if (lobby is null || !lobby.Compatible || (!resume && !lobby.Joinable))
                {
                    completed(null, lobby is { Compatible: false } ? "Build/protocol incompatible." : "Lobby full or closed.");
                    return;
                }

                var options = new JoinLobbyOptions { LocalUserId = _user, LobbyDetailsHandle = details, PresenceEnabled = false };
                _lobbies.JoinLobby(ref options, _callbackOwner, (ref JoinLobbyCallbackInfo info) =>
                {
                    Result result = info.ResultCode;
                    Dispatch(lease, () =>
                    {
                        var joined = result == Result.Success ? ReadCurrent(id) : null;
                        if (result == Result.Success && joined is null)
                        {
                            // Membership exists even if copying its metadata failed; retain cleanup ownership.
                            completed(lobby, "Lobby metadata unavailable. Refresh and retry.");
                        }
                        else
                        {
                            completed(joined, result == Result.Success ? null : Failure(result));
                        }
                    });
                });
                started = true;
            }
            finally
            {
                if (!started)
                {
                    lease.Dispose();
                }
            }
        });
    }

    private void Write(OnlineLobby lobby, bool initial, Action<OnlineLobby?, string?> completed, bool availability = false)
    {
        if (_disposed)
        {
            completed(null, "Online services unavailable.");
            return;
        }

        var current = ReadCurrent(lobby.Id);
        if (!initial && current?.Owner.Equals(new OnlineProductUserId(_user.ToString())) != true)
        {
            completed(null, "Only the host can rename this lobby.");
            return;
        }

        var options = new UpdateLobbyModificationOptions { LocalUserId = _user, LobbyId = lobby.Id };
        Result result = _lobbies.UpdateLobbyModification(ref options, out var modification);
        if (result != Result.Success)
        {
            completed(null, Failure(result));
            return;
        }

        var lease = _handles.Retain(modification.Release);
        bool started = false;
        try
        {
            var attributes = new Dictionary<string, string>();
            if (initial || availability)
            {
                attributes["open"] = lobby.Open ? "1" : "0";
            }

            if (!availability)
            {
                attributes["name"] = lobby.Name;
                attributes["gameHost"] = lobby.HostIdentity.Value;
                attributes["epoch"] = lobby.AuthorityEpoch.ToString(CultureInfo.InvariantCulture);
            }

            if (initial)
            {
                attributes["session"] = lobby.Session.ToString(CultureInfo.InvariantCulture);
                attributes["access"] = lobby.Access.ToString();
                if (lobby.Credential is not null)
                {
                    attributes["verifier"] = lobby.Credential.ExportVerifier();
                }
            }

            foreach (var pair in attributes)
            {
                var attribute = new LobbyModificationAddAttributeOptions { Attribute = new AttributeData { Key = pair.Key, Value = new AttributeDataValue { AsUtf8 = pair.Value } }, Visibility = LobbyAttributeVisibility.Public };
                result = modification.AddAttribute(ref attribute);
                if (result != Result.Success)
                {
                    completed(null, Failure(result));
                    return;
                }
            }

            if (initial || availability)
            {
                // Hide active matches through discovery filtering, not an invitation requirement that blocks retained resumes.
                var permission = new LobbyModificationSetPermissionLevelOptions { PermissionLevel = LobbyPermissionLevel.Publicadvertised };
                result = modification.SetPermissionLevel(ref permission);
                if (result != Result.Success)
                {
                    completed(null, Failure(result));
                    return;
                }
            }

            var update = new UpdateLobbyOptions { LobbyModificationHandle = modification };
            _lobbies.UpdateLobby(ref update, _callbackOwner, (ref UpdateLobbyCallbackInfo info) =>
            {
                Result updated = info.ResultCode;
                Dispatch(lease, () =>
                {
                    var currentLobby = updated == Result.Success ? ReadCurrent(lobby.Id) : null;
                    completed(currentLobby, updated == Result.Success ? currentLobby is null ? "Lobby metadata unavailable. Refresh and retry." : null : Failure(updated));
                });
            });
            started = true;
        }
        finally
        {
            if (!started)
            {
                lease.Dispose();
            }
        }
    }
}
