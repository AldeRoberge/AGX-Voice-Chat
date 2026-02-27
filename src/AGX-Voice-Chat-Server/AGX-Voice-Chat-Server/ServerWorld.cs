using System.Numerics;
using AGH.Shared;
using Friflo.Engine.ECS;
using Serilog;
using static AGH_VOice_Chat_Client.LoggingConfig;

namespace AGX_Voice_Chat_Server
{
    /// <summary>
    /// Server-authoritative world simulation for the voice chat demo.
    /// Handles: player connect, movement (walk/jump/dash), and Dissonance room changes (in DissonanceVoiceModule).
    /// </summary>
    public class ServerWorld
    {
        private readonly EntityStore _world = new();
        private uint _currentTick;
        private int _ticksSinceSnapshot;
        public List<Guid> PlayersToRespawn { get; } = new();

        /// <summary>Flat terrain: constant ground level.</summary>
        public static float GetTerrainHeight(float worldX, float worldY) => SimulationConfig.GroundLevel;

        /// <summary>Flat terrain: no solid blocks.</summary>
        public static bool HasSolidBlockAtPosition(float worldX, float worldY, float worldZ = 0) => false;

        public uint CurrentTick => _currentTick;

        /// <summary>
        /// Adds a new player to the world at the specified spawn position.
        /// </summary>
        public void AddPlayer(Guid playerId, Vector3 spawnPos, string playerName = "Unknown")
        {
            var entity = _world.CreateEntity();
            entity.AddComponent(new IdComponent { Value = playerId });
            entity.AddComponent(new PositionComponent { Value = spawnPos });
            entity.AddComponent(new VelocityComponent { Value = Vector3.Zero });
            entity.AddComponent(new RotationComponent { Value = 0f });
            entity.AddComponent(new VisualRotationComponent { Value = MathF.PI / 2 }); // Default facing up (Y+)
            entity.AddComponent(new NameComponent { Name = playerName });
            entity.AddComponent(new HealthComponentWrapper { Health = new HealthComponent(100, 100) });
            entity.AddComponent(new LastProcessedInputComponent { Tick = _currentTick });
            entity.AddComponent(new InputQueueComponent { Queue = [] });

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
        /// Gets a safe spawn position for a player
        /// </summary>
        public Vector3 GetSpawnPosition()
        {
            var random = new Random();
            float spawnX = (float)(random.NextDouble() * 200 - 100);
            float spawnY = (float)(random.NextDouble() * 200 - 100);

            float terrainHeight = GetTerrainHeight(spawnX, spawnY);
            float spawnZ = terrainHeight + 10f; // Spawn 10 units above terrain


            return new Vector3(spawnX, spawnY, spawnZ);
        }

        /// <summary>
        /// Respawns a player at a safe spawn position
        /// </summary>
        public void RespawnPlayer(Guid playerId)
        {
            var entity = FindPlayerEntity(playerId);
            if (entity.IsNull) return;

            var spawnPos = GetSpawnPosition();


            ref var pos = ref entity.GetComponent<PositionComponent>();
            ref var vel = ref entity.GetComponent<VelocityComponent>();


            pos.Value = spawnPos;
            vel.Value = Vector3.Zero; // Reset velocity


            Log.Information("Player {PlayerId} respawned at ({X:F1}, {Y:F1}, {Z:F1})",
                playerId, spawnPos.X, spawnPos.Y, spawnPos.Z);
        }

        /// <summary>
        /// Buffers an input command for processing during the next tick(s).
        /// </summary>
        public void BufferInput(Guid playerId, InputCommand input)
        {
            // Clone the input to avoid issues with SubscribeReusable packet reuse
            var clonedInput = new InputCommand
            {
                Tick = input.Tick,
                MoveDirection = PhysicsSimulation.SanitizeDirection(input.MoveDirection),
                Rotation = input.Rotation,
                Fire = input.Fire,
                Jump = input.Jump, // CRITICAL: Must copy jump input!
                IsDashing = input.IsDashing
            };

            var entity = FindPlayerEntity(playerId);
            if (entity.IsNull)
            {
                Log.Warning("Input received for unknown player {PlayerId}", playerId);
                return;
            }

            ref var queue = ref entity.GetComponent<InputQueueComponent>();

            // Prevent buffer overflow

            if (queue.Queue.Count >= SimulationConfig.MaxInputBufferSize)
            {
                Log.Warning("Input buffer {queueCount}/{maxInput} full for player {PlayerId}, dropping oldest", playerId,
                    queue.Queue.Count, SimulationConfig.MaxInputBufferSize);
                queue.Queue.RemoveAt(0);
            }

            // Add to queue and sort by tick
            queue.Queue.Add(clonedInput);
            queue.Queue.Sort((a, b) => a.Tick.CompareTo(b.Tick));
        }

        /// <summary>
        /// Main simulation tick. Processes player inputs and movement only.
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

            ProcessPlayerInputs(deltaTime);
        }

        /// <summary>
        /// Processes buffered inputs for all players tick-by-tick.
        /// Server ONLY processes inputs that match the current expected tick.
        /// </summary>
        private void ProcessPlayerInputs(float deltaTime)
        {
            var playerQuery = _world.Query<IdComponent, PositionComponent, VelocityComponent,
                RotationComponent, LastProcessedInputComponent>();

            var boxPositions = new List<Vector3>(); // No boxes in demo
            var visualRotationUpdates = new List<(Entity entity, float rotation)>();

            playerQuery.ForEachEntity((ref IdComponent id, ref PositionComponent pos,
                ref VelocityComponent vel, ref RotationComponent rot,
                ref LastProcessedInputComponent lastProcessed, Entity entity) =>
            {
                if (!entity.TryGetComponent<InputQueueComponent>(out var inputQueue)) return;

                var queue = inputQueue.Queue;

                // Flat terrain: no blocks (no climbing)
                bool isClimbing = false;

                // This ensures gravity is deterministic regardless of how many inputs we process
                PhysicsSimulation.ApplyGravity(ref vel.Value, SimulationConfig.FixedDeltaTime, isClimbing);

                // Process inputs sequentially
                var processedCount = 0;
                while (queue.Count > 0 && processedCount < 5) // Max 5 per tick to catch up
                {
                    var nextInput = queue[0];

                    // CRITICAL: Prevent speed hacking (Fast Forward)
                    // Do not process inputs that are in the future relative to server time.
                    // This forces the client to respect the server's simulation speed.

                    if (nextInput.Tick > _currentTick)
                    {
                        NetworkLog.Error($"[SERVER] Detected speed hack attempt from player {id.Value}: input tick {nextInput.Tick} > server tick {_currentTick}. Discarding future inputs.");
                        break;
                    }

                    var expectedTick = lastProcessed.Tick + 1;

                    if (nextInput.Tick < expectedTick)
                    {
                        // Old/duplicate input, discard
                        queue.RemoveAt(0);
                        continue;
                    }

                    if (nextInput.Tick > expectedTick)
                    {
                        // Client is ahead - this is normal due to network/timing differences
                        // Jump forward to process the input
                        lastProcessed.Tick = nextInput.Tick - 1;
                        expectedTick = nextInput.Tick;
                        // Fall through to process the input
                    }

                    if (nextInput.Tick != expectedTick) continue; // Now process the input
                    // Process this input
                    queue.RemoveAt(0);

                    var isGrounded = PhysicsSimulation.IsGrounded(pos.Value.Z, pos.Value, boxPositions, (x, y) => GetTerrainHeight(x, y));
                    if (nextInput.Jump)
                    {
                        Log.Debug("[JUMP DEBUG] Player {Id} jump input at tick {Tick}, grounded={Grounded}, Z={Z:F2}, VelZ before={VelZ:F2}",
                            id.Value, nextInput.Tick, isGrounded, pos.Value.Z, vel.Value.Z);
                    }

                    PhysicsSimulation.ApplyJump(ref vel.Value, isGrounded, nextInput.Jump);
                    if (nextInput.Jump && isGrounded)
                    {
                        Log.Debug("[JUMP DEBUG] After ApplyJump: VelZ={VelZ:F2}", vel.Value.Z);
                    }

                    // Apply horizontal movement (only XY plane)

                    // Handle Dashing (Instant 5 units)
                    if (nextInput.IsDashing)
                    {
                        InputLog.Debug($"[SERVER] Player {id.Value} dashed at tick {nextInput.Tick}");

                        var dashDir = nextInput.MoveDirection;
                        if (dashDir.LengthSquared() < 0.001f)
                        {
                            // No movement input - use visual/facing rotation (last movement direction)
                            var visualRot = entity.GetComponent<VisualRotationComponent>();
                            var forwardX = MathF.Cos(visualRot.Value);
                            var forwardY = MathF.Sin(visualRot.Value);
                            dashDir = new Vector3(forwardX, forwardY, 0f);
                        }
                        else
                        {
                            dashDir = Vector3.Normalize(dashDir);
                        }

                        // Apply Dash Movement
                        var dashVelocity = dashDir * (SimulationConfig.DashDistance / SimulationConfig.FixedDeltaTime);
                        pos.Value = PhysicsSimulation.MoveAndCollide(
                            pos.Value,
                            ref dashVelocity,
                            SimulationConfig.FixedDeltaTime,
                            SimulationConfig.PlayerRadius,
                            boxPositions,
                            (x, y, z) => HasSolidBlockAtPosition(x, y, z));

                        // Log new position
                        InputLog.Debug($"[SERVER] Player {id.Value} dash result pos: {pos.Value}");
                    }

                    // Regular Movement
                    var moveDir = nextInput.MoveDirection;
                    float speed = SimulationConfig.PlayerSpeed; // CONSTANT Speed, no running


                    if (isClimbing)
                    {
                        PhysicsSimulation.ApplyClimbingPhysics(ref vel.Value, moveDir, SimulationConfig.ClimbSpeed, nextInput.Jump);
                        // Also allow horizontal movement on ladder (strafing)
                        var horizontalVel = PhysicsSimulation.CalculateVelocity(moveDir, speed);
                        // ApplyClimbingPhysics handles Z, we merge X/Y
                        vel.Value = new Vector3(horizontalVel.X, horizontalVel.Y, vel.Value.Z);
                    }
                    else
                    {
                        var horizontalVel = PhysicsSimulation.CalculateVelocity(moveDir, speed);
                        vel.Value = new Vector3(horizontalVel.X, horizontalVel.Y, vel.Value.Z); // Preserve Z velocity
                    }

                    rot.Value = nextInput.Rotation;

                    // Update visual rotation from velocity (for movement-based facing direction)
                    if (vel.Value.LengthSquared() > 0.1f)
                    {
                        // Collect update to apply after query loop (avoid structural changes during iteration)
                        visualRotationUpdates.Add((entity, MathF.Atan2(vel.Value.Y, vel.Value.X)));
                    }

                    // Store position before movement to detect underground swimming
                    var positionBeforeMovement = pos.Value;

                    // Apply physics with collision
                    pos.Value = PhysicsSimulation.MoveAndCollide(
                        pos.Value,
                        ref vel.Value,
                        SimulationConfig.FixedDeltaTime,
                        SimulationConfig.PlayerRadius,
                        boxPositions,
                        (x, y, z) => HasSolidBlockAtPosition(x, y, z));

                    var zBeforeClamp = pos.Value.Z;
                    var velZBeforeClamp = vel.Value.Z;
                    PhysicsSimulation.ClampToGround(ref pos.Value, ref vel.Value, boxPositions, (x, y) => GetTerrainHeight(x, y), previousPosition: positionBeforeMovement);

                    // DEBUG: Log if clamping occurred

                    if (zBeforeClamp != pos.Value.Z || velZBeforeClamp != vel.Value.Z)
                    {
                        Log.Debug("[PHYSICS] Tick {Tick} Player {Id}: Clamped Z from {Before:F2} to {After:F2}, VelZ from {VBefore:F2} to {VAfter:F2}",
                            nextInput.Tick, id.Value, zBeforeClamp, pos.Value.Z, velZBeforeClamp, vel.Value.Z);
                    }

                    // Check if player fell off the world or went too far out
                    if (pos.Value.Z < SimulationConfig.DeathLevel ||
                        Math.Abs(pos.Value.X) > SimulationConfig.HardWorldLimit ||
                        Math.Abs(pos.Value.Y) > SimulationConfig.HardWorldLimit)
                    {
                        Log.Information("Player {PlayerId} went out of bounds (Pos: {Pos}) - Respawning", id.Value, pos.Value);
                        PlayersToRespawn.Add(id.Value);
                    }

                    // Update last processed
                    lastProcessed.Tick = expectedTick;
                    processedCount++;
                }
            });

            // Apply visual rotation updates (after query loop to avoid structural changes)
            foreach (var update in visualRotationUpdates)
            {
                if (update.entity.TryGetComponent<VisualRotationComponent>(out var visualRotComp))
                {
                    // Update existing component
                    update.entity.RemoveComponent<VisualRotationComponent>();
                    update.entity.AddComponent(new VisualRotationComponent { Value = update.rotation });
                }
            }
        }

        /// <summary>
        /// Generates a world snapshot for broadcasting to clients (players only; no projectiles/boxes).
        /// Should be called at 30Hz (every 2nd tick).
        /// </summary>
        public WorldSnapshot GenerateSnapshot()
        {
            var players = new List<PlayerState>();
            var playerQuery = _world.Query<IdComponent, PositionComponent, VelocityComponent,
                RotationComponent, NameComponent>();

            playerQuery.ForEachEntity((ref IdComponent id, ref PositionComponent pos,
                ref VelocityComponent vel, ref RotationComponent rot,
                ref NameComponent name, Entity entity) =>
            {
                var healthData = Array.Empty<byte>();
                if (entity.TryGetComponent<HealthComponentWrapper>(out var healthWrapper))
                    healthData = HealthComponent.Serialize(healthWrapper.Health);

                players.Add(new PlayerState
                {
                    Id = id.Value,
                    Position = pos.Value,
                    Velocity = vel.Value,
                    Rotation = rot.Value,
                    Name = name.Name,
                    HealthData = healthData
                });
            });

            return new WorldSnapshot
            {
                Tick = _currentTick,
                LastProcessedInputTick = 0,
                Players = players.ToArray(),
                Projectiles = Array.Empty<ProjectileState>(),
                Boxes = Array.Empty<BoxState>()
            };
        }

        /// <summary>
        /// Gets the last processed input tick for a specific player.
        /// </summary>
        public uint GetLastProcessedInputTick(Guid playerId)
        {
            var entity = FindPlayerEntity(playerId);
            if (entity.IsNull) return 0;

            ref var lastProcessed = ref entity.GetComponent<LastProcessedInputComponent>();
            return lastProcessed.Tick;
        }

        /// <summary>
        /// Gets the name of a player.
        /// </summary>
        public string GetPlayerName(Guid playerId)
        {
            var entity = FindPlayerEntity(playerId);
            if (!entity.IsNull)
            {
                return entity.GetComponent<NameComponent>().Name;
            }

            return "Unknown";
        }

        /// <summary>
        /// Should a snapshot be broadcast this tick?
        /// </summary>
        public bool ShouldBroadcastSnapshot()
        {
            return _ticksSinceSnapshot >= SimulationConfig.TicksPerSnapshot;
        }

        /// <summary>
        /// Resets the snapshot counter (call after broadcasting).
        /// </summary>
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
                {
                    found = entity;
                }
            });
            return found;
        }

        /// <summary>
        /// Exposes ECS query functionality for external systems.
        /// </summary>
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
        
        /// <summary>
        /// Gets the total count of entities in the world.
        /// </summary>
        public int GetEntityCount()
        {
            return _world.Count;
        }
    }
}

