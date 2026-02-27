using System;
using System.Net;
using System.Net.Sockets;
using AGH_Voice_Chat_Client.Game;
using AGH_Voice_Chat_Shared.Packets;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace AGH.Voice.Scripts
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
            // Use SubscribeNetSerializable so deserialization uses our RegisterNestedType (INetSerializable)
            // and never reflects on the type (which would try to register System.Numerics.Vector3 and fail).
            _packetProcessor.SubscribeNetSerializable<JoinResponsePacket>(OnJoinResponse);
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
        /// Register packet types in the SAME ORDER as the server's NetworkRegistration.RegisterTypes.
        /// </summary>
        private void RegisterTypes()
        {
            _packetProcessor.RegisterNestedType(
                (w, v) =>
                {
                    w.Put(v.x);
                    w.Put(v.y);
                },
                r => new Vector2(r.GetFloat(), r.GetFloat()));
            _packetProcessor.RegisterNestedType(
                (w, v) =>
                {
                    w.Put(v.x);
                    w.Put(v.y);
                    w.Put(v.z);
                },
                r => new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat()));

            RegisterNested<JoinRequestPacket>();
            RegisterNested<JoinResponsePacket>();
            RegisterStub<PlayerPositionPacket>();
            RegisterStub<WorldSnapshot>();
            RegisterStub<PlayerState>();
            RegisterStub<PingPacket>();
            RegisterStub<PongPacket>();
            RegisterStub<PlayerInfoPacket>();

            AGHVoiceNetworkRegistration.RegisterVoicePackets(_packetProcessor);
        }

        private void RegisterNested<T>() where T : class, INetSerializable, new()
        {
            _packetProcessor.RegisterNestedType(
                (w, p) => p.Serialize(w),
                r =>
                {
                    var p = new T();
                    p.Deserialize(r);
                    return p;
                });
        }

        private void RegisterStub<T>() where T : class, INetSerializable, new()
        {
            _packetProcessor.RegisterNestedType(
                (w, p) => p.Serialize(w),
                r =>
                {
                    var p = new T();
                    p.Deserialize(r);
                    return p;
                });
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