using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AGH.Shared;
using AGH.Shared.Items;
using static AGH_VOice_Chat_Client.LoggingConfig;

namespace AGH_VOice_Chat_Client
{
    /// <summary>
    /// Client-side world simulation with client-side prediction and reconciliation.
    /// </summary>
    public class ClientWorld
    {
        // Entity management
        public Dictionary<Guid, ClientEntity> Players { get; } = new();
        public Dictionary<uint, ClientEntity> Projectiles { get; } = new();
        public Dictionary<uint, ClientEntity> Boxes { get; } = new();
        public List<ClientEntity> PredictedProjectiles { get; } = [];

        // Chunk management
        public ClientChunkManager ChunkManager { get; private set; } = new();

        // Static player info cache (name, maxhealth)
        private readonly Dictionary<Guid, PlayerInfoPacket> _playerInfoCache = new();

        // Inventory management
        public InventoryComponent? LocalPlayerInventory { get; private set; }
        private readonly Dictionary<ItemType, uint> _lastItemUseTick = new();

        // Input management

        public ConcurrentQueue<InputCommand> InputBuffer { get; } = new();
        private readonly List<InputCommand> _inputHistory = [];

        // Simulation state

        public uint CurrentTick { get; private set; }
        public uint LastServerTick { get; private set; }
        public Guid LocalPlayerId { get; private set; }

        // Interpolation for remote entities

        private readonly InterpolationBuffer _interpolationBuffer = new();
        public InterpolationBuffer InterpolationBuffer => _interpolationBuffer;

        // Timing

        private float _accumulator;
        private uint _lastReconciledTick;
        private uint _lastFireTick; // Track last tick when fire input was sent

        // Local player smoothing (for rendering only)
        private Vector3 _localPlayerPrevPosition;
        private Vector3 _localPlayerCurrentPosition;

        // Events

        public event Action<float>? OnLargeReconciliation;
        public event Action<int>? OnItemUseRequested;

        // Input callbacks (overridden by MonoGameWorld)

        protected virtual Vector3 GetInputDirection() => Vector3.Zero;
        protected virtual float GetRotation() => 0f;
        protected virtual bool GetFireInput() => false;
        protected virtual bool GetJumpInput() => false;
        protected virtual bool GetDashInput() => false;
        protected virtual bool GetCrouchInput() => false;
        protected virtual float GetVisualRotation() => 0f; // Last movement/facing direction

        /// <summary>
        /// Resets all client world state. Called when disconnecting from server.
        /// This ensures clean state for reconnection scenarios.
        /// </summary>
        public void Reset()
        {
            LocalPlayerId = Guid.Empty;
            CurrentTick = 0;
            LastServerTick = 0;
            _lastReconciledTick = 0;
            _lastFireTick = 0;
            _accumulator = 0;
            _localPlayerPrevPosition = Vector3.Zero;
            _localPlayerCurrentPosition = Vector3.Zero;

            Players.Clear();
            Projectiles.Clear();
            Boxes.Clear();
            PredictedProjectiles.Clear();
            _playerInfoCache.Clear();
            _inputHistory.Clear();


            LocalPlayerInventory = null;
            _lastItemUseTick.Clear();

            while (InputBuffer.TryDequeue(out _))
            {
            }

            _interpolationBuffer.Clear();
        }

        /// <summary>
        /// Main update loop. Runs fixed-timestep simulation.
        /// </summary>
        public void Update(float deltaTime)
        {
            _accumulator += deltaTime;


            while (_accumulator >= SimulationConfig.FixedDeltaTime)
            {
                Tick();
                _accumulator -= SimulationConfig.FixedDeltaTime;
            }
        }

        /// <summary>
        /// Update interpolation for rendering (call every frame).
        /// </summary>
        public void UpdateInterpolation(float deltaTime)
        {
            _interpolationBuffer.Update(deltaTime);

            // Apply interpolation to remote players

            foreach (var player in Players.Values)
            {
                if (player.IsLocalPlayer) continue; // Local player uses prediction


                player.Position = _interpolationBuffer.GetPlayerPosition(player.Id);
                player.Rotation = _interpolationBuffer.GetPlayerRotation(player.Id);
            }

            // Apply interpolation to projectiles

            foreach (var projectile in Projectiles.Values)
            {
                projectile.Position = _interpolationBuffer.GetProjectilePosition(projectile.ProjectileId);
            }
        }

        /// <summary>
        /// Get extrapolated position for a predicted projectile (for rendering only).
        /// This extrapolates projectile movement between ticks for smooth rendering.
        /// </summary>
        public Vector3 GetPredictedProjectileRenderPosition(ClientEntity projectile)
        {
            // Projectiles move linearly at constant velocity, so we can simply extrapolate
            // from their last simulated position using the accumulator time
            return projectile.Position + projectile.Velocity * _accumulator;
        }

        /// <summary>
        /// Get extrapolated position for the local player (for rendering only).
        /// This predicts movement from the last simulated tick into the current frame.
        /// </summary>
        public Vector3 GetLocalPlayerRenderPosition()
        {
            if (LocalPlayerId == Guid.Empty || !Players.TryGetValue(LocalPlayerId, out var localPlayer))
            {
                return Vector3.Zero;
            }

            // Extrapolate from the last confirmed/predicted position using CURRENT input
            // This provides immediate response to input and smooth movement between ticks

            // 1. Get current input (what the player is pressing NOW)

            var moveDir = GetInputDirection();


            // 2. Calculate horizontal velocity from that input
            var isDashing = GetDashInput();
            float speed = SimulationConfig.PlayerSpeed;

            var horizontalVel = PhysicsSimulation.CalculateVelocity(moveDir, speed);

            // 2b. Check if dashing (this is for rendering extrapolation, might just ignore dash since it's instant)
            // If we are dashing in the CURRENT frame (rendering frame), we might want to show it.
            // But GetLocalPlayerRenderPosition is called every frame, while IsDashing is a tick event.
            // Since we can't easily knowing if we JUST pressed dash in this fractional frame without complex state,
            // we will just use normal speed for extrapolation. The Dash will happen on the Tick.

            // 3. Use current vertical velocity (includes gravity/jump state)

            var velocity = new Vector3(horizontalVel.X, horizontalVel.Y, localPlayer.Velocity.Z);

            // 4. Predict where we would be at (LastTickTime + accumulator)
            // We start from _localPlayerCurrentPosition which is the position at the end of the last Tick()

            var extrapolatedPos = PhysicsSimulation.MoveAndCollide(
                _localPlayerCurrentPosition,
                ref velocity,
                _accumulator, // Predict forward by the time accumulated since last tick
                SimulationConfig.PlayerRadius,
                Boxes.Values.Select(b => b.Position),
                (x, y, z) => ChunkManager.HasSolidBlockAtPosition(x, y, z)
            );

            return extrapolatedPos;
        }

        /// <summary>
        /// Single simulation tick - runs client-side prediction.
        /// </summary>
        private void Tick()
        {
            if (LocalPlayerId == Guid.Empty) return;

            CurrentTick++;

            // Gather input

            var moveDir = GetInputDirection();
            var rotation = GetRotation();
            var fireInput = GetFireInput();
            var jumpInput = GetJumpInput();
            var dashInput = GetDashInput();

            // Handle item usage (replaces old fire cooldown system)
            bool canFire = false;
            if (fireInput && LocalPlayerInventory != null)
            {
                // Try to use the active item
                if (UseActiveItem())
                {
                    canFire = true;
                    // Trigger event so GameClient can send item use action
                    OnItemUseRequested?.Invoke(LocalPlayerInventory.ActiveSlotIndex);
                }
            }
            else if (fireInput)
            {
                // Fallback to old cooldown system if inventory not initialized
                var cooldownTicks = (uint)MathF.Ceiling(SimulationConfig.FireCooldown / SimulationConfig.FixedDeltaTime);
                var ticksSinceLastFire = CurrentTick - _lastFireTick;
                canFire = ticksSinceLastFire >= cooldownTicks;
                if (canFire)
                {
                    _lastFireTick = CurrentTick;
                }
            }

            // Create input command (only send fire=true if cooldown allows)
            var input = new InputCommand
            {
                Tick = CurrentTick,
                MoveDirection = moveDir,
                Rotation = rotation,
                Fire = canFire,
                Jump = jumpInput,
                IsDashing = dashInput,
                IsCrouching = GetCrouchInput()
            };

            // Update last fire tick if we're firing
            if (canFire)
            {
                _lastFireTick = CurrentTick;
            }

            // Store in history for reconciliation

            _inputHistory.Add(input);
            if (_inputHistory.Count > SimulationConfig.MaxInputHistory)
            {
                _inputHistory.RemoveAt(0);
            }

            // Send to server
            InputBuffer.Enqueue(input);

            // Client-side prediction: simulate local player immediately

            if (Players.TryGetValue(LocalPlayerId, out var localPlayer))
            {
                // Store previous position for smooth interpolation
                _localPlayerPrevPosition = localPlayer.Position;

                PredictLocalPlayer(localPlayer, input);

                // Store current position after prediction
                _localPlayerCurrentPosition = localPlayer.Position;

                if (input.Fire)
                {
                    // Spawn predicted projectile from current player position (only Z offset)
                    var direction = new Vector2(MathF.Cos(localPlayer.Rotation), MathF.Sin(localPlayer.Rotation));
                    var velocity = new Vector3(
                        direction.X * SimulationConfig.ProjectileSpeed,
                        direction.Y * SimulationConfig.ProjectileSpeed,
                        0f // Projectiles don't have vertical velocity
                    );
                    var spawnPos = new Vector3(
                        localPlayer.Position.X,
                        localPlayer.Position.Y,
                        localPlayer.Position.Z + SimulationConfig.ProjectileSpawnZOffset // Spawn offset from player Z
                    );

                    PredictedProjectiles.Add(new ClientEntity
                    {
                        Id = Guid.NewGuid(),
                        OwnerId = LocalPlayerId,
                        Position = spawnPos,
                        Velocity = velocity,
                        Type = EntityType.Projectile,
                        Lifetime = 0f
                    });
                }
            }

            // Simulate predicted projectiles
            for (int i = PredictedProjectiles.Count - 1; i >= 0; i--)
            {
                var proj = PredictedProjectiles[i];
                proj.Lifetime += SimulationConfig.FixedDeltaTime;

                if (proj.Lifetime >= SimulationConfig.ProjectileMaxLifetime)
                {
                    PredictedProjectiles.RemoveAt(i);
                    continue;
                }

                // Calculate new position BEFORE moving (same as server)
                var oldPos = proj.Position;
                var newPos = proj.Position + proj.Velocity * SimulationConfig.FixedDeltaTime;

                // Check for voxel collision BEFORE moving
                if (RayCastVoxel(oldPos, newPos, out var hitPos))
                {
                    PredictedProjectiles.RemoveAt(i);
                    continue;
                }


                // Move projectile only if no collision detected
                proj.Position = newPos;
            }
        }

        /// <summary>
        /// Predicts local player movement using same physics as server.
        /// CRITICAL: Must match server logic exactly for reconciliation to work.
        /// </summary>
        private void PredictLocalPlayer(ClientEntity player, InputCommand input)
        {
            // Update visual rotation first so it's available for dash calculations
            player.VisualRotation = GetVisualRotation();

            // Use local variables for physics methods that require ref parameters
            var velocity = player.Velocity;
            var position = player.Position;

            // Check for climbing
            bool isClimbing = false;
            if (ChunkManager.HasBlockAtPosition(position.X, position.Y, position.Z))
            {
                var block = ChunkManager.GetBlockTypeAtPosition(position.X, position.Y, position.Z);
                if (block == VoxelType.Ladder) isClimbing = true;
            }

            // Apply gravity (always affects vertical velocity)
            PhysicsSimulation.ApplyGravity(ref velocity, SimulationConfig.FixedDeltaTime, isClimbing);

            // Check if grounded and apply jump if requested
            var isGrounded = PhysicsSimulation.IsGrounded(position.Z, position, Boxes.Values.Select(b => b.Position), (x, y) => ChunkManager.GetTerrainHeight(x, y));
            PhysicsSimulation.ApplyJump(ref velocity, isGrounded, input.Jump);

            // Apply horizontal movement (only XY plane)
            // If dashing, move by DashDistance instantly. otherwise use normal speed/velocity.


            float speed = SimulationConfig.PlayerSpeed;
            Vector3 horizontalVel;


            if (isClimbing)
            {
                PhysicsSimulation.ApplyClimbingPhysics(ref velocity, input.MoveDirection, SimulationConfig.ClimbSpeed, input.Jump);
                horizontalVel = PhysicsSimulation.CalculateVelocity(input.MoveDirection, speed);
            }
            else
            {
                horizontalVel = PhysicsSimulation.CalculateVelocity(input.MoveDirection, speed);
            }

            // If dashing, we add an immediate position offset (simulated as high velocity for one tick? No, just position check)
            // But we need to collision check the dash.
            // Let's apply dash as an immediate move BEFORE the regular move? Or part of it?
            // The request says "replace with dashing, moves the player 5 units forward".
            // Since this is a discrete position change, we should apply it as a separate MoveAndCollide or add to velocity?
            // "Instant" implies position change.


            if (input.IsDashing)
            {
                // Calculate dash vector
                // Normalize input direction, or use rotation if no input?
                // InputCommand.MoveDirection is already sanitized (normalized if length > 1).

                Vector3 dashDir = input.MoveDirection;
                if (dashDir.LengthSquared() < 0.001f)
                {
                    // No movement input - use visual/facing rotation (last movement direction)
                    var forwardX = MathF.Cos(player.VisualRotation);
                    var forwardY = MathF.Sin(player.VisualRotation);
                    dashDir = new Vector3(forwardX, forwardY, 0f);
                }
                else
                {
                    dashDir = Vector3.Normalize(dashDir);
                }

                // Apply Dash: Move and Collide
                // We do this BEFORE the regular movement so they can also walk in the same tick? Or instead?
                // Usually dash adds to mobility. Let's do it before.

                var dashVelocity = dashDir * (SimulationConfig.DashDistance / SimulationConfig.FixedDeltaTime);
                position = PhysicsSimulation.MoveAndCollide(
                    position,
                    ref dashVelocity, // Velocity to achieve distance in 1 tick
                    SimulationConfig.FixedDeltaTime,
                    SimulationConfig.PlayerRadius,
                    Boxes.Values.Select(b => b.Position),
                    (x, y, z) => ChunkManager.HasSolidBlockAtPosition(x, y, z));
            }

            velocity = new Vector3(horizontalVel.X, horizontalVel.Y, velocity.Z); // Preserve Z velocity
            player.Rotation = input.Rotation;

            // Store position before movement to detect underground swimming
            var positionBeforeMovement = position;

            // Apply regular physics with collision (same as server)
            position = PhysicsSimulation.MoveAndCollide(
                position,
                ref velocity,
                SimulationConfig.FixedDeltaTime,
                SimulationConfig.PlayerRadius,
                Boxes.Values.Select(b => b.Position),
                (x, y, z) => ChunkManager.HasSolidBlockAtPosition(x, y, z));

            // Clamp to ground if below
            PhysicsSimulation.ClampToGround(ref position, ref velocity, Boxes.Values.Select(b => b.Position), (x, y) => ChunkManager.GetTerrainHeight(x, y), previousPosition: positionBeforeMovement);

            // Write back to player entity
            player.Position = position;
            player.Velocity = velocity;
            player.Rotation = input.Rotation;
            // VisualRotation is updated after prediction from GetVisualRotation()
        }

        /// <summary>
        /// Raycasts against voxels to find the first hit (client-side version).
        /// Returns true if a voxel was hit.
        /// </summary>
        private bool RayCastVoxel(Vector3 start, Vector3 end, out Vector3 hitPos)
        {
            hitPos = Vector3.Zero;

            Vector3 direction = end - start;
            float maxDistance = direction.Length();


            if (maxDistance < 0.0001f)
                return false;


            direction /= maxDistance;

            // Simple step-based raycast (matches server implementation)
            float stepSize = SimulationConfig.BlockSize / 4f;
            float distance = 0f;

            while (distance <= maxDistance)
            {
                Vector3 currentPos = start + direction * distance;

                if (ChunkManager.HasBlockAtPosition(currentPos.X, currentPos.Y, currentPos.Z))
                {
                    hitPos = currentPos;
                    return true;
                }

                distance += stepSize;
            }

            return false;
        }

        /// <summary>
        /// Handles join response from server.
        /// </summary>
        public void HandleJoinResponse(JoinResponsePacket packet)
        {
            LocalPlayerId = packet.PlayerId;
            CurrentTick = packet.ServerTick;

            // Clear any residual state from previous session (defense in depth)
            // Note: This should already be cleared by Reset() on disconnect, but we clear again here
            // to handle edge cases and ensure clean state
            _inputHistory.Clear();
            _lastFireTick = 0;
            _lastReconciledTick = 0;
            PredictedProjectiles.Clear();
            while (InputBuffer.TryDequeue(out _))
            {
            }

            var localPlayer = new ClientEntity
            {
                Id = LocalPlayerId,
                Position = packet.SpawnPosition,
                Velocity = Vector3.Zero,
                IsLocalPlayer = true,
                Type = EntityType.Player
            };

            Players[LocalPlayerId] = localPlayer;

            // Initialize smoothing positions
            _localPlayerPrevPosition = packet.SpawnPosition;
            _localPlayerCurrentPosition = packet.SpawnPosition;

            NetworkLog.Information("Joined as {PlayerId}. Starting at tick {Tick}, position ({X:F1}, {Y:F1}, {Z:F1})",
                LocalPlayerId, CurrentTick, packet.SpawnPosition.X, packet.SpawnPosition.Y, packet.SpawnPosition.Z);
        }

        /// <summary>
        /// Handles static player information from server.
        /// This data is cached and doesn't change frequently.
        /// </summary>
        public void HandlePlayerInfo(PlayerInfoPacket packet)
        {
            _playerInfoCache[packet.PlayerId] = packet;

            // Update existing player entity if it exists
            if (Players.TryGetValue(packet.PlayerId, out var player))
            {
                player.Name = packet.Name;
                player.Health.Max = packet.MaxHealth;
            }
        }

        /// <summary>
        /// Handles world snapshot from server.
        /// Performs reconciliation for local player and updates remote entities.
        /// </summary>
        public void HandleWorldSnapshot(WorldSnapshot snapshot)
        {
            LastServerTick = snapshot.Tick;

            // Add to interpolation buffer for remote entities

            _interpolationBuffer.AddSnapshot(snapshot);

            // Process players

            foreach (var playerState in snapshot.Players)
            {
                if (playerState.Id == LocalPlayerId)
                {
                    // Reconcile local player
                    ReconcileLocalPlayer(playerState, snapshot.LastProcessedInputTick);
                }
                else
                {
                    // Update or create remote player
                    if (!Players.TryGetValue(playerState.Id, out var player))
                    {
                        // Get name and maxhealth from cache
                        var playerInfo = _playerInfoCache.GetValueOrDefault(playerState.Id);

                        player = new ClientEntity
                        {
                            Id = playerState.Id,
                            IsLocalPlayer = false,
                            Type = EntityType.Player,
                            Name = playerInfo?.Name ?? "Unknown"
                        };
                        Players[playerState.Id] = player;
                    }
                    else
                    {
                        // If player name is still "Unknown", try to update it from cache
                        if (player.Name == "Unknown" && _playerInfoCache.TryGetValue(playerState.Id, out var cachedInfo))
                        {
                            player.Name = cachedInfo.Name;
                            player.Health.Max = cachedInfo.MaxHealth;
                        }
                    }

                    // NOTE: Don't update player.Position or player.Rotation here!
                    // The interpolation buffer handles smooth position/rotation updates every frame in UpdateInterpolation()

                    // Update velocity (needed for animation state)
                    player.Velocity = playerState.Velocity;

                    // Update visual rotation from velocity (for movement-based facing direction)
                    if (playerState.Velocity.LengthSquared() > 0.1f)
                    {
                        player.VisualRotation = MathF.Atan2(playerState.Velocity.X, playerState.Velocity.Y);
                    }
                    // If not moving, preserve the last VisualRotation

                    // Deserialize health component
                    if (playerState.HealthData != null && playerState.HealthData.Length > 0)
                    {
                        player.Health = HealthComponent.Deserialize(playerState.HealthData);
                    }

                    // DEBUG: Update server position for debug visualization
                    player.ServerPosition = playerState.Position;
                }
            }

            // Process projectiles

            if (snapshot.Projectiles.Length > 0)
                ProjectileLog.Debug("Received snapshot with {Count} projectiles", snapshot.Projectiles.Length);

            // Get projectile IDs from THIS snapshot (not from interpolation buffer)
            var currentProjectileIds = new HashSet<uint>(snapshot.Projectiles.Select(p => p.Id));

            foreach (var projState in snapshot.Projectiles)
            {
                ProjectileLog.Verbose("Processing projectile {Id} from owner {OwnerId}", projState.Id, projState.OwnerId);

                // Create or update projectile from server
                if (!Projectiles.TryGetValue(projState.Id, out var projectile))
                {
                    ProjectileLog.Debug("Creating new projectile {Id}", projState.Id);
                    projectile = new ClientEntity
                    {
                        Id = Guid.NewGuid(),
                        ProjectileId = projState.Id,
                        Type = EntityType.Projectile,
                        OwnerId = projState.OwnerId
                    };
                    Projectiles[projState.Id] = projectile;
                }

                // NOTE: Don't update projectile.Position or projectile.Velocity here!
                // The interpolation buffer handles smooth position updates every frame in UpdateInterpolation()
                // Updating directly here causes jerky movement because it overwrites the extrapolated position

                projectile.OwnerId = projState.OwnerId; // Update in case it wasn't set initially
            }

            // Remove projectiles that no longer exist in the snapshot

            var toRemove = Projectiles.Keys.Except(currentProjectileIds).ToList();

            if (toRemove.Count > 0)
                ProjectileLog.Debug("Removing {Count} projectiles. Current count before removal: {CurrentCount}",
                    toRemove.Count, Projectiles.Count);
            foreach (var id in toRemove)
            {
                Projectiles.Remove(id);
            }

            // Remove disconnected players

            var currentPlayerIds = new HashSet<Guid>(snapshot.Players.Select(p => p.Id));
            var playersToRemove = Players.Keys.Except(currentPlayerIds).Where(id => id != LocalPlayerId).ToList();
            foreach (var id in playersToRemove)
            {
                Players.Remove(id);
            }

            // Process Boxes
            foreach (var boxState in snapshot.Boxes)
            {
                if (!Boxes.TryGetValue(boxState.Id, out var box))
                {
                    box = new ClientEntity
                    {
                        Id = Guid.NewGuid(), // Placeholder GUID
                        ProjectileId = boxState.Id, // Reuse ProjectileId field for box uint Id or add BoxId? 
                        // ClientEntity has ProjectileId (uint). I can use that or add BoxId.
                        // Or just use the dictionary key.
                        // ClientEntity.Id is Guid.
                        // Let's use ProjectileId for UInt ID since it's there.


                        Type = EntityType.Box,
                        Position = boxState.Position
                    };
                    Boxes[boxState.Id] = box;
                }
                // Boxes are static usually, but just in case they move (e.g. pushed?):
                // box.Position = boxState.Position; 
                // If static, we don't need update. But snapshot sends it.
                // "Spawn 10 random boxes... on start".
                // Assuming static.
            }
            // Technically should remove boxes that are gone, but boxes are static for now.
        }

        /// <summary>
        /// Reconciles local player by rewinding to server state and replaying inputs.
        /// </summary>
        private void ReconcileLocalPlayer(PlayerState serverState, uint lastProcessedTick)
        {
            if (!Players.TryGetValue(LocalPlayerId, out var localPlayer)) return;

            localPlayer.ServerPosition = serverState.Position;

            // Don't reconcile if we haven't processed this tick yet
            if (lastProcessedTick >= _lastReconciledTick)
            {
                _lastReconciledTick = lastProcessedTick;

                // Find the input that corresponds to the server's processed tick
                var historicalInput = _inputHistory.FirstOrDefault(i => i.Tick == lastProcessedTick);

                if (historicalInput != null)
                {
                    // 1. Get server state at tick T
                    // 2. We have the history of inputs. 
                    // 3. We assume our current state is based on inputs up to T + N.
                    // 4. We can't easily compare T vs T+N directly.
                    // 5. So we just reset to T, replay inputs T+1...T+N, and see if we end up at the same place as we are now.

                    // Rewind to server state

                    var positionBeforeReplay = localPlayer.Position;

                    localPlayer.Position = serverState.Position;
                    localPlayer.Velocity = serverState.Velocity;
                    localPlayer.Rotation = serverState.Rotation;

                    // Deserialize and update health from server
                    if (serverState.HealthData != null && serverState.HealthData.Length > 0)
                    {
                        localPlayer.Health = HealthComponent.Deserialize(serverState.HealthData);
                    }

                    // Replay all inputs after the processed tick

                    var inputsToReplay = _inputHistory.Where(i => i.Tick > lastProcessedTick).OrderBy(i => i.Tick);

                    foreach (var input in inputsToReplay)
                    {
                        PredictLocalPlayer(localPlayer, input);
                    }

                    // Check if the replayed position is different from what we had

                    var error = Vector3.Distance(positionBeforeReplay, localPlayer.Position);

                    if (error > SimulationConfig.ReconciliationThreshold)
                    {
                        ReconciliationLog.Information("Reconciliation: error={Error:F2} at tick {Tick}. Corrected.",
                            error, lastProcessedTick);

                        // Update smoothing positions to the corrected position to avoid visual teleport
                        _localPlayerPrevPosition = localPlayer.Position;
                        _localPlayerCurrentPosition = localPlayer.Position;

                        if (error > 50f)
                        {
                            OnLargeReconciliation?.Invoke(error);
                        }

                        // DEBUG: Record reconciliation event
                        localPlayer.ReconciliationHistory.Add(new ReconciliationInfo
                        {
                            Timestamp = DateTime.UtcNow,
                            ExpectedPosition = positionBeforeReplay, // Where we thought we were
                            ActualPosition = localPlayer.Position // Where we actually are now
                        });

                        // Limit history size
                        if (localPlayer.ReconciliationHistory.Count > 10)
                            localPlayer.ReconciliationHistory.RemoveAt(0);
                    }
                }

                // Clean up old input history

                _inputHistory.RemoveAll(i => i.Tick <= lastProcessedTick);
            }
        }

        // ============================================================================
        // TEST HELPER METHODS
        // ============================================================================

        /// <summary>
        /// Queue an input command directly (for testing).
        /// </summary>
        public void QueueInput(InputCommand input)
        {
            input.Tick = CurrentTick + 1;
            InputBuffer.Enqueue(input);

            // Also add to history for reconciliation
            _inputHistory.Add(input);
            if (_inputHistory.Count > SimulationConfig.MaxInputHistory)
            {
                _inputHistory.RemoveAt(0);
            }

            // Immediately predict on local player
            CurrentTick++;
            if (Players.TryGetValue(LocalPlayerId, out var localPlayer))
            {
                PredictLocalPlayer(localPlayer, input);

                if (input.Fire)
                {
                    // Spawn predicted projectile from current player position (only Z offset)
                    var direction = new Vector2(MathF.Cos(input.Rotation), MathF.Sin(input.Rotation));
                    var velocity = new Vector3(
                        direction.X * SimulationConfig.ProjectileSpeed,
                        direction.Y * SimulationConfig.ProjectileSpeed,
                        0f
                    );
                    var spawnPos = new Vector3(
                        localPlayer.Position.X,
                        localPlayer.Position.Y,
                        localPlayer.Position.Z + SimulationConfig.ProjectileSpawnZOffset
                    );

                    PredictedProjectiles.Add(new ClientEntity
                    {
                        Id = Guid.NewGuid(),
                        OwnerId = LocalPlayerId,
                        Position = spawnPos,
                        Velocity = velocity,
                        Type = EntityType.Projectile,
                        Lifetime = 0f
                    });
                }
            }
        }

        /// <summary>
        /// Get player position (for testing).
        /// </summary>
        public Vector3 GetPlayerPosition(Guid playerId)
        {
            if (Players.TryGetValue(playerId, out var player))
            {
                return player.Position;
            }

            return Vector3.Zero;
        }

        /// <summary>
        /// Get player health (for testing).
        /// </summary>
        public int GetPlayerHealth(Guid playerId)
        {
            if (Players.TryGetValue(playerId, out var player))
            {
                return player.Health.Current;
            }

            return 0;
        }

        /// <summary>
        /// Get projectile count (for testing).
        /// </summary>
        public int GetProjectileCount()
        {
            return Projectiles.Count + PredictedProjectiles.Count;
        }

        /// <summary>
        /// Handle chunk creation packet from server.
        /// </summary>
        public void OnChunkCreate(ChunkCreatePacket packet)
        {
            ChunkManager.AddChunk(packet.ChunkX, packet.ChunkY, packet.ChunkZ, packet.BlockTypes, packet.BlockHealth, packet.BlockData);
        }

        /// <summary>
        /// Handle chunk update packet from server.
        /// </summary>
        public void OnChunkUpdate(ChunkUpdatePacket packet)
        {
            ChunkManager.UpdateChunk(packet.ChunkX, packet.ChunkY, packet.ChunkZ, packet.Updates);
        }

        // ============================================================================
        // INVENTORY METHODS
        // ============================================================================

        /// <summary>
        /// Handle full inventory sync from server (sent on join).
        /// </summary>
        public void OnInventoryFullSync(InventoryFullSyncPacket packet)
        {
            var inventory = packet.GetInventory();
            NetworkLog.Information("OnInventoryFullSync called. PacketPlayerId={PacketPlayerId}, LocalPlayerId={LocalPlayerId}, InventoryNull={InventoryNull}",

                packet.PlayerId, LocalPlayerId, inventory == null);


            if (packet.PlayerId == LocalPlayerId && inventory != null)
            {
                NetworkLog.Information("Setting LocalPlayerInventory. Slot0={Item0}, Slot1={Item1}, Slot2={Item2}, Slot3={Item3}",
                    inventory.Slots[0].ItemType,
                    inventory.Slots[1].ItemType,
                    inventory.Slots[2].ItemType,
                    inventory.Slots[3].ItemType);


                LocalPlayerInventory = inventory;
                NetworkLog.Information("LocalPlayerInventory SET SUCCESSFULLY. Active slot: {ActiveSlot}",

                    LocalPlayerInventory.ActiveSlotIndex);
            }
            else
            {
                NetworkLog.Warning("Did NOT set inventory. Match={Match}, InventoryNotNull={NotNull}",
                    packet.PlayerId == LocalPlayerId, inventory != null);
            }
        }

        /// <summary>
        /// Handle item used event from server.
        /// </summary>
        public void OnItemUsed(ItemUsedEvent packet)
        {
            if (packet.PlayerId == LocalPlayerId)
            {
                // Update local cooldown tracking
                _lastItemUseTick[packet.ItemType] = packet.ServerTick;
                NetworkLog.Debug("Item {ItemType} used, cooldown started at tick {Tick}", packet.ItemType, packet.ServerTick);
            }
        }

        /// <summary>
        /// Handle inventory slot switched event from server.
        /// </summary>
        public void OnInventorySlotSwitched(InventorySlotSwitchedEvent packet)
        {
            if (packet.PlayerId == LocalPlayerId && LocalPlayerInventory != null)
            {
                LocalPlayerInventory.SwitchSlot(packet.SlotIndex);
                NetworkLog.Debug("Switched to slot {SlotIndex}", packet.SlotIndex);
            }
        }

        /// <summary>
        /// Handle status effect changed event from server.
        /// Server is authoritative for status effects.
        /// </summary>
        public void OnStatEffectChanged(StatEffectChanged packet)
        {
            if (Players.TryGetValue(packet.PlayerId, out var player))
            {
                player.StatusEffects = packet.GetActiveEffects();
                NetworkLog.Debug("Player {PlayerId} status effects: {Effects}",

                    packet.PlayerId, string.Join(", ", packet.GetActiveEffects()));
            }
        }

        /// <summary>
        /// Switch inventory slot (sends request to server).
        /// </summary>
        public void SwitchSlot(int slotIndex)
        {
            if (LocalPlayerInventory == null || slotIndex < 0 || slotIndex >= InventoryComponent.SlotCount)
                return;

            // Optimistically update local state
            LocalPlayerInventory.SwitchSlot(slotIndex);

            // Will be sent by GameClient when this method is called

            NetworkLog.Debug("Client: Requesting slot switch to {SlotIndex}", slotIndex);
        }

        /// <summary>
        /// Use the active inventory item (sends request to server).
        /// </summary>
        public bool UseActiveItem()
        {
            if (LocalPlayerInventory == null)
                return false;

            var activeSlot = LocalPlayerInventory.ActiveSlot;
            if (activeSlot.IsEmpty)
                return false;

            var itemDef = ItemDefinitions.Get(activeSlot.ItemType);
            var cooldownTicks = (uint)(itemDef.UseEffect.CooldownSeconds * SimulationConfig.ServerTickRate);

            // Check client-side cooldown prediction
            if (_lastItemUseTick.TryGetValue(activeSlot.ItemType, out var lastUseTick))
            {
                var ticksSinceLastUse = CurrentTick - lastUseTick;
                if (ticksSinceLastUse < cooldownTicks)
                {
                    NetworkLog.Debug("Client: Item {ItemType} on cooldown ({TicksRemaining} ticks remaining)",
                        activeSlot.ItemType, cooldownTicks - ticksSinceLastUse);
                    return false;
                }
            }

            NetworkLog.Debug("Client: Using item {ItemType} in slot {SlotIndex}",

                activeSlot.ItemType, LocalPlayerInventory.ActiveSlotIndex);
            return true;
        }

        /// <summary>
        /// Get the remaining cooldown for an item type in seconds.
        /// </summary>
        public float GetItemCooldownRemaining(ItemType itemType)
        {
            if (!_lastItemUseTick.TryGetValue(itemType, out var lastUseTick))
                return 0f;

            var itemDef = ItemDefinitions.Get(itemType);
            var cooldownTicks = (uint)(itemDef.UseEffect.CooldownSeconds * SimulationConfig.ServerTickRate);
            var ticksSinceLastUse = CurrentTick - lastUseTick;

            if (ticksSinceLastUse >= cooldownTicks)
                return 0f;

            var ticksRemaining = cooldownTicks - ticksSinceLastUse;
            return ticksRemaining * SimulationConfig.FixedDeltaTime;
        }

        /// <summary>
        /// Get the cooldown progress (0.0 = ready, 1.0 = just used).
        /// </summary>
        public float GetItemCooldownProgress(ItemType itemType)
        {
            if (!_lastItemUseTick.TryGetValue(itemType, out var lastUseTick))
                return 0f;

            var itemDef = ItemDefinitions.Get(itemType);
            var cooldownTicks = (uint)(itemDef.UseEffect.CooldownSeconds * SimulationConfig.ServerTickRate);
            var ticksSinceLastUse = CurrentTick - lastUseTick;

            if (ticksSinceLastUse >= cooldownTicks)
                return 0f;

            return 1f - (float)ticksSinceLastUse / cooldownTicks;
        }
    }
}

