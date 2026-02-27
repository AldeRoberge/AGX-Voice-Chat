using System.Numerics;
using AGH_Voice_Chat_Client.Game;
using Friflo.Engine.ECS;
using Serilog;

namespace AGX_Voice_Chat_Server
{
    /// <summary>
    /// Minimal world for voice chat demo: client sends position, server stores and broadcasts.
    /// No reconciliation, interpolation, rotation, health, or input queue.
    /// </summary>
    public class ServerWorld
    {
        private readonly EntityStore _world = new();
        private uint _currentTick;
        private int _ticksSinceSnapshot;

        public uint CurrentTick => _currentTick;

        /// <summary>Flat terrain: constant ground level.</summary>
        public static float GetTerrainHeight(float worldX, float worldY) => SimulationConfig.GroundLevel;

        /// <summary>
        /// Adds a new player at the given spawn position. Entity has Id, Position, Name only.
        /// </summary>
        public void AddPlayer(Guid playerId, Vector3 spawnPos, string playerName = "Unknown")
        {
            var entity = _world.CreateEntity();
            entity.AddComponent(new IdComponent { Value = playerId });
            entity.AddComponent(new PositionComponent { Value = spawnPos });
            entity.AddComponent(new NameComponent { Name = playerName });

            Log.Information("Player {PlayerName} ({PlayerId}) spawned at ({X:F1}, {Y:F1}, {Z:F1})",
                playerName, playerId, spawnPos.X, spawnPos.Y, spawnPos.Z);
        }

        /// <summary>
        /// Removes a player from the world.
        /// </summary>
        public void RemovePlayer(Guid playerId)
        {
            var entity = FindPlayerEntity(playerId);
            if (!entity.IsNull)
            {
                entity.DeleteEntity();
                Log.Information("Player {PlayerId} removed", playerId);
            }
        }

        /// <summary>
        /// Gets a safe spawn position for a player.
        /// </summary>
        public Vector3 GetSpawnPosition()
        {
            var random = new Random();
            float spawnX = (float)(random.NextDouble() * 200 - 100);
            float spawnY = (float)(random.NextDouble() * 200 - 100);
            float terrainHeight = GetTerrainHeight(spawnX, spawnY);
            float spawnZ = terrainHeight + 10f;
            return new Vector3(spawnX, spawnY, spawnZ);
        }

        /// <summary>
        /// Updates a player's position from the client. Server may clamp to world bounds.
        /// </summary>
        public void SetPlayerPosition(Guid playerId, Vector3 position)
        {
            var entity = FindPlayerEntity(playerId);
            if (entity.IsNull)
            {
                Log.Warning("Position update for unknown player {PlayerId}", playerId);
                return;
            }

            // Optional: clamp to world bounds (adjust to your limits)
            float x = Math.Clamp(position.X, -SimulationConfig.HardWorldLimit, SimulationConfig.HardWorldLimit);
            float y = Math.Clamp(position.Y, -SimulationConfig.HardWorldLimit, SimulationConfig.HardWorldLimit);
            float z = Math.Max(position.Z, SimulationConfig.GroundLevel);

            ref var pos = ref entity.GetComponent<PositionComponent>();
            pos.Value = new Vector3(x, y, z);
        }

        /// <summary>
        /// Tick: advance time and snapshot counter. No physics or input processing.
        /// </summary>
        public void Tick(float deltaTime)
        {
            _currentTick++;
            _ticksSinceSnapshot++;

            if (_currentTick % (SimulationConfig.ServerTickRate * 2) == 0)
            {
                var playerCount = _world.Query<IdComponent>().Count;
                Log.Debug("Tick {Tick} | Players: {PlayerCount}", _currentTick, playerCount);
            }
        }

        /// <summary>
        /// Generates a snapshot with players (id, position, name) only.
        /// </summary>
        public WorldSnapshot GenerateSnapshot()
        {
            var players = new List<PlayerState>();
            var playerQuery = _world.Query<IdComponent, PositionComponent, NameComponent>();

            playerQuery.ForEachEntity((ref IdComponent id, ref PositionComponent pos, ref NameComponent name, Entity entity) =>
            {
                players.Add(new PlayerState
                {
                    Id = id.Value,
                    Position = pos.Value,
                    Name = name.Name
                });
            });

            return new WorldSnapshot
            {
                Tick = _currentTick,
                Players = players.ToArray()
            };
        }

        /// <summary>
        /// Gets the name of a player.
        /// </summary>
        public string GetPlayerName(Guid playerId)
        {
            var entity = FindPlayerEntity(playerId);
            if (!entity.IsNull)
                return entity.GetComponent<NameComponent>().Name;
            return "Unknown";
        }

        public bool ShouldBroadcastSnapshot()
        {
            return _ticksSinceSnapshot >= SimulationConfig.TicksPerSnapshot;
        }

        public void ResetSnapshotCounter()
        {
            _ticksSinceSnapshot = 0;
        }

        private Entity FindPlayerEntity(Guid playerId)
        {
            var query = _world.Query<IdComponent>();
            Entity found = default;
            query.ForEachEntity((ref IdComponent id, Entity entity) =>
            {
                if (id.Value == playerId)
                    found = entity;
            });
            return found;
        }

        public ArchetypeQuery<T1> Query<T1>() where T1 : struct, IComponent
        {
            return _world.Query<T1>();
        }

        public ArchetypeQuery<T1, T2> Query<T1, T2>()
            where T1 : struct, IComponent
            where T2 : struct, IComponent
        {
            return _world.Query<T1, T2>();
        }

        public ArchetypeQuery<T1, T2, T3> Query<T1, T2, T3>()
            where T1 : struct, IComponent
            where T2 : struct, IComponent
            where T3 : struct, IComponent
        {
            return _world.Query<T1, T2, T3>();
        }

        public int GetEntityCount()
        {
            return _world.Count;
        }
    }
}
