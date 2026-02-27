using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using AGH.Shared;
using LiteNetLib;
using LiteNetLib.Utils;
using static AGH_VOice_Chat_Client.LoggingConfig;

namespace AGH_VOice_Chat_Client
{
    public class GameClient : INetEventListener
    {
        private readonly NetManager _netManager;
        private readonly NetPacketProcessor _packetProcessor;
        private NetPeer? _serverPeer;
        private readonly ClientWorld _world;

        private string? _lastHost;
        private int _lastPort;
        private string? _lastPlayerName;
        private double _reconnectTimer;
        private const double ReconnectInterval = 2.0;
        private bool _isReconnecting;

        // Ping measurement
        private long _lastPingSent;
        private DateTime _lastPingSentTime;
        private float _customPingMs;
        private const double _pingInterval = 1.0;
        private double _pingTimer;

        // Timing
        private DateTime _lastUpdateTime = DateTime.UtcNow;

        // Internet simulation
        private bool _internetDisconnected;

        // Properties
        public ClientWorld World => _world;
        public bool IsConnected => _serverPeer?.ConnectionState == ConnectionState.Connected;
        public float? Ping => _serverPeer?.Ping;
        public float CustomPingMs => _customPingMs;
        public bool InternetDisconnected => _internetDisconnected;

        public event Action<Guid>? OnJoined;
        public event Action<TextPacket>? OnTextMessage;
        public event Action? OnConnected;
        public event Action<string>? OnDisconnected;
        public event Action? OnReconnecting;

        public GameClient(ClientWorld? world = null)
        {
            _lastUpdateTime = DateTime.UtcNow;
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
            _packetProcessor = new NetPacketProcessor();
            _world = world ?? new ClientWorld();

            NetworkRegistration.RegisterTypes(_packetProcessor);

            _packetProcessor.SubscribeReusable<JoinResponsePacket>(OnJoinResponse);
            _packetProcessor.SubscribeReusable<WorldSnapshot>(OnWorldSnapshot);
            _packetProcessor.SubscribeReusable<PongPacket>(OnPong);
            _packetProcessor.SubscribeReusable<TextPacket>(OnTextPacket);
            _packetProcessor.SubscribeReusable<PlayerInfoPacket>(OnPlayerInfo);
            _packetProcessor.SubscribeReusable<ChunkCreatePacket>(OnChunkCreate);
            _packetProcessor.SubscribeReusable<ChunkUpdatePacket>(OnChunkUpdate);
            _packetProcessor.SubscribeReusable<InventoryFullSyncPacket>(OnInventoryFullSync);
            _packetProcessor.SubscribeReusable<ItemUsedEvent>(OnItemUsed);
            _packetProcessor.SubscribeReusable<InventorySlotSwitchedEvent>(OnInventorySlotSwitched);
            _packetProcessor.SubscribeReusable<StatEffectChanged>(OnStatEffectChanged);
            
            // Subscribe to world events
            _world.OnItemUseRequested += SendItemUse;
        }

        public void Connect(string host, int port, string playerName)
        {
            _lastHost = host;
            _lastPort = port;
            _lastPlayerName = playerName;
            _netManager.Start();
            _serverPeer = _netManager.Connect(host, port, "AGH_GAME");
        }

        public void Disconnect()
        {
            _netManager.Stop();
        }

        public void ToggleInternet()
        {
            _internetDisconnected = !_internetDisconnected;
        }

        public void Update(float deltaTime = -1f)
        {
            if (deltaTime < 0)
            {
                var now = DateTime.UtcNow;
                deltaTime = (float)(now - _lastUpdateTime).TotalSeconds;
                _lastUpdateTime = now;
            }
            else
            {
                _lastUpdateTime = DateTime.UtcNow; // Keep this in sync even if external time is used
            }

            deltaTime = Math.Clamp(deltaTime, 0f, 0.1f); // Max 100ms

            // Skip network if internet disconnected
            if (!_internetDisconnected)
            {
                _netManager.PollEvents();
            }

            // Update world simulation
            _world.Update(deltaTime);

            // Send pending inputs
            if (IsConnected && _world.LocalPlayerId != Guid.Empty)
            {
                SendPendingInputs();
                _reconnectTimer = 0;
                _isReconnecting = false;
            }
            else
            {
                // Auto-reconnect logic
                _reconnectTimer += deltaTime;
                if (_reconnectTimer >= ReconnectInterval && _lastHost != null && _lastPlayerName != null)
                {
                    _reconnectTimer = 0;
                    if (!_isReconnecting)
                    {
                        _isReconnecting = true;
                        OnReconnecting?.Invoke();
                    }

                    if (!_netManager.IsRunning)
                        _netManager.Start();
                    _serverPeer = _netManager.Connect(_lastHost, _lastPort, "AGH_GAME");
                }
            }

            // Ping measurement
            _pingTimer += deltaTime;
            if (_pingTimer >= _pingInterval && IsConnected)
            {
                _pingTimer = 0;
                _lastPingSent = Stopwatch.GetTimestamp();
                _lastPingSentTime = DateTime.UtcNow;

                var pingPacket = new PingPacket { Timestamp = _lastPingSent };
                var writer = new NetDataWriter();
                _packetProcessor.Write(writer, pingPacket);
                _serverPeer?.Send(writer, DeliveryMethod.Unreliable);
            }
        }

        private void SendPendingInputs()
        {
            while (_world.InputBuffer.TryDequeue(out var input))
            {
                if (input.Fire)
                {
                    InputLog.Debug("Sending FIRE input - Tick: {Tick}, Rotation: {Rotation:F3}", input.Tick, input.Rotation);
                }

                var writer = new NetDataWriter();
                _packetProcessor.Write(writer, input);
                _serverPeer?.Send(writer, DeliveryMethod.ReliableOrdered);
            }
        }

        public void SendChatMessage(string message)
        {
            if (!IsConnected) return;

            var chatPacket = new ChatMessagePacket
            {
                Message = message
            };
            var writer = new NetDataWriter();
            _packetProcessor.Write(writer, chatPacket);
            _serverPeer?.Send(writer, DeliveryMethod.ReliableOrdered);
        }

        public void SendVoxelPaintRequest(VoxelPaintRequestPacket packet)
        {
            if (!IsConnected)
            {
                NetworkLog.Warning("Cannot send voxel paint request - not connected to server");
                return;
            }

            NetworkLog.Information("Sending voxel paint request: Pos=({X:F1},{Y:F1},{Z:F1}), Type={Type}, Removing={Removing}",
                packet.WorldPosition.X, packet.WorldPosition.Y, packet.WorldPosition.Z, packet.VoxelType, packet.IsRemoving);

            var writer = new NetDataWriter();
            _packetProcessor.Write(writer, packet);
            _serverPeer?.Send(writer, DeliveryMethod.ReliableOrdered);
        }

        private void OnJoinResponse(JoinResponsePacket packet)
        {
            // Clone the packet to avoid issues with SubscribeReusable packet reuse
            var response = new JoinResponsePacket
            {
                PlayerId = packet.PlayerId,
                SpawnPosition = packet.SpawnPosition,
                ServerTick = packet.ServerTick
            };


            _world.HandleJoinResponse(response);
            OnJoined?.Invoke(response.PlayerId);
        }

        private void OnWorldSnapshot(WorldSnapshot packet)
        {
            // Clone the snapshot to avoid issues with SubscribeReusable packet reuse
            var snapshot = new WorldSnapshot
            {
                Tick = packet.Tick,
                LastProcessedInputTick = packet.LastProcessedInputTick,
                Players = packet.Players.Select(p => new PlayerState
                {
                    Id = p.Id,
                    Position = p.Position,
                    Velocity = p.Velocity,
                    Rotation = p.Rotation,
                    HealthData = p.HealthData
                    // Name cached separately via PlayerInfoPacket
                }).ToArray(),
                Projectiles = packet.Projectiles.Select(p => new ProjectileState
                {
                    Id = p.Id,
                    Position = p.Position,
                    Velocity = p.Velocity,
                    OwnerId = p.OwnerId
                }).ToArray(),
                Boxes = packet.Boxes.Select(b => new BoxState
                {
                    Id = b.Id,
                    Position = b.Position
                }).ToArray()
            };


            _world.HandleWorldSnapshot(snapshot);
        }

        private void OnPong(PongPacket packet)
        {
            var receiveTime = DateTime.UtcNow;
            var now = Stopwatch.GetTimestamp();
            var sentTimestamp = packet.Timestamp;

            var elapsedTicks = now - sentTimestamp;
            var rttMs = (elapsedTicks * 1000.0) / Stopwatch.Frequency;

            if (rttMs >= 0 && rttMs < 5000)
            {
                _customPingMs = (float)rttMs;
            }
        }

        private void OnTextPacket(TextPacket packet)
        {
            // Clone the packet to avoid issues with SubscribeReusable packet reuse
            var textMessage = new TextPacket
            {
                Message = packet.Message,
                MessageType = packet.MessageType
            };


            OnTextMessage?.Invoke(textMessage);

            // Also log to console

            NetworkLog.Information("[{MessageType}] {Message}", textMessage.MessageType, textMessage.Message);
        }

        private void OnPlayerInfo(PlayerInfoPacket packet)
        {
            // Clone the packet to avoid issues with SubscribeReusable packet reuse
            var playerInfo = new PlayerInfoPacket
            {
                PlayerId = packet.PlayerId,
                Name = packet.Name,
                MaxHealth = packet.MaxHealth
            };


            _world.HandlePlayerInfo(playerInfo);
        }

        private void OnChunkCreate(ChunkCreatePacket packet)
        {
            _world.OnChunkCreate(packet);
        }

        private void OnChunkUpdate(ChunkUpdatePacket packet)
        {
            _world.OnChunkUpdate(packet);
        }

        private void OnInventoryFullSync(InventoryFullSyncPacket packet)
        {
            _world.OnInventoryFullSync(packet);
        }

        private void OnItemUsed(ItemUsedEvent packet)
        {
            _world.OnItemUsed(packet);
        }

        private void OnInventorySlotSwitched(InventorySlotSwitchedEvent packet)
        {
            _world.OnInventorySlotSwitched(packet);
        }

        private void OnStatEffectChanged(StatEffectChanged packet)
        {
            _world.OnStatEffectChanged(packet);
        }

        /// <summary>
        /// Send an inventory slot switch action to the server.
        /// </summary>
        public void SendSlotSwitch(int slotIndex)
        {
            if (_serverPeer == null || _internetDisconnected) return;

            var packet = new InventorySlotSwitchedAction { SlotIndex = slotIndex };
            var writer = new NetDataWriter();
            _packetProcessor.Write(writer, packet);
            _serverPeer.Send(writer, DeliveryMethod.ReliableOrdered);
        }

        /// <summary>
        /// Send an item use action to the server.
        /// </summary>
        public void SendItemUse(int slotIndex)
        {
            if (_serverPeer == null || _internetDisconnected) return;

            var packet = new ItemUseAction 
            { 
                Tick = _world.CurrentTick,
                SlotIndex = slotIndex 
            };
            var writer = new NetDataWriter();
            _packetProcessor.Write(writer, packet);
            _serverPeer.Send(writer, DeliveryMethod.ReliableOrdered);
        }

        // INetEventListener implementation
        public void OnPeerConnected(NetPeer peer)
        {
            _serverPeer = peer;
            _reconnectTimer = 0;
            _isReconnecting = false;
            var join = new JoinRequestPacket { PlayerName = _lastPlayerName ?? "Unknown" };
            var writer = new NetDataWriter();
            _packetProcessor.Write(writer, join);
            peer.Send(writer, DeliveryMethod.ReliableOrdered);
            NetworkLog.Information("Connected to server, sent join request");
            OnConnected?.Invoke();
        }

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            _serverPeer = null;
            _world.Reset(); // Clear all client world state for clean reconnection
            NetworkLog.Information("Disconnected from server: {Reason}", disconnectInfo.Reason);
            OnDisconnected?.Invoke(disconnectInfo.Reason.ToString());
        }

        public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        {
        }

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            _packetProcessor.ReadAllPackets(reader);
        }

        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
        {
        }

        public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
        {
        }

        public void OnConnectionRequest(ConnectionRequest request)
        {
        }
    }
}

