# Transport and Replicated-State Foundation

## Purpose

The networking foundation gives authoritative Core code a transport-facing seam without importing a native networking library. It also distinguishes opaque delivery from game-specific replication so transport choices cannot become gameplay-state ownership.

## How It Works

`Trackstorm.Core.Networking.Transport` defines `ITransportGateway`, opaque `TransportMessage` payloads with reliable/unreliable delivery, lifecycle events with stable reasons, nullable statistics and validated `NetworkSimulation` settings. The gateway accepts a validated `TransportEndpoint`: `DirectIp` carries an adapter-validated development address, while `PeerSession` carries opaque peer identity and session context. Core imports no provider types. The gateway supports provider-selected listen/connect, disconnect, poll, send/receive, statistics, simulation configuration and reusable stop. Core `TransportConnections` owns admission and valid transitions: a host reserves one of eight player slots, leaving at most seven remote peers connecting or connected. Pending peers reserve slots before native acceptance. Disconnect releases a slot; identifiers increase within a gateway lifetime and closed IDs never revive. These local peer IDs carry no gameplay/player identity or authority.

Client `GameNetworkingSocketsTransport` implements the boundary using the pinned standalone GameNetworkingSockets library and GnsSharp binding. `NetworkTransportNode` polls on Godot's main thread and disposes on scene exit. Gateways share one process-global native runtime on the same thread. Managed events run after native callbacks return, so consumer exceptions cannot unwind across native code. Aggregate connection state reports peer connectivity; `IsListening` separately reports host availability. Invalid addresses and failed listen/connect creation throw synchronously; asynchronous failures produce lifecycle events with stable reasons and diagnostic text. `Stop` closes peers/listener, drains callbacks and clears received payloads; `Dispose` also releases the shared runtime. The native module stays mapped for the process lifetime because P/Invoke caches function addresses; the last gateway releases native networking threads and sockets.

Reliable sends use ordered retransmission; unreliable sends use the independent no-retransmission path without reliable head-of-line delivery blocking. Both share network bandwidth. Each payload is bounded at 64 KiB and native send rejection is explicit. Receive polling copies and releases each native message, reads up to 32 messages per peer per poll and bounds the managed queue at 256 messages. Capacity overflow disconnects the peer with an explicit reason. Callers must consume messages regularly. Copied payloads remain valid after native memory is released; protocol consumers must account for peers closing after reception.

Statistics expose nullable round-trip ping and local/remote on-time delivery quality fractions. Loss is one minus quality and includes late packets; unknown samples stay null instead of reporting perfect connectivity. Network simulation acts on outbound native packets **process-wide**, including all local test gateways. Latency clamps to 0–5000 ms, average jitter to 0–1000 ms (native maximum twice the average), percentages to 0–100, and reorder delay to 0–5000 ms. Non-finite percentages are rejected. Simulation acts below retransmission, so reliable loss is recovered by the native library. Reset with `ConfigureSimulation(new NetworkSimulation())`; stopping one gateway does not change other gateways' process-wide test settings.

Development launches pass `-- --transport-host=0.0.0.0:27020` or `-- --transport-connect=127.0.0.1:27020` to Godot. IPv6 uses brackets, for example `[::1]:27020`. The options are mutually exclusive. The diagnostics HUD displays the first available sampled ping among connected peers when enabled, or unavailable when none has a sample. These options open the development lobby in host or join mode; `--player-name=Name` supplies the requested display name. Without either option the main scene opens the EOS multiplayer browser; Direct-IP Host/Join is an explicit developer fallback, initializing transport only when requested. `-- --local-practice` selects the local rigid-body arena. Lobby gameplay starts only after the host validates a Start Match intent.

`Trackstorm.Core.Networking.Replication` separately defines `SimulationStateMessage`. Its legacy version-one wire representation contains an envelope version followed by one versioned `InputFrame`, for 22 bytes total. It supports only states without registered vehicles and rejects a populated aggregate rather than silently discarding gameplay state. Complete archival vehicle serialization uses `VehicleSnapshotCodec`; live vehicle replication uses the compact binary `VehicleNetworkCodec` described in [vehicle networking](vehicle-networking.md). Reading the legacy message reconstructs a tick/input-only Core state; it does not open a connection or call a transport. The split allows a native adapter to carry bytes without interpreting authoritative gameplay state.

## Design Reasoning

Native transports are runtime and platform concerns, while message meaning, validation, and synchronization-relevant state are authoritative game concerns. Keeping an opaque byte seam between them prevents Core from acquiring Godot or native APIs and lets deterministic serialization be tested without a socket. A concrete transport can change without moving replication rules out of Core.

## Native Listener Reuse

The pinned GameNetworkingSockets 1.6.0 library defers raw UDP socket destruction to its service thread. `CloseListenSocket` invalidates the listener before that thread necessarily releases the OS port; `RunCallbacks` only dispatches notifications and is not a socket-release barrier. See the pinned [raw socket close implementation](https://github.com/ValveSoftware/GameNetworkingSockets/blob/v1.6.0/src/steamnetworkingsockets/clientlib/steamnetworkingsockets_socketthread.cpp).

For immediate reuse of the same endpoint on the same gateway, `Listen` retries only the native bind diagnostic for Windows `WSAEADDRINUSE` (`0x2740`). Each attempt yields for 1 ms outside native calls so the service thread can finish deferred destruction. The recovery window is capped at 100 ms from the successful listener close, not restarted by subsequent calls. This is a short scheduling allowance, not a guaranteed OS release deadline; exhausted recovery still throws. Initial binds, different endpoints, unknown diagnostics, and other native errors fail without retry. No socket-sharing flags or alternate ports are used. Native creation diagnostics are captured synchronously on the calling thread and included in the exception; background output is not logged. The process owner roots the diagnostic delegate across all gateways and native shutdown.

`RepeatedHostJoinAndStopReleasesListenerAndPeers` retains one port across all twelve cycles. A separate native test holds a real exclusive UDP socket, checks the bind diagnostic and absent listener state, then verifies recovery once that owner closes. The test helper's ephemeral-port reservation has a release-to-bind race with other processes; it is not a reservation that production can rely on, and unrelated occupancy is not treated as our deferred close.

## Architecture and Module Ownership

`Trackstorm.Core` owns:

- Vehicle simulation, logical input, validated settings data, items, spawning and match state. See their [system documents](README.md) for current contracts.
- The transport-facing plain-C# interface and data contracts.
- Game-specific replicated-state contracts, validation, and versioned serialization.

`Trackstorm.Client` or another outer runtime layer owns:

- Godot nodes, scenes, bootstrap, UI, audio, and Dev Mode presentation.
- Native transport objects, sockets, callbacks, buffer adaptation, and gateway implementation.
- Translation between transport-delivered bytes and the appropriate Core replication message.

The dependency direction remains `Client -> Core`. Core cannot reference Client, Godot, GameNetworkingSockets, or another native transport API. Networking transport and game-specific replication are distinct Core namespaces and responsibilities; no Shared layer is used.

## Important Invariants

- Transport payloads are opaque to the transport boundary; game-specific code owns their interpretation.
- A published payload buffer is treated as immutable for the duration of its use.
- Peer identifiers are transport-assigned plain unsigned integers and carry no gameplay authority by themselves.
- Replication message versions and lengths are validated before state is constructed.
- Replicated simulation and input ticks must match.
- Serializable and synchronization-relevant gameplay state remains Core-owned.

## Serialization / Network Assumptions

The current replication envelope is little-endian through its nested `InputFrame` contract and uses explicit version bytes rather than CLR memory layout. Message interpretation and peer identity authentication belong to the game protocol; delivery mode, payload limits and native buffer ownership are enforced by the adapter. The Core simulation independently rejects duplicate, skipped, or out-of-order frame ticks. Transport messages, lifecycle events, configuration and statistics are engine-independent JSON-serializable data. Serializing diagnostics or connection IDs does not restore live sockets or grant gameplay identity.

## Interactions

Client `VehicleNetworkDriver` routes versioned vehicle protocol payloads over `ITransportGateway` at fixed boundaries. Reliable session assignment is separate from unreliable redundant inputs and world snapshots. The legacy `SimulationStateMessage` remains a standalone serialization contract and is not the live vehicle protocol.

## Intentional Limitations / Tradeoffs

The packaged native adapter currently targets Windows x64. [Dependency provenance and licenses](../licenses/transport/README.md) record the pinned binary pairing. Native DLLs and notices copy to build/publish output; Godot uses its application base directory to resolve native dependencies. Other platforms need their corresponding bindings and native binaries. This GNS development adapter supplies neither matchmaking nor authenticated Internet identity or automatic retry. [Session flow](sessions.md) owns its admission. Normal online play uses separate [EOS identity](eos-identity.md), [lobby discovery](eos-lobbies.md) and [P2P](eos-p2p.md) adapters. [Vehicle networking](vehicle-networking.md) supplies prediction/reconciliation, and [match scoring](matches.md) publishes match events above the transport boundary. Full-world rollback remains unsupported.

`check.ps1` verifies Core contracts and transport flag/error conversions in Debug and Release. `check-transport.ps1 -GodotPath <Godot .NET executable>` additionally runs native UDP tests for host plus seven clients, excess admission, bidirectional payloads, reliable ordering during unreliable loss, sampled diagnostics, initial/established timeouts, queue overflow and repeated reconnect/host cycles. It then runs production Godot nodes through three creation/poll/message/removal cycles. `TRACKSTORM_TEST_ADDRESS` can select a local IPv4 LAN interface instead of loopback for native tests. These checks do not establish cross-machine firewall behavior, long-session memory stability, export-template packaging or non-Windows compatibility.

[Feature index](README.md)
