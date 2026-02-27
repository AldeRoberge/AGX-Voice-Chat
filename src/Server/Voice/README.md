# Dissonance Voice Module

This folder contains the server-side **Dissonance voice chat** integration for AGH. The module relays opaque Dissonance protocol packets between clients over the existing game connection (LiteNetLib).

## How It Works

1. **Client → Server**: Clients send `VoiceDataPacket` (opaque payload + reliable flag) to the server.
2. **Server relay**: The server relays to all other connected clients as `VoiceDataFromPacket` (adds `FromPlayerId` so Dissonance can route by peer).
3. **Host → One client**: When the Dissonance “server” (host peer) sends to a specific client, the client sends `VoiceDataToPeerPacket` (target player id + payload). The server relays only to that peer as `VoiceDataFromPacket`.

## Packets (AGH.Shared)

- **VoiceDataPacket** – Client → server: `Data`, `Reliable`.
- **VoiceDataFromPacket** – Server → client: `FromPlayerId`, `Data`, `Reliable` (used for all relayed voice).
- **VoiceDataToPeerPacket** – Client (host) → server: `TargetPlayerId`, `Data`, `Reliable` (targeted relay).

## Unity Client Integration

To use Dissonance in a Unity client that connects to this server:

1. Install [Dissonance](https://assetstore.unity.com/packages/tools/audio/dissonance-voice-chat-70078) in your Unity project.
2. Implement a **custom Dissonance network** that sends/receives via your AGH game connection:
   - **BaseCommsNetwork** subclass that creates your **BaseServer** / **BaseClient** subclasses.
   - **BaseClient**: in `SendReliable` / `SendUnreliable`, send a `VoiceDataPacket` over the game connection; in `ReadMessages`, take received `VoiceDataFromPacket` (and, if host, `VoiceDataToPeerPacket`) and call `NetworkPacketReceived` with the payload and peer (use `FromPlayerId` as your peer identity).
   - **BaseServer**: in `ReadMessages`, handle relayed packets and call `NetworkPacketReceived(peer, data)`; in `SendReliable(peer, …)` / `SendUnreliable(peer, …)`, send a `VoiceDataToPeerPacket` with `TargetPlayerId = peer` over the game connection.
3. Use your game’s existing **player id** (e.g. `Guid`) as the Dissonance peer identity so it matches `FromPlayerId` / `TargetPlayerId` on the server.
4. Start Dissonance when the game connection is established and stop it on disconnect; drive `RunAsHost` / `RunAsClient` from your game’s host/client state.

See Dissonance docs: **Writing A Custom Network Integration** for the required base class methods (`Connect`, `Disconnect`, `ReadMessages`, `SendReliable`, `SendUnreliable`, `ClientDisconnected`, loopback, and optional P2P).

## Disabling the Module

The module is registered in `Server`’s constructor. To disable voice relay, remove the `_voiceModule` construction and `Register` call, and do not subscribe to `VoiceDataPacket` / `VoiceDataToPeerPacket` on the server.
