using System;
using System.Collections.Generic;
using System.Numerics;

namespace AGH.Shared
{
    /// <summary>
    /// Deterministic physics simulation shared between client and server.
    /// CRITICAL: Client and server MUST use identical logic for prediction/reconciliation to work.
    /// </summary>
    public static class PhysicsSimulation
    {
        /// <summary>
        /// Simulates movement with collision detection and resolution.
        /// Returns the new position after moving and resolving any collisions.
        /// Modifies velocity when hitting ceilings or walls.
        /// </summary>
        public static Vector3 MoveAndCollide(Vector3 position, ref Vector3 velocity, float deltaTime, float radius, IEnumerable<Vector3> boxPositions = null, Func<float, float, float, bool> hasVoxelAt = null)
        {
            // Calculate desired new position
            var newPosition = position + velocity * deltaTime;

            // Check and resolve collisions
            // Wall collision logic removed

            // Check voxel collisions if checker provided
            if (hasVoxelAt != null)
            {
                newPosition = ResolveVoxelCollision(position, newPosition, radius, ref velocity, hasVoxelAt);
            }

            if (boxPositions != null)
            {
                newPosition = ResolveBoxCollision(position, newPosition, radius, ref velocity, boxPositions);
            }

            return newPosition;
        }


        private static Vector3 ResolveBoxCollision(Vector3 oldPosition, Vector3 newPosition, float radius, ref Vector3 velocity, IEnumerable<Vector3> boxPositions)
        {
            var resolvedPosition = newPosition;
            float halfBox = SimulationConfig.BoxSize / 2f;
            float playerHeight = radius * 2f;

            foreach (var boxPos in boxPositions)
            {
                // Box bounds (AABB)
                float boxMinX = boxPos.X - halfBox;
                float boxMaxX = boxPos.X + halfBox;
                float boxMinY = boxPos.Y - halfBox;
                float boxMaxY = boxPos.Y + halfBox;
                float boxMinZ = boxPos.Z - halfBox;
                float boxMaxZ = boxPos.Z + halfBox;

                // Find closest point on box to player position (XY plane)
                float closestX = Math.Clamp(resolvedPosition.X, boxMinX, boxMaxX);
                float closestY = Math.Clamp(resolvedPosition.Y, boxMinY, boxMaxY);

                // Calculate distance from player center to closest point on box
                float distanceX = resolvedPosition.X - closestX;
                float distanceY = resolvedPosition.Y - closestY;
                float distanceSquared = distanceX * distanceX + distanceY * distanceY;

                // Check if player's cylinder overlaps with box in XY plane
                if (distanceSquared < radius * radius)
                {
                    // Check vertical overlap
                    float playerBottom = resolvedPosition.Z;
                    float playerTop = resolvedPosition.Z + playerHeight;
                    float oldPlayerBottom = oldPosition.Z;
                    float oldPlayerTop = oldPosition.Z + playerHeight;

                    bool verticalOverlap = playerBottom < boxMaxZ && playerTop > boxMinZ;

                    if (verticalOverlap)
                    {
                        // Determine collision type: landing on top, hitting ceiling, or side collision

                        // Landing on top: was above and moving down

                        if (oldPlayerBottom >= boxMaxZ - 0.5f && resolvedPosition.Z < boxMaxZ && velocity.Z <= 0)
                        {
                            resolvedPosition.Z = boxMaxZ;
                            velocity.Z = 0f; // Stop vertical velocity on landing
                        }
                        // Hitting ceiling: was below and moving up
                        else if (oldPlayerTop <= boxMinZ + 0.5f && resolvedPosition.Z + playerHeight > boxMinZ && velocity.Z > 0)
                        {
                            resolvedPosition.Z = boxMinZ - playerHeight;
                            velocity.Z = 0f; // Stop upward velocity on ceiling hit
                        }
                        else
                        {
                            // Side collision - check for step-up first
                            float stepHeight = boxMaxZ - oldPlayerBottom;

                            // If obstacle is small enough, step up automatically

                            if (stepHeight > 0 && stepHeight <= SimulationConfig.MaxStepHeight && velocity.Z <= 0)
                            {
                                resolvedPosition.Z = boxMaxZ;
                                // Don't zero velocity - preserve horizontal movement
                            }
                            else
                            {
                                // Regular side collision - push player out horizontally only
                                // This allows free-fall sliding against walls
                                float distance = MathF.Sqrt(distanceSquared);

                                if (distance > 0.001f)
                                {
                                    float penetration = radius - distance;
                                    float normalX = distanceX / distance;
                                    float normalY = distanceY / distance;

                                    // Only push horizontally, preserve vertical movement for sliding
                                    resolvedPosition.X += normalX * penetration;
                                    resolvedPosition.Y += normalY * penetration;
                                    // DO NOT modify velocity.Z - allows free-fall against walls
                                }
                                else
                                {
                                    // Player at box center - push away
                                    float dirX = newPosition.X - oldPosition.X;
                                    float dirY = newPosition.Y - oldPosition.Y;
                                    float dirLength = MathF.Sqrt(dirX * dirX + dirY * dirY);

                                    if (dirLength > 0.001f)
                                    {
                                        resolvedPosition.X = boxPos.X + (dirX / dirLength) * (halfBox + radius);
                                        resolvedPosition.Y = boxPos.Y + (dirY / dirLength) * (halfBox + radius);
                                    }
                                    else
                                    {
                                        resolvedPosition.X = boxMaxX + radius;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return resolvedPosition;
        }

        /// <summary>
        /// Checks if a projectile hit a player.
        /// Uses circle-circle collision detection on XY plane.
        /// Projectile is at fixed Z=0.5, player is at Z=0, so vertical overlap is always true.
        /// </summary>
        public static bool CheckProjectileHit(Vector3 projectilePos, float projectileRadius,
                                               Vector3 playerPos, float playerRadius)
        {
            // Only check XY plane distance (projectile at Z=0.5, player at Z=0)
            var dx = projectilePos.X - playerPos.X;
            var dy = projectilePos.Y - playerPos.Y;
            var distanceSquared = dx * dx + dy * dy;
            var radiusSum = projectileRadius + playerRadius;
            return distanceSquared <= radiusSum * radiusSum;
        }

        /// <summary>
        /// Normalizes a direction vector if its length exceeds 1.
        /// Used to prevent speed hacks and ensure consistent movement.
        /// Only normalizes XY components - Z is handled separately.
        /// </summary>
        public static Vector3 SanitizeDirection(Vector3 direction)
        {
            // Only normalize horizontal movement (XY plane)
            var horizontalLengthSquared = direction.X * direction.X + direction.Y * direction.Y;
            if (horizontalLengthSquared > 1.0f)
            {
                var horizontalLength = MathF.Sqrt(horizontalLengthSquared);
                direction.X /= horizontalLength;
                direction.Y /= horizontalLength;
            }
            // Z component is preserved as-is
            return direction;
        }

        /// <summary>
        /// Calculates velocity from direction and speed.
        /// Only applies to horizontal movement (XY plane).
        /// </summary>
        public static Vector3 CalculateVelocity(Vector3 direction, float speed)
        {
            var sanitized = SanitizeDirection(direction);
            return new Vector3(sanitized.X * speed, sanitized.Y * speed, sanitized.Z); // Preserve Z velocity
        }

        /// <summary>
        /// Applies gravity to velocity (affects Z component only).
        /// </summary>
        public static void ApplyGravity(ref Vector3 velocity, float deltaTime, bool isClimbing = false)
        {
            if (isClimbing)
            {
                // Counteract any existing gravity/downward momentum when grabbing ladder, 
                // but only if not moving intentionally
                // Actually, just disable gravity application
                return;

            }
            velocity.Z -= SimulationConfig.Gravity * deltaTime;
        }

        /// <summary>
        /// Applies jump to velocity if player is grounded.
        /// </summary>
        public static void ApplyJump(ref Vector3 velocity, bool isGrounded, bool jumpPressed)
        {
            if (jumpPressed && isGrounded)
            {
                velocity.Z = SimulationConfig.JumpVelocity;
            }
        }

        public static void ApplyClimbingPhysics(ref Vector3 velocity, Vector3 moveDirection, float climbSpeed, bool jumpPressed)
        {
            // Up/Down movement on ladder
            // moveDirection.Y corresponds to Forward/Backward input (W/S)
            // W (+) = Up, S (-) = Down

            if (jumpPressed)
            {
                // Launch off ladder
                velocity.Z = SimulationConfig.JumpVelocity;
                // Add backward impulse? For now just jump up/off
                return;
            }

            // Direct control of vertical velocity
            velocity.Z = moveDirection.Y * climbSpeed;
        }

        /// <summary>
        /// Checks if entity is on the ground.
        /// </summary>
        /// <summary>
        /// Checks if entity is on the ground or on a box.
        /// </summary>
        public static bool IsGrounded(float z, Vector3 position, IEnumerable<Vector3> boxPositions = null, Func<float, float, float> getTerrainHeight = null)
        {
            if (z <= SimulationConfig.GroundLevel) return true;

            // Check terrain height if callback provided
            if (getTerrainHeight != null)
            {
                float terrainHeight = getTerrainHeight(position.X, position.Y);
                // Allow a small epsilon for floating point errors
                if (z <= terrainHeight + 1.0f) return true;
            }

            if (boxPositions != null)
            {
                float halfBox = SimulationConfig.BoxSize / 2f;
                float epsilon = 1.0f; // Tolerance

                foreach (var boxPos in boxPositions)
                {
                    // Check if we are on top of this box (XY overlap + Z close to top)
                    float halfSizePlusRadius = halfBox + SimulationConfig.PlayerRadius; // Approximate footprint

                    // Strict XY check? Player is a point/circle.
                    // Box is AABB. 
                    // Check if circle overlaps rectangle.
                    // Circle center: position.XY.
                    // Rect: boxPos.XY +/- halfBox.

                    float dx = Math.Abs(position.X - boxPos.X);
                    float dy = Math.Abs(position.Y - boxPos.Y);

                    bool onBoxXY = dx < (halfBox + SimulationConfig.PlayerRadius / 2) && dy < (halfBox + SimulationConfig.PlayerRadius / 2);
                    // Using slightly smaller radius for "feet" to prevent hanging off edge too much if desired, 
                    // but let's use standard.

                    if (onBoxXY)
                    {
                        float boxTop = boxPos.Z + halfBox;
                        if (Math.Abs(z - boxTop) <= epsilon)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Clamps entity to ground and zeroes vertical velocity when landing.
        /// Prevents horizontal movement when below ground to avoid "swimming" exploits.
        /// </summary>
        public static void ClampToGround(ref Vector3 position, ref Vector3 velocity, IEnumerable<Vector3> boxPositions = null, Func<float, float, float> getTerrainHeight = null, Vector3? previousPosition = null)
        {
            float groundLevel = SimulationConfig.GroundLevel;

            // Use voxel terrain height if available
            if (getTerrainHeight != null)
            {
                groundLevel = getTerrainHeight(position.X, position.Y);
            }

            if (position.Z < groundLevel)
            {
                // Player is below ground - clamp to ground level
                position.Z = groundLevel;
                velocity.Z = 0f;
                
                // If we have a previous position, check if player was also below ground there
                // This prevents "swimming" through terrain by moving horizontally while below ground
                if (previousPosition.HasValue && getTerrainHeight != null)
                {
                    float previousGroundLevel = getTerrainHeight(previousPosition.Value.X, previousPosition.Value.Y);
                    if (previousPosition.Value.Z < previousGroundLevel)
                    {
                        // Player was already below ground at previous position
                        // Restore horizontal position to prevent underground movement
                        position.X = previousPosition.Value.X;
                        position.Y = previousPosition.Value.Y;
                    }
                }
            }
            else if (boxPositions != null)
            {
                // Check if we are grounded on a box
                if (IsGrounded(position.Z, position, boxPositions, getTerrainHeight))
                {
                    if (velocity.Z < 0) velocity.Z = 0f;
                }
            }
        }

        /// <summary>
        /// Resolves collision with voxel terrain
        /// </summary>
        private static Vector3 ResolveVoxelCollision(
            Vector3 oldPosition,
            Vector3 newPosition,
            float radius,
            ref Vector3 velocity,
            Func<float, float, float, bool> hasVoxelAt)
        {
            var resolvedPosition = newPosition;
            float playerHeight = radius * 2f; // Approximate player as cylinder

            // Check collision with voxels in a small area around player
            int checkRadius = (int)Math.Ceiling(radius / SimulationConfig.BlockSize) + 1;
            int centerBlockX = (int)Math.Floor((newPosition.X - SimulationConfig.WorldMinX) / SimulationConfig.BlockSize);
            int centerBlockY = (int)Math.Floor((newPosition.Y - SimulationConfig.WorldMinY) / SimulationConfig.BlockSize);

            for (int dx = -checkRadius; dx <= checkRadius; dx++)
            {
                for (int dy = -checkRadius; dy <= checkRadius; dy++)
                {
                    float blockWorldX = SimulationConfig.WorldMinX + (centerBlockX + dx) * SimulationConfig.BlockSize;
                    float blockWorldY = SimulationConfig.WorldMinY + (centerBlockY + dy) * SimulationConfig.BlockSize;

                    // Check multiple heights
                    for (int blockZ = 0; blockZ < SimulationConfig.ChunkHeight; blockZ++)
                    {
                        float blockWorldZ = blockZ * SimulationConfig.BlockSize;

                        // Skip if no voxel here
                        if (!hasVoxelAt(blockWorldX, blockWorldY, blockWorldZ))
                            continue;

                        // AABB collision check
                        float blockSize = SimulationConfig.BlockSize;
                        float blockMinX = blockWorldX;
                        float blockMaxX = blockWorldX + blockSize;
                        float blockMinY = blockWorldY;
                        float blockMaxY = blockWorldY + blockSize;
                        float blockMinZ = blockWorldZ;
                        float blockMaxZ = blockWorldZ + blockSize;

                        // Find closest point on block to player
                        float closestX = Math.Clamp(resolvedPosition.X, blockMinX, blockMaxX);
                        float closestY = Math.Clamp(resolvedPosition.Y, blockMinY, blockMaxY);

                        float distX = resolvedPosition.X - closestX;
                        float distY = resolvedPosition.Y - closestY;
                        float distSq = distX * distX + distY * distY;

                        // Check horizontal overlap
                        if (distSq < radius * radius)
                        {
                            // Check vertical overlap
                            float playerBottom = resolvedPosition.Z;
                            float playerTop = resolvedPosition.Z + playerHeight;
                            float oldPlayerBottom = oldPosition.Z;
                            float oldPlayerTop = oldPosition.Z + playerHeight;

                            if (playerBottom < blockMaxZ && playerTop > blockMinZ)
                            {
                                // Collision! Determine type: landing on top, hitting ceiling, or side collision

                                // Landing on top: was above and moving down
                                if (oldPlayerBottom >= blockMaxZ - 0.5f && resolvedPosition.Z < blockMaxZ && velocity.Z <= 0)
                                {
                                    resolvedPosition.Z = blockMaxZ;
                                    velocity.Z = 0f; // Stop falling when landing
                                }
                                // Hitting ceiling: was below and moving up
                                else if (oldPlayerTop <= blockMinZ + 0.5f && resolvedPosition.Z + playerHeight > blockMinZ && velocity.Z > 0)
                                {
                                    resolvedPosition.Z = blockMinZ - playerHeight;
                                    velocity.Z = 0f; // Stop upward velocity on ceiling hit
                                }
                                else
                                {
                                    // Side collision - check for step-up first
                                    float stepHeight = blockMaxZ - oldPlayerBottom;

                                    // If obstacle is small enough, step up automatically
                                    if (stepHeight > 0 && stepHeight <= SimulationConfig.MaxStepHeight && velocity.Z <= 0)
                                    {
                                        resolvedPosition.Z = blockMaxZ;
                                        // Don't zero velocity - preserve horizontal movement
                                    }
                                    else
                                    {
                                        // Regular side collision - push player out horizontally only
                                        // This allows free-fall sliding against walls
                                        float dist = MathF.Sqrt(distSq);
                                        if (dist > 0.001f)
                                        {
                                            float penetration = radius - dist;
                                            float normalX = distX / dist;
                                            float normalY = distY / dist;

                                            // Only push horizontally, preserve vertical movement for sliding
                                            resolvedPosition.X += normalX * penetration;
                                            resolvedPosition.Y += normalY * penetration;
                                            // DO NOT modify velocity.Z - allows free-fall against walls
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return resolvedPosition;
        }
    }
}
