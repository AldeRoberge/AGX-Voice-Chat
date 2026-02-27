using System.Numerics;
using AGH.Shared;
using AGH.Shared.Items;
using Friflo.Engine.ECS;
using Serilog;
using static AGH.Client.Core.LoggingConfig;

namespace AGH.Server
{
    /// <summary>
    /// Server-authoritative world simulation.
    /// Processes inputs strictly per-tick, spawns/simulates projectiles, and handles collisions.
    /// </summary>
    public class ServerWorld
    {
        private readonly EntityStore _world = new();
        private uint _currentTick;
        private uint _nextProjectileId = 1;
        private int _ticksSinceSnapshot;
        public ChunkManager ChunkManager { get; private set; }
        public List<Guid> PlayersToRespawn { get; private set; } = new();

        // Status effect changes that occurred this tick (to be broadcast)

        public List<(Guid playerId, List<StatEffectType> effects)> StatusEffectChanges { get; private set; } = new();

        // Event for block changes (chunkX, chunkY, chunkZ, localX, localY, localZ, exists, health)
        public event Action<int, int, int, int, int, int, bool, byte> OnBlockChanged = delegate { };

        // Track damage to blocks: stored in VoxelChunk.BlockHealth
        // private Dictionary<(int, int, int), int> _blockDamage = new();
        private const int DamagePerHit = 34; // destroy in 3 hits

        public ServerWorld()
        {
            ChunkManager = new ChunkManager();
            SpawnRandomBoxes();
        }

        private void SpawnRandomBoxes()
        {
            var random = new Random();
            uint boxId = 1000;

            // Spawn 5 horizontal lines of 3 cubes each (15 total cubes)

            for (int line = 0; line < 5; line++)
            {
                // Random starting position within world bounds (conservative)
                var startX = (float)(random.NextDouble() * (SimulationConfig.WorldWidth - 300) - (SimulationConfig.WorldWidth / 2 - 150));
                var startY = (float)(random.NextDouble() * (SimulationConfig.WorldHeight - 300) - (SimulationConfig.WorldHeight / 2 - 150));

                // Random direction for the line (0 = horizontal/X-axis, 1 = vertical/Y-axis)

                var direction = random.Next(2);

                // Spawn 3 cubes in a line on the ground

                for (int i = 0; i < 3; i++)
                {
                    // Position cubes in a line with BoxSize spacing
                    float x, y;
                    if (direction == 0)
                    {
                        // Horizontal line along X-axis
                        x = startX + i * SimulationConfig.BoxSize;
                        y = startY;
                    }
                    else
                    {
                        // Horizontal line along Y-axis
                        x = startX;
                        y = startY + i * SimulationConfig.BoxSize;
                    }

                    // All cubes sit on ground level

                    var z = SimulationConfig.BoxSize / 2f;

                    var entity = _world.CreateEntity();
                    entity.AddComponent(new BoxComponent { Id = boxId });
                    entity.AddComponent(new PositionComponent { Value = new Vector3(x, y, z) });

                    Log.Information($"Spawned box {boxId} at {x:F1}, {y:F1}, {z:F1} (line {line}, position {i}, direction {(direction == 0 ? "X" : "Y")})");
                    boxId++;
                }
            }
        }

        // Limit max projectiles to prevent packet size issues
        private const int MaxProjectiles = 100;

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
            entity.AddComponent(new LastFireTickComponent { Tick = 0 });

            // Add inventory with default loadout

            entity.AddComponent(new InventoryComponentWrapper { Inventory = InventoryComponent.CreateDefaultLoadout() });
            entity.AddComponent(new ItemCooldownsComponent { LastUseTicks = new Dictionary<ItemType, uint>() });

            // Add status effect tracking (server is authoritative)

            entity.AddComponent(new StatusEffectComponent { ActiveEffects = new List<StatEffectType>() });

            Log.Information("Player {PlayerName} ({PlayerId}) spawned at ({X:F1}, {Y:F1}, {Z:F1}) with default inventory",
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

            // Get terrain height at this position and spawn above it

            float terrainHeight = ChunkManager.GetTerrainHeight(spawnX, spawnY);
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
        /// Main simulation tick.
        /// Processes inputs, simulates physics, handles projectiles, and detects hits.
        /// </summary>
        public void Tick(float deltaTime)
        {
            _currentTick++;
            _ticksSinceSnapshot++;

            // Clear status effect changes from last tick

            StatusEffectChanges.Clear();

            // Debug output every 2 seconds
            if (_currentTick % (SimulationConfig.ServerTickRate * 2) == 0)
            {
                var playerCount = _world.Query<IdComponent>().Count;
                var projectileCount = _world.Query<ProjectileComponent>().Count;
                Log.Debug("Tick {Tick} | Players: {PlayerCount} | Projectiles: {ProjectileCount}",
                    _currentTick, playerCount, projectileCount);
            }

            // 1. Process player inputs
            ProcessPlayerInputs(deltaTime);

            // 2. Simulate projectiles
            SimulateProjectiles(deltaTime);

            // 3. Detect hits (projectile vs player)
            DetectHits();

            // Debug: Log projectile count after each tick if any exist
            var projCountAfterTick = _world.Query<ProjectileComponent>().Count;
            if (projCountAfterTick > 0)
            {
                ProjectileLog.Debug($"[SERVER] After tick {_currentTick}: {projCountAfterTick} projectiles alive");
            }
        }

        /// <summary>
        /// Processes buffered inputs for all players tick-by-tick.
        /// Server ONLY processes inputs that match the current expected tick.
        /// </summary>
        private void ProcessPlayerInputs(float deltaTime)
        {
            var playerQuery = _world.Query<IdComponent, PositionComponent, VelocityComponent,
                RotationComponent, LastProcessedInputComponent>();

            // Collect box positions for collision
            var boxQuery = _world.Query<BoxComponent, PositionComponent>();
            var boxPositions = new List<Vector3>();
            boxQuery.ForEachEntity((ref BoxComponent box, ref PositionComponent pos, Entity entity) => { boxPositions.Add(pos.Value); });


            // Collect spawn requests and component updates to avoid structural changes during iteration
            var spawnRequests = new List<(Guid owner, Vector3 pos, float rot)>();
            var fireTickUpdates = new List<(Entity entity, LastFireTickComponent component)>();
            var visualRotationUpdates = new List<(Entity entity, float rotation)>();

            playerQuery.ForEachEntity((ref IdComponent id, ref PositionComponent pos,
                ref VelocityComponent vel, ref RotationComponent rot,
                ref LastProcessedInputComponent lastProcessed, Entity entity) =>
            {
                // Get input queue separately to avoid 5-component query limit
                if (!entity.TryGetComponent<InputQueueComponent>(out var inputQueue)) return;

                // Get last fire tick for cooldown enforcement
                if (!entity.TryGetComponent<LastFireTickComponent>(out var lastFireTick))
                {
                    lastFireTick = new LastFireTickComponent { Tick = 0 };
                }

                var queue = inputQueue.Queue;

                // CRITICAL: Apply gravity ONCE per tick, not per input
                // But check for climbing first
                bool isClimbing = false;
                if (ChunkManager.HasBlockAtPosition(pos.Value.X, pos.Value.Y, pos.Value.Z))
                {
                    var block = ChunkManager.GetBlockTypeAtPosition(pos.Value.X, pos.Value.Y, pos.Value.Z);
                    if (block == VoxelType.Ladder) isClimbing = true;
                }

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

                    // Check if grounded and apply jump if requested
                    var isGrounded = PhysicsSimulation.IsGrounded(pos.Value.Z, pos.Value, boxPositions, (x, y) => ChunkManager.GetTerrainHeight(x, y));
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
                            (x, y, z) => ChunkManager.HasSolidBlockAtPosition(x, y, z));

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
                        (x, y, z) => ChunkManager.HasSolidBlockAtPosition(x, y, z));

                    // Clamp to ground if below
                    var zBeforeClamp = pos.Value.Z;
                    var velZBeforeClamp = vel.Value.Z;
                    PhysicsSimulation.ClampToGround(ref pos.Value, ref vel.Value, boxPositions, (x, y) => ChunkManager.GetTerrainHeight(x, y), previousPosition: positionBeforeMovement);

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

                    // Check for status effect changes based on current position
                    var statusEffect = entity.GetComponent<StatusEffectComponent>();
                    var newEffects = new List<StatEffectType>();

                    // Check if player is in a special block (water, ladder, etc.)

                    if (ChunkManager.HasBlockAtPosition(pos.Value.X, pos.Value.Y, pos.Value.Z))
                    {
                        var blockType = ChunkManager.GetBlockTypeAtPosition(pos.Value.X, pos.Value.Y, pos.Value.Z);
                        switch (blockType)
                        {
                            case VoxelType.Water:
                                newEffects.Add(StatEffectType.Swimming);
                                break;
                            case VoxelType.Ladder:
                                newEffects.Add(StatEffectType.Climbing);
                                break;
                        }
                    }

                    // Check if effects changed

                    bool effectsChanged = false;
                    if (statusEffect.ActiveEffects == null || statusEffect.ActiveEffects.Count != newEffects.Count)
                    {
                        effectsChanged = true;
                    }
                    else
                    {
                        // Compare lists
                        for (int i = 0; i < newEffects.Count; i++)
                        {
                            if (!statusEffect.ActiveEffects.Contains(newEffects[i]))
                            {
                                effectsChanged = true;
                                break;
                            }
                        }
                    }

                    if (effectsChanged)
                    {
                        // Directly modify the List (it's a reference type, so changes persist)
                        statusEffect.ActiveEffects.Clear();
                        statusEffect.ActiveEffects.AddRange(newEffects);
                        StatusEffectChanges.Add((id.Value, new List<StatEffectType>(newEffects)));
                    }

                    // Handle fire action with cooldown check
                    if (nextInput.Fire)
                    {
                        InputLog.Debug($"[SERVER] Received FIRE input from player {id.Value} at tick {nextInput.Tick}");

                        // Calculate ticks needed for cooldown

                        var cooldownTicks = (uint)MathF.Ceiling(SimulationConfig.FireCooldown / SimulationConfig.FixedDeltaTime);
                        var ticksSinceLastFire = _currentTick - lastFireTick.Tick;

                        ProjectileLog.Debug($"[SERVER] Cooldown check: ticksSinceLastFire={ticksSinceLastFire}, cooldownTicks={cooldownTicks}");

                        if (ticksSinceLastFire >= cooldownTicks)
                        {
                            ProjectileLog.Debug($"[SERVER] Spawning projectile for player {id.Value}");
                            // Spawn from post-move position to stay aligned with the player
                            spawnRequests.Add((id.Value, pos.Value, rot.Value));
                            lastFireTick.Tick = _currentTick;
                        }
                        else
                        {
                            ProjectileLog.Debug($"[SERVER] Fire blocked by cooldown for player {id.Value}");
                        }
                    }

                    // Update last processed
                    lastProcessed.Tick = expectedTick;
                    processedCount++;
                }
            });

            // Process spawn requests
            foreach (var req in spawnRequests)
            {
                SpawnProjectile(req.owner, req.pos, req.rot);
            }

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
        /// Spawns a projectile from the player's position in the direction they're facing.
        /// Projectiles spawn at fixed Z=0.5 height and travel horizontally.
        /// </summary>
        private void SpawnProjectile(Guid ownerId, Vector3 position, float rotation)
        {
            // Check projectile limit to prevent packet size issues
            var currentProjectileCount = _world.Query<ProjectileComponent>().Count;
            if (currentProjectileCount >= MaxProjectiles)
            {
                Log.Warning("Max projectile limit ({MaxProjectiles}) reached, skipping spawn", MaxProjectiles);
                return;
            }

            // Calculate projectile direction from rotation (horizontal only)
            var direction = new Vector2(MathF.Cos(rotation), MathF.Sin(rotation));
            var velocity = new Vector3(
                direction.X * SimulationConfig.ProjectileSpeed,
                direction.Y * SimulationConfig.ProjectileSpeed,
                0f // Projectiles don't have vertical velocity
            );

            // Spawn exactly at player XY (only Z offset remains)
            var spawnPos = new Vector3(
                position.X,
                position.Y,
                position.Z + SimulationConfig.ProjectileSpawnZOffset // Spawn offset from player Z
            );

            var projectile = _world.CreateEntity();
            var projId = _nextProjectileId++;
            projectile.AddComponent(new ProjectileComponent
            {
                Id = projId
            });
            projectile.AddComponent(new OwnerComponent { Value = ownerId });
            projectile.AddComponent(new LifetimeComponent { Value = 0f });
            projectile.AddComponent(new PositionComponent { Value = spawnPos });
            projectile.AddComponent(new VelocityComponent { Value = velocity });

            // Check if spawning inside a wall
            ProjectileLog.Debug($"[SERVER] Projectile {projId} spawned at ({spawnPos.X:F1}, {spawnPos.Y:F1}, {spawnPos.Z:F1})");
            ProjectileLog.Debug($"[SERVER]   Velocity: ({velocity.X:F1}, {velocity.Y:F1}, {velocity.Z:F1})");

            Log.Debug("Projectile {ProjectileId} spawned by {OwnerId} at ({X:F1}, {Y:F1}, {Z:F1})",
                projId, ownerId, spawnPos.X, spawnPos.Y, spawnPos.Z);
        }

        /// <summary>
        /// Simulates all projectiles: movement, lifetime, and cleanup.
        /// </summary>
        private void SimulateProjectiles(float deltaTime)
        {
            var projectileQuery = _world.Query<ProjectileComponent, PositionComponent, VelocityComponent, LifetimeComponent>();
            var toDestroy = new List<Entity>();

            projectileQuery.ForEachEntity((ref ProjectileComponent proj, ref PositionComponent pos,
                ref VelocityComponent vel, ref LifetimeComponent lifetime, Entity entity) =>
            {
                // Update lifetime
                lifetime.Value += deltaTime;
                if (lifetime.Value >= SimulationConfig.ProjectileMaxLifetime)
                {
                    ProjectileLog.Debug($"[SERVER] Projectile {proj.Id} expired (lifetime={lifetime.Value:F2}s)");
                    toDestroy.Add(entity);
                    return;
                }

                // Calculate new position BEFORE moving
                var oldPos = pos.Value;
                var newPos = pos.Value + vel.Value * deltaTime;

                // Check for voxel collision BEFORE moving
                if (RayCastVoxel(oldPos, newPos, out var hitPos, out var hitNormal))
                {
                    ProjectileLog.Debug($"[SERVER] Projectile {proj.Id} hit voxel at ({hitPos.X:F1}, {hitPos.Y:F1}, {hitPos.Z:F1})");
                    DamageVoxel(hitPos, hitNormal);
                    toDestroy.Add(entity);
                    return; // Hit voxel, stop processing
                }

                // Check if new position would be out of bounds (destroy)

                // Move projectile (only if no collision detected)
                pos.Value = newPos;
            });

            // Destroy expired/out-of-bounds projectiles
            foreach (var entity in toDestroy)
            {
                entity.DeleteEntity();
            }
        }

        /// <summary>
        /// Detects projectile-player hits. Server is authoritative for all hit detection.
        /// </summary>
        private void DetectHits()
        {
            var projectileQuery = _world.Query<ProjectileComponent, PositionComponent, OwnerComponent>();
            var playerQuery = _world.Query<IdComponent, PositionComponent>();
            var toDestroy = new List<Entity>();

            // Collect projectiles
            var projectiles = new List<(Entity entity, uint id, Guid ownerId, Vector3 pos)>();
            projectileQuery.ForEachEntity((ref ProjectileComponent proj, ref PositionComponent projPos, ref OwnerComponent owner, Entity projEntity) => { projectiles.Add((projEntity, proj.Id, owner.Value, projPos.Value)); });

            // Collect players
            var players = new List<(Guid id, Vector3 pos)>();
            playerQuery.ForEachEntity((ref IdComponent id, ref PositionComponent playerPos, Entity playerEntity) => { players.Add((id.Value, playerPos.Value)); });

            // Check collisions
            foreach (var proj in projectiles)
            {
                foreach (var player in players)
                {
                    // Ignore collisions with the owner (no self-damage)
                    if (proj.ownerId == player.id) continue;

                    // Check collision
                    if (!PhysicsSimulation.CheckProjectileHit(
                            proj.pos, SimulationConfig.ProjectileRadius,
                            player.pos, SimulationConfig.PlayerRadius)) continue;

                    Log.Information("Projectile {ProjectileId} hit player {PlayerId}", proj.id, player.id);
                    toDestroy.Add(proj.entity);

                    // Apply damage
                    var playerEntity = FindPlayerEntity(player.id);
                    if (!playerEntity.IsNull && playerEntity.TryGetComponent<HealthComponentWrapper>(out var healthWrapper))
                    {
                        var health = healthWrapper.Health;
                        health.Current -= 10; // Default damage
                        if (health.Current < 0) health.Current = 0;
                        playerEntity.AddComponent(new HealthComponentWrapper { Health = health }); // Update component
                        Log.Information("Player {PlayerId} took damage. Health: {Current}/{Max}",
                            player.id, health.Current, health.Max);
                    }

                    break; // Projectile can only hit one player
                }
            }

            // Destroy hit projectiles
            foreach (var entity in toDestroy)
            {
                entity.DeleteEntity();
            }
        }

        /// <summary>
        /// Generates a world snapshot for broadcasting to clients.
        /// Should be called at 30Hz (every 2nd tick).
        /// </summary>
        public WorldSnapshot GenerateSnapshot()
        {
            var players = new List<PlayerState>();
            var projectiles = new List<ProjectileState>();
            var boxes = new List<BoxState>();

            // Gather player states
            var playerQuery = _world.Query<IdComponent, PositionComponent, VelocityComponent,
                RotationComponent, NameComponent>();


            playerQuery.ForEachEntity((ref IdComponent id, ref PositionComponent pos,
                ref VelocityComponent vel, ref RotationComponent rot,
                ref NameComponent name, Entity entity) =>
            {
                // Serialize health component
                var healthData = Array.Empty<byte>();
                if (entity.TryGetComponent<HealthComponentWrapper>(out var healthWrapper))
                {
                    healthData = HealthComponent.Serialize(healthWrapper.Health);
                }

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

            // Gather projectile states
            var projectileQuery = _world.Query<ProjectileComponent, PositionComponent, VelocityComponent, OwnerComponent>();
            projectileQuery.ForEachEntity((ref ProjectileComponent proj, ref PositionComponent pos,
                ref VelocityComponent vel, ref OwnerComponent owner, Entity entity) =>
            {
                projectiles.Add(new ProjectileState
                {
                    Id = proj.Id,
                    Position = pos.Value,
                    Velocity = vel.Value,
                    OwnerId = owner.Value
                });
            });

            // Gather box states
            var boxQuery = _world.Query<BoxComponent, PositionComponent>();
            boxQuery.ForEachEntity((ref BoxComponent box, ref PositionComponent pos, Entity entity) =>
            {
                boxes.Add(new BoxState
                {
                    Id = box.Id,
                    Position = pos.Value
                });
            });

            if (projectiles.Count > 0)
            {
                NetworkLog.Debug($"[SERVER] Generating snapshot with {projectiles.Count} projectiles");
                foreach (var proj in projectiles)
                {
                    NetworkLog.Debug($"[SERVER]   Projectile {proj.Id}: Pos=({proj.Position.X:F1}, {proj.Position.Y:F1}, {proj.Position.Z:F1}) Owner={proj.OwnerId}");
                }
            }

            return new WorldSnapshot
            {
                Tick = _currentTick,
                LastProcessedInputTick = 0, // Will be set per-client in Server.cs
                Players = players.ToArray(),
                Projectiles = projectiles.ToArray(),
                Boxes = boxes.ToArray()
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
        /// Raycasts against voxels to find the first hit.
        /// Returns true if a voxel was hit.
        /// </summary>
        private bool RayCastVoxel(Vector3 start, Vector3 end, out Vector3 hitPos, out Vector3 hitNormal)
        {
            hitPos = Vector3.Zero;
            hitNormal = Vector3.Zero;

            Vector3 direction = end - start;
            float maxDistance = direction.Length();
            direction /= maxDistance;

            // Simple step-based raycast (DDA or similar would be better but this is sufficient for now)
            float stepSize = SimulationConfig.BlockSize / 4f;

            float distance = 0f;

            while (distance <= maxDistance)
            {
                Vector3 currentPos = start + direction * distance;


                if (ChunkManager.HasBlockAtPosition(currentPos.X, currentPos.Y, currentPos.Z))
                {
                    hitPos = currentPos;
                    hitNormal = -direction; // Rough approximation
                    return true;
                }

                distance += stepSize;
            }

            return false;
        }

        private void DamageVoxel(Vector3 hitPos, Vector3 hitNormal)
        {
            // Identify block coordinates
            int blockX = (int)Math.Floor((hitPos.X - SimulationConfig.WorldMinX) / SimulationConfig.BlockSize);
            int blockY = (int)Math.Floor((hitPos.Y - SimulationConfig.WorldMinY) / SimulationConfig.BlockSize);
            int blockZ = (int)Math.Floor(hitPos.Z / SimulationConfig.BlockSize);

            int chunkX = blockX / SimulationConfig.ChunkSize;
            int chunkY = blockY / SimulationConfig.ChunkSize;
            int chunkZ = blockZ / SimulationConfig.ChunkHeight;
            int localX = blockX % SimulationConfig.ChunkSize;
            int localY = blockY % SimulationConfig.ChunkSize;
            int localZ = blockZ % SimulationConfig.ChunkHeight;

            var chunk = ChunkManager.GetChunk(chunkX, chunkY, chunkZ);
            if (chunk != null)
            {
                byte currentHealth = chunk.GetBlockHealth(localX, localY, localZ);
                if (currentHealth == 0) return; // Already destroyed

                int newHealth = Math.Max(0, currentHealth - DamagePerHit);
                chunk.SetBlockHealth(localX, localY, localZ, (byte)newHealth);

                if (newHealth <= 0)
                {
                    // Destroy block
                    chunk.SetBlock(localX, localY, localZ, false);
                    OnBlockChanged?.Invoke(chunkX, chunkY, chunkZ, localX, localY, localZ, false, 0);
                    Log.Information($"Block destroyed at ({blockX}, {blockY}, {blockZ})");
                }
                else
                {
                    // Broadcast health update (block still exists but damaged)
                    OnBlockChanged?.Invoke(chunkX, chunkY, chunkZ, localX, localY, localZ, true, (byte)newHealth);
                    Log.Information($"Block damaged at ({blockX}, {blockY}, {blockZ}), health: {newHealth}/100");
                }
            }
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

