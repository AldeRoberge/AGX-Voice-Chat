using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using AGH.Shared;
using Friflo.Engine.ECS;
using LiteNetLib;
using LiteNetLib.Utils;
using Serilog;
using static AGH_VOice_Chat_Client.LoggingConfig;

namespace AGX_Voice_Chat_Server
{
    public class Server : INetEventListener
    {
        private readonly NetManager _netManager;
        private readonly NetPacketProcessor _packetProcessor;
        private readonly ServerWorld _world;
        private readonly Dictionary<NetPeer, Guid> _peers = new();
        private readonly ServerMetrics _metrics = new();
        private readonly DissonanceVoiceModule _voiceModule;

        private volatile bool _isRunning = true;
        private const double TickWarningThresholdMs = 20.0;

        public int GamePort { get; init; } = 10515;

        public Server()
        {
            _world = new ServerWorld();
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

            // Register networking types
            NetworkRegistration.RegisterTypes(_packetProcessor);

            // Subscribe to packets
            _packetProcessor.SubscribeReusable<JoinRequestPacket, NetPeer>(OnJoinRequest);
            _packetProcessor.SubscribeReusable<InputCommand, NetPeer>(OnInputCommand);
            _packetProcessor.SubscribeReusable<PingPacket, NetPeer>(OnPing);
            _packetProcessor.SubscribeReusable<ChatMessagePacket, NetPeer>(OnTextMessage);

            // Dissonance voice chat relay (all voice logic lives in DissonanceVoiceModule)
            _voiceModule = new DissonanceVoiceModule(_packetProcessor, _peers, _metrics);
            _voiceModule.Register();

        }

        public void Start(int port = 10515, CancellationToken cancellationToken = default)
        {
            if (!_netManager.Start(port))
            {
                Log.Error("Failed to start server on port {Port}", port);
                return;
            }

            Log.Information("Server started on port {Port}", _netManager.LocalPort);
            Log.Information("Tick rate: {TickRate}Hz | Snapshot rate: {SnapshotRate}Hz",
                SimulationConfig.ServerTickRate, SimulationConfig.SnapshotRate);

            var stopwatch = Stopwatch.StartNew();
            var lastTime = stopwatch.Elapsed.TotalSeconds;
            double accumulator = 0;
            const double fixedDelta = SimulationConfig.FixedDeltaTime;

            double tickDurationSum = 0;
            var tickCount = 0;

            while (_isRunning && !cancellationToken.IsCancellationRequested)
            {
                var now = stopwatch.Elapsed.TotalSeconds;
                var frameTime = now - lastTime;
                lastTime = now;
                accumulator += frameTime;

                // Fixed-tick simulation loop
                while (accumulator >= fixedDelta)
                {
                    var tickStart = stopwatch.Elapsed.TotalMilliseconds;

                    // Simulation tick
                    _world.Tick((float)fixedDelta);

                    // Broadcast snapshot at configured rate (30Hz)
                    if (_world.ShouldBroadcastSnapshot())
                    {
                        BroadcastSnapshot();
                        _world.ResetSnapshotCounter();
                    }

                    var tickEnd = stopwatch.Elapsed.TotalMilliseconds;
                    var tickDuration = tickEnd - tickStart;
                    tickDurationSum += tickDuration;
                    tickCount++;

                    // Record tick duration metric
                    _metrics.TickDuration.Record(tickDuration);

                    // Performance monitoring (log every 10 seconds)
                    if (tickCount % (SimulationConfig.ServerTickRate * 10) == 0)
                    {
                        var playerCount = _peers.Count;
                        var avgTickDuration = tickDurationSum / tickCount;
                        var tickBudgetMs = fixedDelta * 1000;
                        var timeRemainingMs = tickBudgetMs - tickDuration;
                        var budgetUsedPercent = (tickDuration / tickBudgetMs) * 100;


                        string budgetMessage;
                        if (timeRemainingMs > 10)
                            budgetMessage = $"Perfect! {timeRemainingMs:F2}ms free";
                        else if (timeRemainingMs > 0)
                            budgetMessage = $"Close! {timeRemainingMs:F2}ms free";
                        else
                            budgetMessage = $"Overbudget by {Math.Abs(timeRemainingMs):F2}ms!";


                        Log.Information("{PlayerCount} Connected Players | Last Tick took {LastTick:F5}ms ({AvgTick:F5}ms average) | {BudgetUsed:F1}% budget used | {BudgetMessage}",
                            playerCount, tickDuration, avgTickDuration, budgetUsedPercent, budgetMessage);

                        // Update tick rate metric
                        _metrics.UpdateTickRate(SimulationConfig.ServerTickRate);

                        // Update GC metrics
                        _metrics.UpdateGcCollections();

                        // Update entity metrics
                        _metrics.UpdateEntitiesActive(_world.GetEntityCount());

                        tickDurationSum = 0;
                        tickCount = 0;
                    }

                    if (tickDuration > TickWarningThresholdMs)
                    {
                        Log.Warning("Tick took {TickDuration:F2}ms out of an expected {FixedDelta:F2}ms! Server might be overloaded.",
                            tickDuration, fixedDelta * 1000);
                        _metrics.TickOverruns.Add(1);
                    }

                    accumulator -= fixedDelta;
                }

                // Network polling
                _netManager.PollEvents();
                Thread.Sleep(1); // Yield to OS
            }

            _netManager.Stop();
            Log.Information("Server stopped.");
        }

        public void Stop()
        {
            _isRunning = false;
        }

        private void BroadcastSnapshot()
        {
            // Handle player respawns
            foreach (var playerId in _world.PlayersToRespawn)
            {
                _world.RespawnPlayer(playerId);
                SendMessage(playerId, "You fell!");
            }

            _world.PlayersToRespawn.Clear();

            var baseSnapshot = _world.GenerateSnapshot();

            foreach (var kvp in _peers)
            {
                var peer = kvp.Key;
                var playerId = kvp.Value;

                // Customize snapshot with player-specific last processed tick
                var snapshot = baseSnapshot;
                snapshot.LastProcessedInputTick = _world.GetLastProcessedInputTick(playerId);

                var writer = new NetDataWriter();
                _packetProcessor.Write(writer, snapshot);

                if (writer.Length > 800)
                    Log.Warning("Large snapshot packet: {Size} bytes (Players: {PlayerCount})", writer.Length, snapshot.Players.Length);

                // Use ReliableSequenced instead of Unreliable to support fragmentation for large packets
                // ReliableSequenced provides fragmentation support while still being relatively fast
                // and ensuring only the latest snapshot matters
                peer.Send(writer, DeliveryMethod.ReliableSequenced);

                // Record network metrics
                _metrics.RecordBytesSent(writer.Length);
                _metrics.RecordPacketSent();
            }
        }

        /// <summary>
        /// Sends a text message to a specific player.
        /// </summary>
        public void SendMessage(Guid playerId, string message, TextMessageType messageType = TextMessageType.System)
        {
            // Find the peer for this player
            var peer = _peers.FirstOrDefault(kvp => kvp.Value == playerId).Key;
            if (peer == null) return;

            var textPacket = new TextPacket
            {
                Message = message,
                MessageType = messageType
            };
            var writer = new NetDataWriter();
            _packetProcessor.Write(writer, textPacket);
            peer.Send(writer, DeliveryMethod.ReliableOrdered);

            // Record network metrics
            _metrics.RecordBytesSent(writer.Length);
            _metrics.RecordPacketSent();
        }

        /// <summary>
        /// Broadcasts a text message to all connected players.
        /// </summary>
        public void BroadcastMessage(string message, TextMessageType messageType = TextMessageType.Chat)
        {
            var textPacket = new TextPacket
            {
                Message = message,
                MessageType = messageType
            };
            var writer = new NetDataWriter();
            _packetProcessor.Write(writer, textPacket);
            foreach (var peer in _peers.Keys)
            {
                peer.Send(writer, DeliveryMethod.ReliableOrdered);

                // Record network metrics
                _metrics.RecordBytesSent(writer.Length);
                _metrics.RecordPacketSent();
            }
        }

        private void OnJoinRequest(JoinRequestPacket packet, NetPeer peer)
        {
            if (_peers.ContainsKey(peer))
            {
                Log.Warning("Player {PlayerName} already joined! Ignoring duplicate join request.", packet.PlayerName);
                return;
            }

            var playerId = Guid.NewGuid();
            Log.Information("Player {PlayerName} joining with ID {PlayerId}", packet.PlayerName, playerId);

            var random = new Random();
            float spawnX = (float)(random.NextDouble() * 200 - 100);
            float spawnY = (float)(random.NextDouble() * 200 - 100);
            float terrainHeight = ServerWorld.GetTerrainHeight(spawnX, spawnY);
            float spawnZ = terrainHeight + 10f;
            var spawnPos = new Vector3(spawnX, spawnY, spawnZ);

            Log.Information("Player {PlayerId} spawning at ({X:F1}, {Y:F1}, {Z:F1}), terrain height: {Height:F1}",
                playerId, spawnX, spawnY, spawnZ, terrainHeight);

            _world.AddPlayer(playerId, spawnPos, packet.PlayerName);
            _peers[peer] = playerId;

            _metrics.RecordPlayerJoin();

            var response = new JoinResponsePacket
            {
                PlayerId = playerId,
                SpawnPosition = spawnPos,
                ServerTick = _world.CurrentTick
            };

            var writer = new NetDataWriter();
            _packetProcessor.Write(writer, response);
            peer.Send(writer, DeliveryMethod.ReliableOrdered);

            BroadcastPlayerInfo(playerId, packet.PlayerName, 100);

            // Send welcome message
            SendMessage(playerId, $"Welcome to the server, {packet.PlayerName}!");
        }

        /// <summary>
        /// Broadcasts static player information to all connected clients.
        /// This is sent separately from snapshots to reduce bandwidth.
        /// </summary>
        private void BroadcastPlayerInfo(Guid playerId, string name, int maxHealth)
        {
            var playerInfo = new PlayerInfoPacket
            {
                PlayerId = playerId,
                Name = name,
                MaxHealth = maxHealth
            };

            var writer = new NetDataWriter();
            _packetProcessor.Write(writer, playerInfo);

            foreach (var peer in _peers.Keys)
            {
                peer.Send(writer, DeliveryMethod.ReliableOrdered);
            }
        }

        private void OnInputCommand(InputCommand packet, NetPeer peer)
        {
            if (_peers.TryGetValue(peer, out var playerId))
            {
                _world.BufferInput(playerId, packet);
            }
            else
            {
                Log.Warning("Received input from unknown peer {PeerId}", peer.Id);
            }
        }

        private void OnPing(PingPacket packet, NetPeer peer)
        {
            var pong = new PongPacket { Timestamp = packet.Timestamp };
            var writer = new NetDataWriter();
            _packetProcessor.Write(writer, pong);
            peer.Send(writer, DeliveryMethod.Unreliable);
        }

        private void OnTextMessage(ChatMessagePacket packet, NetPeer peer)
        {
            if (_peers.TryGetValue(peer, out var playerId))
            {
                var playerName = _world.GetPlayerName(playerId);
                var fullMessage = $"{playerName}: {packet.Message}";
                BroadcastMessage(fullMessage);
            }
            else
            {
                Log.Warning("Received text message from unknown peer {PeerId}", peer.Id);
            }
        }

        // INetEventListener implementation
        public void OnPeerConnected(NetPeer peer)
        {
            Log.Information("Client connected: {Address}", peer.Address);
        }

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            Log.Information("Client disconnected: {Address}, Reason: {Reason}", peer.Address, disconnectInfo.Reason);

            if (_peers.Remove(peer, out var playerId))
            {
                _world.RemovePlayer(playerId);

                // Notify voice module of disconnection
                _voiceModule.OnClientDisconnected(playerId);

                // Record player leave and disconnect metrics
                _metrics.RecordPlayerLeave();
                _metrics.DisconnectsTotal.Add(1, new KeyValuePair<string, object?>("reason", disconnectInfo.Reason.ToString()));
            }
        }

        public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        {
            Log.Error("Network error from {EndPoint}: {Error}", endPoint, socketError);

            // Record error metric
            _metrics.ErrorsTotal.Add(1, new KeyValuePair<string, object?>("type", "network"), new KeyValuePair<string, object?>("subsystem", "network"));
        }

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            // Record network metrics
            _metrics.RecordBytesReceived(reader.AvailableBytes);
            _metrics.RecordPacketReceived();

            try
            {
                while (reader.AvailableBytes > 0)
                    _packetProcessor.ReadPacket(reader, peer);
            }
            catch (ParseException)
            {
                // This exception occurs if we receive a packet that hasn't been registered
                // or if the packet data is malformed.
                // Instead of crashing the server, we'll log a warning and notify the client.
                Log.Error("Received invalid packet from {Peer}. Possible causes : \n" +
                          "Using an outdated version of the game.\n" +
                          "Packet is not registered in NetworkRegistration.RegisterTypes.\n" +
                          "User is trying to cheat.", peer.Address);

                // Record error metric
                _metrics.ErrorsTotal.Add(1, new KeyValuePair<string, object?>("type", "parse"), new KeyValuePair<string, object?>("subsystem", "network"));

                var textPacket = new TextPacket
                {
                    Message = "Invalid packet.",
                    MessageType = TextMessageType.Error
                };
                var writer = new NetDataWriter();
                _packetProcessor.Write(writer, textPacket);
                peer.Send(writer, DeliveryMethod.ReliableOrdered);
            }
        }

        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
        {
        }

        public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
        {
        }

        public void OnConnectionRequest(ConnectionRequest request)
        {
            request.Accept();
        }
    }
}