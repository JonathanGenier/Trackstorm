# EOS Lobby Discovery and Session Coordination

EOS owns discovery, identity and P2P routing. The separate [Cloudflare authority lease](authority-leases.md), scoped to one Durable Object per private session, fences host transitions without moving gameplay or player reservations into the service. Its public endpoint is bundled with the game.

## Browser, Names and Access

Normal multiplayer opens one browser for compatible **Public** and **Locked / Private** lobbies, without address or port entry. Each row has a name, online member count out of eight, access indicator and Join action. Counts describe EOS membership, not an authoritative gameplay roster. Names truncate visually before the separate count/access/action columns. A refresh replaces the previous cache; membership updates replace entries by logical EOS lobby ID. Rows sort by `OrdinalIgnoreCase` name and then ordinal lobby ID. Search is a local ordinal, case-insensitive substring of the lobby name; clearing it restores all compatible cached results. Updated names stop matching their previous spelling. Native search retrieves up to EOS's 200-result limit in the compatibility bucket; this prototype does not implement global pagination beyond that service limit.

Creation and rename share canonical name rules: retain Unicode letters/digits, apostrophe, hyphen and underscore; collapse whitespace; discard other characters including markup and directional controls; trim and truncate to 48 Unicode scalars without splitting surrogate pairs. Empty canonical names are rejected. The host chooses Public or Locked and supplies a 4–64 character access code for Locked lobbies. The credential edit is masked and cleared after submission. Public joins never request a code; Locked joins display a separate masked prompt. Incorrect codes do not join EOS through the normal coordinator and cannot admit a player through the host transport binding.

Only the host may rename an active lobby. The name attribute changes in place: EOS lobby ID, Trackstorm session lifetime, access policy, verifier, authoritative roster and Ready state are preserved. Current members receive notifications; browsers see the new name on refresh. Invalid names and non-host rename requests receive recoverable errors.

The multiplayer panel owns the visible EOS status and login/retry/logout controls. Distinct initializing, authenticating, online, configuration-error, authentication-failure and unavailable states explain disabled hosting next to Host Game. Configuration guidance distinguishes an invalid embedded default from an explicitly invalid `TRACKSTORM_EOS_CONFIG` override; a valid identity and coordinator enable hosting automatically. Pending cleanup also disables hosting with its current reason. Direct-IP fallback stays at the top; the menu scrolls when recovery guidance needs more space. There is no separate floating identity panel.

## Lobbies Decision and Minimal Metadata

EOS **Lobbies** provides the persistent group, owner-controlled attributes, bounded membership and update notifications needed here. Sessions would add another coordination primitive without improving this browser/rename flow. The pinned SDK's Create, Update, Search, Join, Leave and Destroy APIs implement the lifecycle. See the [official lobby interface](https://dev.epicgames.com/docs/epic-online-services/multiplayer/lobbies-and-sessions/lobby-interface).

Both access modes use EOS `Publicadvertised` permission so they appear together. “Private” means a game-level access code, not EOS invite-only visibility. A new lobby starts hidden until its complete metadata is published. Invites, presence and RTC are disabled. EOS ownership migration is enabled for coordination; it never grants Trackstorm gameplay authority. EOS supplies owner, membership count and eight-member capacity. Custom attributes contain canonical `name`, Trackstorm `session`, `access`, a salted `verifier` for Locked lobbies, agreed `gameHost`/`epoch` routing, and `open` admission availability. A private per-member `coordination` nonce supports bounded service membership proofs without publishing credentials. Gameplay checkpoints remain outside lobby metadata. Indexed bucket `trackstorm-lobby-11` identifies compatibility. No Ready, phase, HP, vehicle, score or item state is stored in attributes. When an attached host driver enters an arena, its authority-derived `open` attribute excludes it from normal discovery; returning to lobby opens discovery again. EOS permission remains Publicadvertised so retained players can search by lobby ID and use ordinary Join while fresh gameplay admission is closed. The native-invite-only JoinLobbyById API is not used for resume. Core independently rejects invalid admission if a publication races discovery.

Provider notifications merge against the established Trackstorm routing fence. A lower `epoch` cannot replace the current `gameHost` or authority epoch. At the current epoch, a different `gameHost` is treated as conflicting and the established route is retained. Name, ownership and availability can refresh from metadata/ownership notifications. An EOS member-status event authorizes only its explicit target transition: Joined adds that target, while remote Left/Kicked/Disconnected removes that target. Every unrelated admitted member is preserved even when the accompanying lobby-details copy omits it. Restart locators retain the last established epoch and host so a delayed snapshot cannot route a resumed process to retired authority. A higher epoch remains eligible as the result of the independent Trackstorm migration flow; EOS ownership alone still cannot create gameplay authority.

Restart routing can bypass stale EOS `gameHost`/`epoch` metadata through an authenticated read of the existing [authority lease](authority-leases.md), using the opaque read-only address in new arena resume locators. EOS still validates membership; the trusted route selects only the P2P target and expected epoch. Core Resume still validates retained identity and generation. The coordinator never reconnects a former host to itself or depends on successful EOS promotion to establish gameplay authority. Later metadata refreshes retain the resolved epoch/host under the same stale-routing protections.

## Lightweight Credential Handling

Each Locked lobby creates a random 128-bit salt and a 256-bit PBKDF2-SHA256 verifier with 100,000 iterations. Raw codes are not retained in provider state, searchable metadata, row view models or normal diagnostic formatting. Verification metadata is public to support browser admission without a backend; short codes remain susceptible to offline guessing. This is intentional lightweight lobby access control, not account authentication or a security boundary. Renaming does not rotate the verifier. Closing or replacing membership invalidates transport admission and identity bindings; a new lobby creates a fresh verifier. Immutable discovery snapshots never grant authority by themselves.

## Authority, Transport and Identity

`EosIdentityService` supplies the existing authenticated platform to `EosLobbyProvider`. `OnlineLobbyCoordinator` owns discovery, one membership lifetime, subscriptions, safe status and cancellation. `OnlineSessionBinding` and `DevelopmentSession.OpenOnline` connect a separately established authenticated gateway to the existing `LobbyNetworkDriver` and arena presentation. Production composition automatically creates `EosP2pTransport` when online membership becomes active. Its authenticated handshake runs before the existing lobby admission, Ready, Start, arena and Return flow. Provider-independent tests exercise the real packet framing and lobby driver over a fake native packet provider.

The transport adapter must obtain the remote PUID from its authenticated connection, never from a player-supplied identity claim. Before normal Join processing, the host binding checks online membership and the Locked verifier. Only then can `LobbyNetworkDriver` call `LobbyAuthority.Join`. Core assigns its normal monotonic numeric PlayerId. Client keeps an explicit online-identity-to-PlayerId map; PUID is not a gameplay ID and no EOS SDK type enters Core. A departed online member disconnects its bound peer. Core removes it immediately in the lobby, while any active-match departure retains the player for authenticated resume until Return. Notifications never set Ready or session phase. The explicit Direct-IP developer fallback uses the same phase policy.

## Lifetime and Verification

Native callbacks enqueue managed work for delivery after platform Tick. Disposable subscriptions reject already-queued events; a separate membership generation rejects notifications from an earlier visit even to the same lobby. Operation generations reject stale create/join/rename callbacks and search request generations reject obsolete refreshes. Late successful membership operations are explicitly left/destroyed. Leaving or closing drops the departed membership snapshot from the browser until fresh discovery arrives. Failed close retains cleanup ownership, blocks replacement and exposes Leave for retry. Search and membership operations have a monotonic 60-second deadline. Disposal removes notifications and suppresses consumer delivery. Async search, join-details and modification handles remain platform-owned through completion; teardown releases still-pending caller-owned handles while the platform remains valid, then releases the platform and discards canceled callback registrations before SDK shutdown. Process exit or connectivity loss can still require EOS's service-side departure detection rather than a confirmed asynchronous close acknowledgment.

Lobby-attribute, ownership-promotion and actual membership notifications have
different authority. Attribute and Promoted refreshes may carry a temporarily
incomplete cached member list, so they can update name, owner, access, availability
and routing but cannot remove an admitted member or tear down P2P. Joined adds only
the callback target; remote Left/Kicked/Disconnected removes and disconnects only
the callback target. Arbitrary omissions from the accompanying details copy never
remove unrelated admitted members. Explicit local Left/Kicked/Disconnected or
lobby Closed status still enters the normal recovery/closure path. Actual Left/Kicked/Disconnected callbacks update membership. The existing local proof-expiry/retirement boundary permanently freezes a host and stops/releases its Cloudflare lease before further renewal; remote promotion still requires the [trusted lease service](authority-leases.md) fence. Temporary proof delays inside the validity window and ordinary client/P2P loss do not release a healthy host lease. Healthy clients activate lease reads only when the migration state suspects host loss or needs fencing confirmation. Promotion is not
retirement evidence and never grants Trackstorm gameplay authority. Private
coordination writes and EOS ownership migration therefore cannot churn a healthy
Public or Locked gameplay session.

Developer Options exposes credential-free coordination diagnostics: EOS lobby ID,
Trackstorm SessionId, access mode, fingerprinted local/owner/gameplay-host
identities and member list, metadata/ownership/joined/departed/retirement callback counters, proof
request/result counts and lease age, availability updates, recovery state and
resume-locator generation. Passwords, verifier bytes, raw PUIDs and tokens are
never formatted.

`OnlineLobbyTests` exercises fake-provider coordination plus the real lobby driver without loading native EOS or Godot: access mapping, Unicode names, search/order/rename, identity separation, capacity, credential failure/expiry, wrong-code roster exclusion, lifecycle cleanup, delayed operations, obsolete subscriptions, timeouts and close retry. `check-online-lobby.ps1 -GodotPath <exe>` exercises production browser controls using a fake provider. `-Visual` saves browser/prompt/renamed-host images under `.godot/online-lobby-checks`. Existing `check-lobby.ps1` exercises eight actual UDP sessions and Ready/Start/Return regression behavior. These checks do not establish authenticated EOS service behavior or separate-PC interoperability. The real-device procedure remains in [EOS development setup](../eos-development.md).

See [reconnection and session resume](reconnection.md) for authenticated match-long retention, rebind and checkpoint semantics.

[Feature index](README.md)

The [Event Log](event-log.md) records local online create/join/leave/close outcomes and safe failure categories. It never copies credentials, provider error text or platform identifiers.
