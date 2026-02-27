using System.Collections.Generic;
using System.Linq;
using System.Text;
using AGH.Shared;
using LiteNetLib;
using LiteNetLib.Utils;
using Serilog;

namespace AGH.Server
{
    /// <summary>
    /// Server-side module that relays Dissonance voice chat packets.
    /// Receives <see cref="VoiceDataPacket"/> from clients and relays to all other connected clients,
    /// preserving reliable vs unreliable delivery so the Dissonance protocol works correctly.
    /// Responds to HandshakeRequest so Dissonance clients reach Connected; does not relay server-only message types.
    /// Owns all voice transport logic (peer lookup, send to peer/target, metrics).
    /// </summary>
    public class DissonanceVoiceModule
    {
        private readonly NetPacketProcessor _packetProcessor;
        private readonly IReadOnlyDictionary<NetPeer, Guid> _peerToPlayerId;
        private readonly ServerMetrics _metrics;

        public DissonanceVoiceModule(NetPacketProcessor packetProcessor, IReadOnlyDictionary<NetPeer, Guid> peerToPlayerId, ServerMetrics metrics)
        {
            _packetProcessor = packetProcessor ?? throw new ArgumentNullException(nameof(packetProcessor));
            _peerToPlayerId = peerToPlayerId ?? throw new ArgumentNullException(nameof(peerToPlayerId));
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        }

        private bool TryGetPlayerId(NetPeer peer, out Guid playerId) =>
            _peerToPlayerId.TryGetValue(peer, out playerId);

        private IEnumerable<NetPeer> GetPeersExcept(NetPeer exclude)
        {
            foreach (var kvp in _peerToPlayerId)
            {
                if (exclude != null && kvp.Key == exclude)
                    continue;
                yield return kvp.Key;
            }
        }

        private void SendVoiceFromTo(NetPeer peer, Guid fromPlayerId, byte[] data, bool reliable)
        {
            var packet = new VoiceDataFromPacket { FromPlayerId = fromPlayerId, Data = data, Reliable = reliable };
            var writer = new NetDataWriter();
            _packetProcessor.Write(writer, packet);
            peer.Send(writer, reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable);
            _metrics.RecordBytesSent(writer.Length);
            _metrics.RecordPacketSent();
        }

        private void SendVoiceFromToTarget(Guid targetPlayerId, Guid fromPlayerId, byte[] data, bool reliable)
        {
            var peer = _peerToPlayerId.FirstOrDefault(kvp => kvp.Value == targetPlayerId).Key;
            if (peer == null)
                return;
            SendVoiceFromTo(peer, fromPlayerId, data, reliable);
        }
        // Dissonance MessageTypes (byte) - MUST match Unity Dissonance.Networking.MessageTypes exactly
        private const byte ClientState = 1;
        private const byte VoiceData = 2;
        private const byte TextData = 3;
        private const byte HandshakeRequest = 4;
        private const byte HandshakeResponse = 5;
        private const byte ErrorWrongSession = 6;
        private const byte ServerRelayReliable = 7;
        private const byte ServerRelayUnreliable = 8;
        private const byte DeltaChannelState = 9;
        private const byte RemoveClient = 10;
        private const byte HandshakeP2P = 11;

        // Dissonance packet magic (big-endian network order)
        private const ushort DissonanceMagic = 0x8bc7;

        // Session management
        private readonly uint _sessionId = GenerateSessionId();
        private ushort _nextClientId = 1;
        
        // Client ID mappings
        private readonly Dictionary<Guid, ushort> _dissonanceClientIds = new();
        private readonly Dictionary<ushort, Guid> _clientIdToPlayerId = new();
        
        // Client metadata
        private readonly Dictionary<ushort, ClientMetadata> _clientMetadata = new();
        
        // Room membership tracking (clientId -> set of room names)
        private readonly Dictionary<ushort, HashSet<string>> _clientRooms = new();
        
        // Room tracking (room name -> set of client IDs listening)
        private readonly Dictionary<string, HashSet<ushort>> _roomListeners = new();
        
        /// <summary>
        /// Client metadata stored during handshake
        /// </summary>
        private class ClientMetadata
        {
            public string Name { get; set; } = string.Empty;
            public byte[] CodecSettings { get; set; } = Array.Empty<byte>();
        }
        
        /// <summary>
        /// Generate a random session ID (per Dissonance spec)
        /// </summary>
        private static uint GenerateSessionId()
        {
            var random = new Random();
            return (uint)random.Next(1, int.MaxValue);
        }
        
        /// <summary>
        /// Compute Dissonance room ID from room name (simple hash to 16-bit)
        /// Dissonance uses ToRoomId method - we implement a compatible hash
        /// </summary>
        private static ushort ToRoomId(string roomName)
        {
            if (string.IsNullOrEmpty(roomName))
                return 0;
            
            // Simple hash matching Dissonance behavior
            uint hash = 0;
            foreach (var c in roomName)
            {
                hash = ((hash << 5) + hash) + c;
            }
            return (ushort)(hash & 0xFFFF);
        }

        /// <summary>
        /// Subscribes to voice packets on the packet processor. Call once during server setup.
        /// </summary>
        public void Register()
        {
            _packetProcessor.SubscribeReusable<VoiceDataPacket, NetPeer>(OnVoiceDataReceived);
            _packetProcessor.SubscribeReusable<VoiceDataToPeerPacket, NetPeer>(OnVoiceDataToPeerReceived);
        }

        /// <summary>
        /// Returns true if this Dissonance payload is server-only (we handle or drop, do not relay).
        /// Payload format: 2 bytes magic, 1 byte message type (Dissonance.Networking.MessageTypes).
        /// </summary>
        private static bool IsServerOnlyDissonanceMessage(byte[] data)
        {
            if (data == null || data.Length < 3)
                return false;
            var type = data[2];
            return type == HandshakeRequest ||
                   type == HandshakeResponse ||
                   type == ServerRelayReliable ||
                   type == ServerRelayUnreliable ||
                   type == ClientState ||
                   type == DeltaChannelState ||
                   type == RemoveClient ||
                   type == TextData ||
                   type == ErrorWrongSession ||
                   type == HandshakeP2P;
        }

        /// <summary>
        /// Parse Dissonance ServerRelay packet (type 7 or 8) and send the inner payload to each destination.
        /// Format after magic+type: session(4), count(1), count×ushort(2 each), length(2), payload(length). All multi-byte values big-endian.
        /// </summary>
        private void ProcessServerRelay(byte[] data, NetPeer fromPeer, bool reliable)
        {
            if (data == null || data.Length < 8)
                return;
            if (!TryGetPlayerId(fromPeer, out var senderId))
                return;

            // Validate session ID
            var sessionId = (uint)((data[3] << 24) | (data[4] << 16) | (data[5] << 8) | data[6]);
            if (sessionId != _sessionId)
            {
                Log.Warning("Received ServerRelay with wrong session ID {ReceivedSession} from peer {Address}, expected {ExpectedSession}",
                    sessionId, fromPeer.Address, _sessionId);
                SendErrorWrongSession(fromPeer);
                return;
            }

            var count = data[7];
            var destStart = 8;
            var destEnd = destStart + count * 2;
            if (data.Length < destEnd + 2)
                return;

            var length = (data[destEnd] << 8) | data[destEnd + 1];
            var payloadStart = destEnd + 2;
            if (data.Length < payloadStart + length)
                return;

            var payload = new byte[length];
            System.Array.Copy(data, payloadStart, payload, 0, length);

            // Block HandshakeP2P packets per Dissonance spec
            if (payload.Length >= 3 && payload[2] == HandshakeP2P)
            {
                Log.Information("Blocking HandshakeP2P relay attempt from peer {Address} (per Dissonance spec)", fromPeer.Address);
                return;
            }

            for (var i = 0; i < count; i++)
            {
                var clientId = (ushort)((data[destStart + i * 2] << 8) | data[destStart + i * 2 + 1]);
                if (clientId == ushort.MaxValue)
                    continue;
                if (!_clientIdToPlayerId.TryGetValue(clientId, out var destPlayerId))
                    continue;
                try
                {
                    SendVoiceFromToTarget(destPlayerId, senderId, payload, reliable);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to relay voice to player {PlayerId}", destPlayerId);
                }
            }
        }

        /// <summary>
        /// Parse HandshakeRequest payload (after magic+type): codecSettings (9 bytes) then name (ushort length + UTF8).
        /// Store in _clientMetadata so we can include this client in HandshakeResponse to others.
        /// </summary>
        private void ParseHandshakeRequest(byte[] data, ushort clientId)
        {
            if (data == null || data.Length < 14)
                return;
            // Payload after magic(2)+type(1): codec(1), frameSize(4), sampleRate(4), nameLength(2) = 11 bytes at offset 3
            var codecSettings = new byte[9];
            for (var i = 0; i < 9; i++)
                codecSettings[i] = data[3 + i];
            var nameLength = (ushort)((data[12] << 8) | data[13]);
            var nameByteCount = nameLength == 0 ? 0 : nameLength - 1; // Dissonance uses length+1 for non-null
            if (14 + nameByteCount > data.Length)
                return;
            var name = nameLength == 0 ? string.Empty : Encoding.UTF8.GetString(data, 14, nameByteCount);
            _clientMetadata[clientId] = new ClientMetadata { Name = name ?? string.Empty, CodecSettings = codecSettings };
        }

        /// <summary>
        /// Build Dissonance HandshakeResponse: Magic, type 5, session, clientId, then client list + rooms so new client knows existing peers.
        /// Dissonance uses network byte order (big-endian) for multi-byte integers.
        /// </summary>
        private byte[] BuildHandshakeResponse(uint session, ushort clientId)
        {
            var list = new List<byte>();
            void WriteUInt16(ushort u)
            {
                list.Add((byte)(u >> 8));
                list.Add((byte)(u & 0xFF));
            }
            void WriteString(string s)
            {
                if (string.IsNullOrEmpty(s))
                {
                    WriteUInt16(0);
                    return;
                }
                var utf8 = Encoding.UTF8.GetBytes(s);
                WriteUInt16((ushort)(utf8.Length + 1));
                list.AddRange(utf8);
            }

            list.Add((byte)(DissonanceMagic >> 8));
            list.Add((byte)(DissonanceMagic & 0xFF));
            list.Add(HandshakeResponse);
            list.Add((byte)((session >> 24) & 0xFF));
            list.Add((byte)((session >> 16) & 0xFF));
            list.Add((byte)((session >> 8) & 0xFF));
            list.Add((byte)(session & 0xFF));
            list.Add((byte)(clientId >> 8));
            list.Add((byte)(clientId & 0xFF));

            // Include all other clients so the new client can route voice (avoids "unknown/disconnected peer")
            var otherClients = new List<(ushort id, string name, byte[] codec)>();
            foreach (var kv in _clientMetadata)
            {
                if (kv.Key == clientId)
                    continue;
                otherClients.Add((kv.Key, kv.Value.Name, kv.Value.CodecSettings));
            }

            WriteUInt16((ushort)otherClients.Count);
            foreach (var (id, name, codec) in otherClients)
            {
                WriteString(name);
                WriteUInt16(id);
                if (codec != null && codec.Length >= 9)
                    list.AddRange(codec);
            }
            WriteUInt16(0); // room name count
            WriteUInt16(0); // channel count
            return list.ToArray();
        }

        /// <summary>
        /// Send ErrorWrongSession packet to client with wrong session ID
        /// </summary>
        private void SendErrorWrongSession(NetPeer peer)
        {
            var buf = new byte[11];
            buf[0] = (byte)(DissonanceMagic >> 8);
            buf[1] = (byte)(DissonanceMagic & 0xFF);
            buf[2] = ErrorWrongSession;
            buf[3] = (byte)((_sessionId >> 24) & 0xFF);
            buf[4] = (byte)((_sessionId >> 16) & 0xFF);
            buf[5] = (byte)((_sessionId >> 8) & 0xFF);
            buf[6] = (byte)(_sessionId & 0xFF);
            // Include expected session ID (4 more bytes)
            buf[7] = (byte)((_sessionId >> 24) & 0xFF);
            buf[8] = (byte)((_sessionId >> 16) & 0xFF);
            buf[9] = (byte)((_sessionId >> 8) & 0xFF);
            buf[10] = (byte)(_sessionId & 0xFF);
            
            SendVoiceFromTo(peer, Guid.Empty, buf, true);
            Log.Warning("Sent ErrorWrongSession to peer {Address}", peer.Address);
        }

        /// <summary>
        /// Process ClientState packet (full room list for a client)
        /// Format: magic(2), type(1), session(4), clientId(2), name(string), codec(bytes), roomCount(1), rooms...
        /// </summary>
        private void ProcessClientState(byte[] data, NetPeer fromPeer)
        {
            if (data == null || data.Length < 10)
                return;

            // Validate session
            var sessionId = (uint)((data[3] << 24) | (data[4] << 16) | (data[5] << 8) | data[6]);
            if (sessionId != _sessionId)
            {
                SendErrorWrongSession(fromPeer);
                return;
            }

            var clientId = (ushort)((data[7] << 8) | data[8]);
            
            // Simple parsing: we don't fully decode name/codec here, just track room memberships
            // The client tells us which rooms they're listening to
            // For now, we'll log receipt and broadcast to other clients
            
            Log.Information("Received ClientState from clientId {ClientId}", clientId);
            
            // Broadcast ClientState to all other clients for state synchronization
            BroadcastToOthers(fromPeer, data, true);
        }

        /// <summary>
        /// Process DeltaClientState packet (single room join/leave)
        /// Format: magic(2), type(1), session(4), flags(1), clientId(2), roomName(string)
        /// </summary>
        private void ProcessDeltaClientState(byte[] data, NetPeer fromPeer)
        {
            if (data == null || data.Length < 12)
                return;

            // Validate session
            var sessionId = (uint)((data[3] << 24) | (data[4] << 16) | (data[5] << 8) | data[6]);
            if (sessionId != _sessionId)
            {
                SendErrorWrongSession(fromPeer);
                return;
            }

            var flags = data[7];
            var joining = (flags & 0x01) != 0;
            var clientId = (ushort)((data[8] << 8) | data[9]);
            
            // Room name is at data[10] onwards (length-prefixed string in Dissonance format)
            // For simplicity, we'll just log and broadcast
            
            Log.Information("Received DeltaClientState from clientId {ClientId}, joining: {Joining}", clientId, joining);
            
            // Broadcast DeltaClientState to all other clients for state synchronization
            BroadcastToOthers(fromPeer, data, true);
        }

        /// <summary>
        /// Process TextData packet (text message through Dissonance)
        /// Format: magic(2), type(1), session(4), recipientType(1), senderId(2), recipientId(2), text(string)
        /// </summary>
        private void ProcessTextData(byte[] data, NetPeer fromPeer)
        {
            if (data == null || data.Length < 13)
                return;

            // Validate session
            var sessionId = (uint)((data[3] << 24) | (data[4] << 16) | (data[5] << 8) | data[6]);
            if (sessionId != _sessionId)
            {
                SendErrorWrongSession(fromPeer);
                return;
            }

            var recipientType = data[7]; // 0 = player, 1 = room
            var senderId = (ushort)((data[8] << 8) | data[9]);
            var recipientId = (ushort)((data[10] << 8) | data[11]);
            
            Log.Information("Received TextData from clientId {SenderId} to recipientId {RecipientId}, type {Type}", 
                senderId, recipientId, recipientType);
            
            if (recipientType == 0)
            {
                // Send to specific player
                if (_clientIdToPlayerId.TryGetValue(recipientId, out var targetPlayerId))
                {
                    SendVoiceFromToTarget(targetPlayerId, Guid.Empty, data, true);
                }
            }
            else
            {
                // Broadcast to room (for now, broadcast to all)
                BroadcastToOthers(fromPeer, data, true);
            }
        }

        /// <summary>
        /// Broadcast data to all clients except the sender
        /// </summary>
        private void BroadcastToOthers(NetPeer sender, byte[] data, bool reliable)
        {
            if (!TryGetPlayerId(sender, out var senderId))
                return;

            foreach (var peer in GetPeersExcept(sender))
            {
                try
                {
                    SendVoiceFromTo(peer, senderId, data, reliable);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to broadcast to peer {Address}", peer.Address);
                }
            }
        }

        /// <summary>
        /// Called when a client disconnects. Broadcasts RemoveClient packet and cleans up state.
        /// </summary>
        public void OnClientDisconnected(Guid playerId)
        {
            if (!_dissonanceClientIds.TryGetValue(playerId, out var clientId))
                return;

            Log.Information("Client {PlayerId} (Dissonance ID {ClientId}) disconnected, broadcasting RemoveClient", 
                playerId, clientId);

            // Build RemoveClient packet
            var buf = new byte[11];
            buf[0] = (byte)(DissonanceMagic >> 8);
            buf[1] = (byte)(DissonanceMagic & 0xFF);
            buf[2] = RemoveClient;
            buf[3] = (byte)((_sessionId >> 24) & 0xFF);
            buf[4] = (byte)((_sessionId >> 16) & 0xFF);
            buf[5] = (byte)((_sessionId >> 8) & 0xFF);
            buf[6] = (byte)(_sessionId & 0xFF);
            buf[7] = (byte)((clientId >> 8) & 0xFF);
            buf[8] = (byte)(clientId & 0xFF);
            // Disconnect reason (ushort) = 0 (unknown)
            buf[9] = 0;
            buf[10] = 0;

            // Broadcast to all remaining clients
            foreach (var peer in GetPeersExcept(null))
            {
                try
                {
                    SendVoiceFromTo(peer, Guid.Empty, buf, true);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to send RemoveClient to peer");
                }
            }

            // Clean up internal state
            _dissonanceClientIds.Remove(playerId);
            _clientIdToPlayerId.Remove(clientId);
            _clientMetadata.Remove(clientId);
            
            if (_clientRooms.TryGetValue(clientId, out var rooms))
            {
                foreach (var room in rooms)
                {
                    if (_roomListeners.TryGetValue(room, out var listeners))
                    {
                        listeners.Remove(clientId);
                        if (listeners.Count == 0)
                            _roomListeners.Remove(room);
                    }
                }
                _clientRooms.Remove(clientId);
            }

            Log.Information("Cleaned up state for disconnected client {ClientId}", clientId);
        }

        private void OnVoiceDataReceived(VoiceDataPacket packet, NetPeer fromPeer)
        {
            Log.Information("Received voice data from peer {Address} with Id {Id}, length {Length}, reliable {Reliable}", fromPeer.Address, fromPeer.Id, packet.Data?.Length ?? 0, packet.Reliable);

            if (packet.Data == null || packet.Data.Length == 0)
            {
                Log.Information("Received empty voice data packet from peer {Address}, ignoring", fromPeer.Address);
                return;
            }

            // Identify packet type
            if (packet.Data.Length < 3)
                return;

            var packetType = packet.Data[2];

            // Handle HandshakeRequest
            if (packetType == HandshakeRequest)
            {
                Log.Information("Received Dissonance HandshakeRequest from peer {Address}", fromPeer.Address);

                if (!TryGetPlayerId(fromPeer, out var fromPlayerId))
                    return;

                if (!_dissonanceClientIds.TryGetValue(fromPlayerId, out var clientId))
                {
                    clientId = _nextClientId++;
                    _dissonanceClientIds[fromPlayerId] = clientId;
                    _clientIdToPlayerId[clientId] = fromPlayerId;
                    Log.Information("Assigned Dissonance clientId {ClientId} to player {PlayerId}", clientId, fromPlayerId);
                }

                // Parse HandshakeRequest body (codecSettings then name) so we can store and include in future handshakes
                ParseHandshakeRequest(packet.Data, clientId);

                var response = BuildHandshakeResponse(_sessionId, clientId);
                SendVoiceFromTo(fromPeer, Guid.Empty, response, true);
                Log.Information("Sent Dissonance HandshakeResponse to peer {PlayerId}, clientId {ClientId}, sessionId {SessionId}", fromPlayerId, clientId, _sessionId);
                return;
            }

            // Handle ServerRelay packets
            if (packetType == ServerRelayReliable || packetType == ServerRelayUnreliable)
            {
                var reliable = packetType == ServerRelayReliable;
                ProcessServerRelay(packet.Data, fromPeer, reliable);
                return;
            }

            // Handle ClientState
            if (packetType == ClientState)
            {
                ProcessClientState(packet.Data, fromPeer);
                return;
            }

            // Handle DeltaChannelState
            if (packetType == DeltaChannelState)
            {
                ProcessDeltaClientState(packet.Data, fromPeer);
                return;
            }

            // Handle TextData
            if (packetType == TextData)
            {
                ProcessTextData(packet.Data, fromPeer);
                return;
            }

            // Drop server-only messages
            if (IsServerOnlyDissonanceMessage(packet.Data))
            {
                Log.Information("Received server-only Dissonance message from peer {Address}, type {Type}, dropping", fromPeer.Address, packetType);
                return;
            }

            // Handle VoiceData and other client-to-client packets (relay to all others)
            if (!TryGetPlayerId(fromPeer, out var senderId))
            {
                Log.Information("Received voice data from unknown peer {Address}, cannot determine sender ID, dropping", fromPeer.Address);
                return;
            }

            var destinations = GetPeersExcept(fromPeer).ToList();

            Log.Information("There are " + destinations.Count + " other peers to relay to");

            foreach (var peer in destinations)
            {
                Log.Information("Relaying voice data from player {SenderId} to peer {Address}, length {Length}, reliable {Reliable}", senderId, peer.Address, packet.Data.Length, packet.Reliable);

                try
                {
                    SendVoiceFromTo(peer, senderId, packet.Data, packet.Reliable);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to relay voice to peer {Address}", peer.Address);
                }
            }
        }

        private void OnVoiceDataToPeerReceived(VoiceDataToPeerPacket packet, NetPeer fromPeer)
        {
            Log.Information("Received voice data to peer {TargetId} from peer {Address}, length {Length}, reliable {Reliable}", packet.TargetPlayerId, fromPeer.Address, packet.Data?.Length ?? 0, packet.Reliable);

            if (packet.Data == null || packet.Data.Length == 0)
                return;

            if (IsServerOnlyDissonanceMessage(packet.Data))
                return;

            if (!TryGetPlayerId(fromPeer, out var fromPlayerId))
                return;

            try
            {
                SendVoiceFromToTarget(packet.TargetPlayerId, fromPlayerId, packet.Data, packet.Reliable);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to relay voice to player {TargetId}", packet.TargetPlayerId);
            }
        }
    }
}