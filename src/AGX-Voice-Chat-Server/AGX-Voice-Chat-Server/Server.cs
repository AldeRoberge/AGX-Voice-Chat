using System.Net;
using System.Net.Sockets;
using System.Numerics;
using AGH_Voice_Chat_Client.Game;
using AGH_Voice_Chat_Shared;
using AGH_Voice_Chat_Shared.Packets.Join;
using AGH_Voice_Chat_Shared.Packets.Ping;
using LiteNetLib;
using LiteNetLib.Utils;
using Serilog;

namespace AGX_Voice_Chat_Server
{
    /// <summary>
    /// Simple voice + position relay server. No ECS, no snapshots, no tick loop.
    /// Client joins → Welcome. Client sends position → relay to everyone else. Voice → relay.
    /// Scales to 250+ players by design (small packets, no full-state broadcasts).
    /// </summary>
    public class Server : INetEventListener
    {
        private readonly NetManager _netManager;
        private readonly NetPacketProcessor _packetProcessor;
        private readonly Dictionary<NetPeer, PlayerInfo> _players = new();
        private readonly ServerMetrics _metrics = new();
        private readonly DissonanceVoiceModule _voiceModule;
        private readonly ServerPerformanceMonitor _performanceMonitor;

        private volatile bool _isRunning = true;
        private const float GroundLevel = -300f;

        public int VoicePort { get; init; } = 10515;

        private sealed class PlayerInfo
        {
            public Guid Id;
            public string Name = string.Empty;
            public Vector3 Position;
        }

        public Server()
        {
            _packetProcessor = new NetPacketProcessor();
            _netManager = new NetManager(this)
            {
                IPv6Enabled = false,
                PingInterval = 1000,
                SimulateLatency = SimulationConfig.SimulateLatency,
                SimulationMinLatency = SimulationConfig.SimulationMinLatency,
                SimulationMaxLatency = SimulationConfig.SimulationMaxLatency,
                SimulatePacketLoss = SimulationConfig.SimulatePacketLoss,
                SimulationPacketLossChance = SimulationConfig.SimulationPacketLossChance
            };

            NetworkRegistration.RegisterTypes(_packetProcessor);

            _packetProcessor.SubscribeReusable<JoinRequestPacket, NetPeer>(OnJoinRequest);
            _packetProcessor.SubscribeReusable<PlayerPositionPacket, NetPeer>(OnPlayerPosition);
            _packetProcessor.SubscribeReusable<PingPacket, NetPeer>(OnPing);

            // Expose peer -> Guid for voice module
            var peerToPlayerId = new Dictionary<NetPeer, Guid>();
            _voiceModule = new DissonanceVoiceModule(_packetProcessor, peerToPlayerId, _metrics);
            _voiceModule.Register();

            // Keep peerToPlayerId in sync with _players (same keys, value = Id)
            _peerToPlayerId = peerToPlayerId;

            _performanceMonitor = new ServerPerformanceMonitor(_metrics, () => _players.Count);
        }

        private readonly Dictionary<NetPeer, Guid> _peerToPlayerId;

        public void Start(int port = 10515, CancellationToken cancellationToken = default)
        {
            if (!_netManager.Start(port))
            {
                Log.Error("Failed to start server on port {Port}", port);
                return;
            }

            Log.Information("Voice server started on port {Port}.", _netManager.LocalPort);

            while (_isRunning && !cancellationToken.IsCancellationRequested)
            {
                _performanceMonitor.BeginPollCycle();
                _netManager.PollEvents();
                Thread.Sleep(1);
                _performanceMonitor.EndPollCycle();
            }

            _netManager.Stop();
            Log.Information("Server stopped.");
        }

        public void Stop()
        {
            _isRunning = false;
        }

        private IEnumerable<NetPeer> OtherPeers(NetPeer? exclude)
        {
            foreach (var kv in _players)
            {
                if (exclude != null && kv.Key == exclude)
                    continue;
                yield return kv.Key;
            }
        }

        private void OnJoinRequest(JoinRequestPacket packet, NetPeer peer)
        {
            if (_players.ContainsKey(peer))
            {
                Log.Warning("Duplicate join from {Address}, ignoring", peer.Address);
                return;
            }

            var playerId = Guid.NewGuid();
            var r = new Random();
            float x = (float)(r.NextDouble() * 200 - 100);
            float y = (float)(r.NextDouble() * 200 - 100);
            float z = GroundLevel + 10f;
            var spawnPos = new Vector3(x, y, z);

            var info = new PlayerInfo { Id = playerId, Name = packet.PlayerName ?? "Player", Position = spawnPos };
            _players[peer] = info;
            _peerToPlayerId[peer] = playerId;

            _metrics.RecordPlayerJoin();

            Log.Information("Player {Name} joined as {PlayerId}", info.Name, playerId);

            // Welcome the joining client
            var response = new JoinResponsePacket
            {
                PlayerId = playerId,
                SpawnPosition = spawnPos,
                ServerTick = 0
            };
            var w = new NetDataWriter();
            _packetProcessor.WriteNetSerializable(w, ref response);
            peer.Send(w, DeliveryMethod.ReliableOrdered);

            // Tell everyone else "someone joined"
            var playerInfo = new PlayerInfoPacket
            {
                PlayerId = playerId,
                Name = info.Name,
                MaxHealth = 100
            };
            w = new NetDataWriter();
            _packetProcessor.WriteNetSerializable(w, ref playerInfo);
            foreach (var other in OtherPeers(peer))
            {
                other.Send(w, DeliveryMethod.ReliableOrdered);
                _metrics.RecordBytesSent(w.Length);
                _metrics.RecordPacketSent();
            }
        }

        private void OnPlayerPosition(PlayerPositionPacket packet, NetPeer peer)
        {
            if (!_players.TryGetValue(peer, out var info))
                return;

            info.Position = packet.Position;

            // Relay to everyone else (small packet per update)
            var update = new PlayerPositionUpdatePacket { PlayerId = info.Id, Position = packet.Position };
            var w = new NetDataWriter();
            _packetProcessor.WriteNetSerializable(w, ref update);
            foreach (var other in OtherPeers(peer))
            {
                other.Send(w, DeliveryMethod.Unreliable);
                _metrics.RecordBytesSent(w.Length);
                _metrics.RecordPacketSent();
            }
        }

        private void OnPing(PingPacket packet, NetPeer peer)
        {
            var pong = new PongPacket { Timestamp = packet.Timestamp };
            var w = new NetDataWriter();
            _packetProcessor.WriteNetSerializable(w, ref pong);
            peer.Send(w, DeliveryMethod.Unreliable);
        }

        public void OnPeerConnected(NetPeer peer)
        {
            Log.Information("Client connected: {Address}", peer.Address);
        }

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            Log.Information("Client disconnected: {Address}, Reason: {Reason}", peer.Address, disconnectInfo.Reason);

            if (!_players.Remove(peer, out var info))
                return;

            _peerToPlayerId.Remove(peer);
            _voiceModule.OnClientDisconnected(info.Id);
            _metrics.RecordPlayerLeave();
            _metrics.DisconnectsTotal.Add(1, new KeyValuePair<string, object?>("reason", disconnectInfo.Reason.ToString()));

            // Tell everyone "this player left"
            var left = new PlayerLeftPacket { PlayerId = info.Id };
            var w = new NetDataWriter();
            _packetProcessor.WriteNetSerializable(w, ref left);
            foreach (var other in _players.Keys)
            {
                other.Send(w, DeliveryMethod.ReliableOrdered);
                _metrics.RecordBytesSent(w.Length);
                _metrics.RecordPacketSent();
            }
        }

        public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        {
            Log.Error("Network error from {EndPoint}: {Error}", endPoint, socketError);
            _metrics.ErrorsTotal.Add(1, new KeyValuePair<string, object?>("type", "network"), new KeyValuePair<string, object?>("subsystem", "network"));
        }

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            _metrics.RecordBytesReceived(reader.AvailableBytes);
            _metrics.RecordPacketReceived();

            try
            {
                while (reader.AvailableBytes > 0)
                    _packetProcessor.ReadPacket(reader, peer);
            }
            catch (ParseException)
            {
                Log.Error("Invalid packet from {Address}", peer.Address);
                _metrics.ErrorsTotal.Add(1, new KeyValuePair<string, object?>("type", "parse"), new KeyValuePair<string, object?>("subsystem", "network"));
            }
        }

        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }
        public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }

        public void OnConnectionRequest(ConnectionRequest request)
        {
            request.Accept();
        }
    }
}
