using System;
using System.Net;
using System.Net.Sockets;
using AGH_Voice_Chat_Shared.Packets;
using AGH.Shared;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace AGH.Voice
{
    /// <summary>
    /// Connects the Unity client to the AGH server and starts Dissonance voice chat once joined.
    /// Add to a GameObject with <see cref="AGHCommsNetwork"/> and <see cref="AGHVoiceTransportAdapter"/>.
    /// Set server address/port and player name, then call <see cref="Connect"/> (e.g. from a UI button).
    /// </summary>
    public class AGHVoiceClient : MonoBehaviour, INetEventListener
    {
        [Header("Connect to (AGH.Server — Unity is client only)")] [SerializeField]
        private string serverAddress = "127.0.0.1";

        [SerializeField] private int serverPort = 10515;
        [SerializeField] private string playerName = "VoicePlayer";

        [Header("References")] [SerializeField]
        private AGHCommsNetwork commsNetwork;

        [SerializeField] private AGHVoiceTransportAdapter transportAdapter;

        private NetManager _netManager;
        private NetPacketProcessor _packetProcessor;
        private NetPeer _serverPeer;
        private string _pendingPlayerName;
        private bool _voiceStarted;

        public bool IsConnected => _serverPeer != null && _serverPeer.ConnectionState == ConnectionState.Connected;

        private void Awake()
        {
            if (commsNetwork == null) commsNetwork = GetComponent<AGHCommsNetwork>();
            if (transportAdapter == null) transportAdapter = GetComponent<AGHVoiceTransportAdapter>();
            if (commsNetwork == null || transportAdapter == null)
            {
                Debug.LogError("AGHVoiceClient: Assign AGHCommsNetwork and AGHVoiceTransportAdapter, or add them to the same GameObject.");
                return;
            }

            _packetProcessor = new NetPacketProcessor();
            RegisterTypes();
            _packetProcessor.SubscribeReusable<JoinResponsePacket>(OnJoinResponse);
            AGHVoiceNetworkRegistration.SubscribeVoiceFrom(_packetProcessor, OnVoiceDataFrom);

            _netManager = new NetManager(this)
            {
                IPv6Enabled = false,
                PingInterval = 1000,
                MaxConnectAttempts = 5,
                ReconnectDelay = 500
            };

            Connect();
        }

        /// <summary>
        /// Register ALL packet types in the EXACT SAME ORDER as the server's NetworkRegistration.RegisterTypes.
        /// This ensures LiteNetLib type hashes match and ReadAllPackets can handle every server packet.
        /// </summary>
        private void RegisterTypes()
        {
            // 1. Vector2 (server uses System.Numerics.Vector2; we use UnityEngine.Vector2)
            _packetProcessor.RegisterNestedType(
                (w, v) => { w.Put(v.x); w.Put(v.y); },
                r => new Vector2(r.GetFloat(), r.GetFloat()));

            // 2. Vector3 (server uses System.Numerics.Vector3; we use UnityEngine.Vector3)
            _packetProcessor.RegisterNestedType(
                (w, v) => { w.Put(v.x); w.Put(v.y); w.Put(v.z); },
                r => new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat()));

            // 3. JoinRequestPacket
            RegisterNested<JoinRequestPacket>();

            // 4. JoinResponsePacket
            RegisterNested<JoinResponsePacket>();

            // 5. PlayerPositionPacket (client sends position; server stores and broadcasts)
            RegisterStub<PlayerPositionPacket>();

            // 6. WorldSnapshot (server sends at 30Hz — most frequent packet)
            RegisterStub<WorldSnapshot>();

            // 7. PlayerState (nested in WorldSnapshot, but also registered as top-level)
            RegisterStub<PlayerState>();

            // 8. ProjectileState
            RegisterStub<ProjectileState>();

            // 9. BoxState
            RegisterStub<BoxState>();

            // 10. PingPacket
            RegisterStub<PingPacket>();

            // 11. PongPacket (server sends in response to ping)
            RegisterStub<PongPacket>();

            // 12. PlayerInfoPacket (server sends on join)
            RegisterStub<PlayerInfoPacket>();

            // 13. TextPacket (server sends messages)
            RegisterStub<TextPacket>();

            // 14. ChatMessagePacket
            RegisterStub<ChatMessagePacket>();

            // 15-17. Voice packets (VoiceDataPacket, VoiceDataFromPacket, VoiceDataToPeerPacket)
            AGHVoiceNetworkRegistration.RegisterVoicePackets(_packetProcessor);

            // 18. BlockUpdate (struct — server registers with 6 fields, no Data)
            _packetProcessor.RegisterNestedType(
                (w, b) =>
                {
                    w.Put(b.LocalX); w.Put(b.LocalY); w.Put(b.LocalZ);
                    w.Put(b.Exists); w.Put(b.BlockType); w.Put(b.Health);
                },
                r => new BlockUpdate
                {
                    LocalX = r.GetByte(), LocalY = r.GetByte(), LocalZ = r.GetByte(),
                    Exists = r.GetByte(), BlockType = r.GetByte(), Health = r.GetByte()
                });

            // 19. ChunkCreatePacket (server sends on join)
            RegisterStub<ChunkCreatePacket>();

            // 20. ChunkUpdatePacket (server sends on block change)
            RegisterStub<ChunkUpdatePacket>();

            // 21. VoxelPaintRequestPacket (client-to-server, but registered for hash matching)
            RegisterStub<VoxelPaintRequestPacket>();

            // 22. StatEffectChanged (server sends status effects)
            RegisterStub<StatEffectChanged>();

            // 23. ItemUseAction (client-to-server, registered for hash matching)
            RegisterStub<ItemUseAction>();

            // 24. ItemUsedEvent (server broadcasts on item use)
            RegisterStub<ItemUsedEvent>();

            // 25. InventorySlotSwitchedAction (client-to-server, registered for hash matching)
            RegisterStub<InventorySlotSwitchedAction>();

            // 26. InventorySlotSwitchedEvent (server broadcasts on slot switch)
            RegisterStub<InventorySlotSwitchedEvent>();

            // 27. InventoryFullSyncPacket (server sends on join)
            RegisterStub<InventoryFullSyncPacket>();
        }

        private void RegisterNested<T>() where T : class, INetSerializable, new()
        {
            _packetProcessor.RegisterNestedType(
                (w, p) => p.Serialize(w),
                r => { var p = new T(); p.Deserialize(r); return p; });
        }

        private void RegisterStub<T>() where T : class, INetSerializable, new()
        {
            _packetProcessor.RegisterNestedType(
                (w, p) => p.Serialize(w),
                r => { var p = new T(); p.Deserialize(r); return p; });
            // Use SubscribeNetSerializable so we deserialize via INetSerializable.Deserialize
            // instead of NetSerializer (which does not support array types like PlayerState[]).
            _packetProcessor.SubscribeNetSerializable<T>(_ => { });
        }

        private void OnJoinResponse(JoinResponsePacket packet)
        {
            if (packet == null)
            {
                Debug.LogWarning("AGHVoiceClient: Received null JoinResponsePacket; ignoring.");
                return;
            }

            if (_voiceStarted)
            {
                Debug.LogWarning("AGHVoiceClient: Received JoinResponse after voice already started; ignoring.");
                return;
            }

            _voiceStarted = true;

            transportAdapter.IsConnected = true;
            transportAdapter.SetLiteNet(_serverPeer, _packetProcessor);

            commsNetwork.RunAsClient(transportAdapter);
            Debug.Log($"AGHVoiceClient: Joined as {packet.PlayerId}, voice started.");
        }

        private void OnVoiceDataFrom(VoiceDataFromPacket packet, NetPeer peer)
        {
            transportAdapter.RaiseVoiceDataFrom(packet.FromPlayerId, packet.Data, packet.Reliable);
        }

        private void Update()
        {
            _netManager?.PollEvents();
        }

        private void OnDestroy()
        {
            Disconnect();
        }

        /// <summary>
        /// Connect to the AGH server and join with the configured (or passed) player name.
        /// </summary>
        [ContextMenu("Connect")]
        public void Connect(string host = null, int? port = null, string name = null)
        {
            Debug.Log("AGHVoiceClient: Connect called.");

            var hostToUse = host ?? serverAddress;
            var portToUse = port ?? serverPort;
            _pendingPlayerName = name ?? playerName;

            if (_netManager == null)
            {
                Debug.LogError("AGHVoiceClient: NetManager not initialized.");
                return;
            }

            if (_netManager.IsRunning)
            {
                Debug.LogWarning("AGHVoiceClient: Already connected or connecting.");
                return;
            }

            _voiceStarted = false;
            if (!_netManager.Start())
            {
                Debug.LogError("AGHVoiceClient: NetManager.Start() failed.");
                return;
            }

            _serverPeer = _netManager.Connect(hostToUse, portToUse, "AGH_GAME");
            if (_serverPeer == null)
                Debug.LogWarning("AGHVoiceClient: Connect returned null (may still be connecting).");
            Debug.Log($"AGHVoiceClient: Connecting to {hostToUse}:{portToUse} as '{_pendingPlayerName}'... (Start AGH.Server first if you see ConnectionFailed)");
        }

        /// <summary>
        /// Disconnect from the server and stop voice.
        /// </summary>
        public void Disconnect()
        {
            Debug.Log("AGHVoiceClient: Disconnect called.");

            if (_netManager != null && _netManager.IsRunning)
            {
                _netManager.Stop();
                _serverPeer = null;
            }

            if (transportAdapter != null)
                transportAdapter.IsConnected = false;
            if (commsNetwork != null)
                commsNetwork.Stop();
            _voiceStarted = false;
            Debug.Log("AGHVoiceClient: Disconnected.");
        }

        void INetEventListener.OnPeerConnected(NetPeer peer)
        {
            Debug.Log($"AGHVoiceClient: Connected to server {peer}");

            _serverPeer = peer;
            var join = new JoinRequestPacket { PlayerName = _pendingPlayerName ?? playerName };
            var writer = new NetDataWriter();
            _packetProcessor.Write(writer, join);
            const byte channel = 0;
            peer.Send(writer, channel, DeliveryMethod.ReliableOrdered);
            Debug.Log("AGHVoiceClient: Connected, sent join request.");
        }

        void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            Debug.LogWarning($"AGHVoiceClient: Disconnected from server {peer}, reason: {disconnectInfo.Reason}, socket error: {disconnectInfo.SocketErrorCode}");

            _serverPeer = null;
            transportAdapter.IsConnected = false;
            commsNetwork.Stop();
            _voiceStarted = false;
            var msg = $"AGHVoiceClient: Disconnected: {disconnectInfo.Reason}";
            if (disconnectInfo.Reason == DisconnectReason.ConnectionFailed)
            {
                msg += ". Ensure AGH.Server is running (default port 10515), serverAddress and serverPort are correct, and no firewall is blocking UDP.";
            }
            else if (disconnectInfo.SocketErrorCode != SocketError.Success)
            {
                msg += $" (SocketError: {disconnectInfo.SocketErrorCode})";
            }

            Debug.LogWarning(msg);
        }

        void INetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        {
            Debug.LogWarning($"AGHVoiceClient: Network error {socketError}");
        }

        void INetEventListener.OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            if (reader.AvailableBytes <= 0) return;
            // NetPacketProcessor reads an 8-byte type hash first
            if (reader.AvailableBytes < 8)
            {
                Debug.LogWarning($"AGHVoiceClient: Ignoring short packet ({reader.AvailableBytes} bytes); need at least 8 for type hash.");
                return;
            }

            try
            {
                _packetProcessor.ReadAllPackets(reader, peer);
            }
            catch (ParseException ex)
            {
                Debug.LogWarning($"AGHVoiceClient: Parse error — unregistered packet type from server. {ex.Message}");
            }
            catch (ArgumentException ex)
            {
                Debug.LogWarning($"AGHVoiceClient: Malformed packet (buffer length/position) — skipping. {ex.Message}");
            }
        }

        void INetEventListener.OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
        {
        }

        void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency)
        {
        }

        void INetEventListener.OnConnectionRequest(ConnectionRequest request)
        {
            request.Reject();
        }
    }
}