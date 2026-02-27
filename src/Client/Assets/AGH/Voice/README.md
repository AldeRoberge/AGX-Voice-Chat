# AGH Dissonance Voice Integration

This folder contains the **Unity client** integration for Dissonance voice chat over the AGH game connection (LiteNetLib). **Unity is always a client**; the server is the separate **AGH.Server** (Voice module). Unity never acts as the game or voice server.

## How it works

1. **Client → Server**: The Unity client sends voice via `VoiceDataPacket` (opaque payload + reliable flag) over the game connection.
2. **Server relay**: The AGH server relays to all other clients as `VoiceDataFromPacket` (adds `FromPlayerId`).

## Setup in your Unity project

1. **Dissonance** must be installed (Asset Store: Dissonance Voice Chat).
2. **LiteNetLib** must be installed (NuGet for Unity or package). Voice packet types (`VoiceDataPacket`, `VoiceDataFromPacket`, `VoiceDataToPeerPacket`) come from **AGH.Voice.Shared** (DLL in `Assets/Plugins/AGH.Voice.Shared/`); source is in AGH-Website/AGH.Voice.Shared.
3. **Register voice packets** with your `NetPacketProcessor` (same one you use for game packets):
   - Call `AGHVoiceNetworkRegistration.RegisterVoicePackets(yourPacketProcessor)` so the wire format matches the server.
4. **Subscribe to incoming voice**: When you receive packets (e.g. in your `OnNetworkReceive` or poll loop), use `NetPacketProcessor.ReadPacket` (or similar). Subscribe to `VoiceDataFromPacket` and forward to the transport:
   - `AGHVoiceNetworkRegistration.SubscribeVoiceFrom(yourPacketProcessor, (packet, peer) => voiceAdapter.RaiseVoiceDataFrom(packet.FromPlayerId, packet.Data, packet.Reliable));`
5. **Scene**:
   - Add a GameObject with **DissonanceComms** (from Dissonance).
   - Add **AGHCommsNetwork** and **AGHVoiceTransportAdapter** (or your own `IAGHVoiceTransport`).
   - Assign the adapter as the DissonanceComms "Comms Network" (use **AGHCommsNetwork**; the adapter is the transport the network uses).
6. **When the game connects**:
   - Call `voiceAdapter.SetLiteNet(serverPeer, yourPacketProcessor)` (for **AGHVoiceTransportAdapter**).
   - Set `voiceAdapter.SetLocalPlayerId(...)` (e.g. from `JoinResponsePacket.PlayerId`) and `IsConnected = true`.
   - Call `aghCommsNetwork.RunAsClient(voiceAdapter)`.
7. **When the game disconnects**: Call `aghCommsNetwork.Stop()` or disable the component; set `IsConnected = false` on the adapter.

## Quick start: connect to AGH.Server (voice-only)

1. In your scene, add a GameObject with:
   - **DissonanceComms** (Dissonance) – set its "Comms Network" to the **AGHCommsNetwork** on the same object.
   - **AGHCommsNetwork**
   - **AGHVoiceTransportAdapter**
   - **AGHVoiceClient**
2. Set **AGHVoiceClient** fields: `Server Address` (e.g. `127.0.0.1`), `Server Port` (e.g. `10515`), `Player Name`.
3. Start the **AGH.Server** (e.g. from AGH-Website).
4. In play mode, call **AGHVoiceClient.Connect()** (e.g. from a UI button). The client will connect, send a join request, and after receiving **JoinResponsePacket** will start Dissonance voice and relay via the server.
5. Add **VoiceBroadcastTrigger** and **VoiceReceiptTrigger** (Dissonance) so you can talk and hear others in the default room.

## IAGHVoiceTransport

Implement this interface with your game connection:

- **LocalPlayerId**: Your game's player id (Guid). Must match the id the AGH server uses.
- **IsConnected**: True when the game connection is connected.
- **SendToServer(byte[] data, bool reliable)**: Send a `VoiceDataPacket` with `Data = data`, `Reliable = reliable`.
- **VoiceDataFromReceived**: Raise this event when you receive a `VoiceDataFromPacket` (args: `FromPlayerId`, `Data`, `Reliable`).

Use your game's existing player id as the Dissonance peer identity so it matches `FromPlayerId` / `TargetPlayerId` on the server.

## Packet types (shared)

Voice packet types come from **AGH.Voice.Shared** (DLL in `Assets/Plugins/AGH.Voice.Shared/`; source in AGH-Website/AGH.Voice.Shared).
Register with `AGHVoiceNetworkRegistration.RegisterVoicePackets`. To refresh the DLL, see `Assets/Plugins/AGH.Voice.Shared/README.txt`.

