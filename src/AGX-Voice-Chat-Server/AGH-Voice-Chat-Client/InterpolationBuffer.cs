using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AGH.Shared;
using static AGH_VOice_Chat_Client.LoggingConfig;

namespace AGH_VOice_Chat_Client
{
    /// <summary>
    /// Manages interpolation for remote entities (players and projectiles).
    /// Uses fixed 100ms delay buffer to smooth out network jitter.
    /// </summary>
    public class InterpolationBuffer
    {
        private struct Snapshot
        {
            public float Time; // Server tick converted to seconds
            public Dictionary<Guid, Vector3> PlayerPositions;
            public Dictionary<Guid, Vector3> PlayerVelocities;
            public Dictionary<Guid, float> PlayerRotations;
            public Dictionary<uint, Vector3> ProjectilePositions; // Keyed by ProjectileId
            public Dictionary<uint, Vector3> ProjectileVelocities;
        }

        private readonly List<Snapshot> _snapshots = [];

        private float _currentRenderTime;

        // Minimal delay for very fast snapping to server position (0.5 ticks)
        private static float FixedDelay => SimulationConfig.FixedDeltaTime * 0.5f;

        // More aggressive extrapolation for smoother fast movement
        private static float ExtrapolationLimit => SimulationConfig.FixedDeltaTime * 1.5f;

        // Toggle flags for debugging
        public bool InterpolationEnabled { get; set; } = true;
        public bool ExtrapolationEnabled { get; set; } = true;

        /// <summary>
        /// Adds a world snapshot to the buffer.
        /// </summary>
        public void AddSnapshot(WorldSnapshot snapshot)
        {
            var playerPositions = new Dictionary<Guid, Vector3>();
            var playerVelocities = new Dictionary<Guid, Vector3>();
            var playerRotations = new Dictionary<Guid, float>();
            var projectilePositions = new Dictionary<uint, Vector3>();
            var projectileVelocities = new Dictionary<uint, Vector3>();

            // Extract player data
            foreach (var player in snapshot.Players)
            {
                playerPositions[player.Id] = player.Position;
                playerVelocities[player.Id] = player.Velocity;
                playerRotations[player.Id] = player.Rotation;
            }

            // Extract projectile data
            foreach (var projectile in snapshot.Projectiles)
            {
                projectilePositions[projectile.Id] = projectile.Position;
                projectileVelocities[projectile.Id] = projectile.Velocity;
            }

            var snapshotTime = snapshot.Tick * SimulationConfig.FixedDeltaTime;

            var newSnapshot = new Snapshot
            {
                Time = snapshotTime,
                PlayerPositions = playerPositions,
                PlayerVelocities = playerVelocities,
                PlayerRotations = playerRotations,
                ProjectilePositions = projectilePositions,
                ProjectileVelocities = projectileVelocities
            };

            _snapshots.Add(newSnapshot);
            _snapshots.Sort((a, b) => a.Time.CompareTo(b.Time));

            // Initialize render time on first snapshot
            if (_snapshots.Count == 1)
            {
                _currentRenderTime = snapshotTime - FixedDelay;
            }

            // Remove very old snapshots (keep minimum 2 for interpolation)
            while (_snapshots.Count > SimulationConfig.MaxInterpolationSnapshots)
            {
                _snapshots.RemoveAt(0);
            }
        }

        /// <summary>
        /// Updates the interpolation time. Call once per frame.
        /// </summary>
        public void Update(float deltaTime)
        {
            if (_snapshots.Count == 0) return;

            // Advance render time at normal speed
            _currentRenderTime += deltaTime;

            // Allow extrapolation up to a reasonable limit
            // This enables smooth projectile rendering while preventing excessive drift
            var latestTime = _snapshots[_snapshots.Count - 1].Time;
            var maxExtrapolation = SimulationConfig.FixedDeltaTime * 3f; // Allow 3 ticks ahead
            var maxRenderTime = latestTime + maxExtrapolation;

            if (_currentRenderTime > maxRenderTime)
            {
                _currentRenderTime = maxRenderTime;
            }
        }

        /// <summary>
        /// Gets the interpolated position for a player.
        /// </summary>
        public Vector3 GetPlayerPosition(Guid playerId)
        {
            return InterpolatePositionWithVelocity(playerId,
                s => s.PlayerPositions,
                s => s.PlayerVelocities);
        }

        /// <summary>
        /// Gets the interpolated rotation for a player.
        /// </summary>
        public float GetPlayerRotation(Guid playerId)
        {
            return InterpolateValue(playerId,
                s => s.PlayerRotations,
                (a, b, t) => LerpAngle(a, b, t),
                0f);
        }

        /// <summary>
        /// Gets the interpolated position for a projectile.
        /// Uses aggressive extrapolation for smoother fast movement.
        /// </summary>
        public Vector3 GetProjectilePosition(uint projectileId)
        {
            return InterpolateProjectilePosition(projectileId);
        }

        /// <summary>
        /// Checks if a projectile exists in the buffer.
        /// </summary>
        public bool HasProjectile(uint projectileId)
        {
            if (_snapshots.Count == 0) return false;
            return _snapshots[^1].ProjectilePositions.ContainsKey(projectileId);
        }

        /// <summary>
        /// Gets all current projectile IDs from the latest snapshot.
        /// </summary>
        public IEnumerable<uint> GetCurrentProjectileIds()
        {
            if (_snapshots.Count == 0) return Enumerable.Empty<uint>();
            return _snapshots[_snapshots.Count - 1].ProjectilePositions.Keys;
        }

        /// <summary>
        /// Interpolates position with velocity-based prediction/extrapolation for smoother movement.
        /// </summary>
        private Vector3 InterpolatePositionWithVelocity<TKey>(
            TKey key,
            Func<Snapshot, Dictionary<TKey, Vector3>> positionSelector,
            Func<Snapshot, Dictionary<TKey, Vector3>> velocitySelector)
            where TKey : notnull
        {
            if (_snapshots.Count == 0) return Vector3.Zero;

            // If interpolation is disabled, just return the latest snapshot position
            if (!InterpolationEnabled && _snapshots.Count > 0)
            {
                var mostRecent = _snapshots[^1];
                var mostRecentPos = positionSelector(mostRecent);
                return mostRecentPos.TryGetValue(key, out var pos) ? pos : Vector3.Zero;
            }

            // Single snapshot - extrapolate using velocity (if enabled)
            if (_snapshots.Count == 1)
            {
                var snapshot = _snapshots[0];
                var positions = positionSelector(snapshot);
                var velocities = velocitySelector(snapshot);

                if (positions.TryGetValue(key, out var pos) && velocities.TryGetValue(key, out var vel))
                {
                    // Only extrapolate if enabled
                    if (ExtrapolationEnabled)
                    {
                        var extrapolationTime = Math.Min(_currentRenderTime - snapshot.Time, ExtrapolationLimit);
                        return pos + vel * extrapolationTime;
                    }

                    return pos;
                }

                return positions.TryGetValue(key, out var fallback) ? fallback : Vector3.Zero;
            }

            // Find surrounding snapshots
            for (int i = 0; i < _snapshots.Count - 1; i++)
            {
                var older = _snapshots[i];
                var newer = _snapshots[i + 1];

                if (_currentRenderTime >= older.Time && _currentRenderTime <= newer.Time)
                {
                    var olderPos = positionSelector(older);
                    var newerPos = positionSelector(newer);

                    if (olderPos.TryGetValue(key, out var oldPos) &&
                        newerPos.TryGetValue(key, out var newPos))
                    {
                        var duration = newer.Time - older.Time;
                        if (duration <= 0.0001f) return oldPos;

                        var t = (_currentRenderTime - older.Time) / duration;
                        // Pure interpolation - no velocity prediction to avoid rubberbanding
                        var interpolatedPos = Vector3.Lerp(oldPos, newPos, Math.Clamp(t, 0f, 1f));


                        return interpolatedPos;
                    }

                    // Entity exists in one snapshot but not the other - use whichever we have
                    if (olderPos.TryGetValue(key, out var fallback)) return fallback;
                    if (newerPos.TryGetValue(key, out fallback)) return fallback;
                }
            }

            // Past the latest snapshot - extrapolate using velocity (if enabled)
            var finalSnapshot = _snapshots[^1];
            var finalPos = positionSelector(finalSnapshot);
            var finalVel = velocitySelector(finalSnapshot);

            if (finalPos.TryGetValue(key, out var finalPosition) &&
                finalVel.TryGetValue(key, out var finalVelocity))
            {
                // Only extrapolate if enabled
                if (ExtrapolationEnabled)
                {
                    var extrapolationTime = Math.Min(_currentRenderTime - finalSnapshot.Time, ExtrapolationLimit);
                    return finalPosition + finalVelocity * extrapolationTime;
                }

                return finalPosition;
            }

            return finalPos.TryGetValue(key, out var lastFallback) ? lastFallback : Vector3.Zero;
        }

        /// <summary>
        /// Interpolates projectile position with aggressive extrapolation for smooth rendering.
        /// Projectiles move fast and linearly, so we can be more aggressive with prediction.
        /// </summary>
        private Vector3 InterpolateProjectilePosition(uint projectileId)
        {
            if (_snapshots.Count == 0) return Vector3.Zero;

            // If interpolation is disabled, just return the latest snapshot position
            if (!InterpolationEnabled && _snapshots.Count > 0)
            {
                var mostRecent = _snapshots[^1];
                return mostRecent.ProjectilePositions.TryGetValue(projectileId, out var pos) ? pos : Vector3.Zero;
            }

            // Get the latest snapshot with this projectile
            var latestSnapshot = _snapshots[^1];
            if (!latestSnapshot.ProjectilePositions.TryGetValue(projectileId, out var latestPos))
            {
                return Vector3.Zero; // Projectile doesn't exist
            }

            if (!latestSnapshot.ProjectileVelocities.TryGetValue(projectileId, out var latestVel))
            {
                return latestPos; // No velocity data, just return position
            }

            // For projectiles, we ALWAYS extrapolate from the latest known position
            // because they move fast and linearly (no acceleration, no collision prediction needed on client)
            // This gives the smoothest rendering at framerate
            
            var timeSinceLastSnapshot = _currentRenderTime - latestSnapshot.Time;
            
            // Clamp extrapolation to a reasonable limit (3 ticks worth)
            // This prevents projectiles from flying too far ahead if packets are delayed
            var maxExtrapolation = SimulationConfig.FixedDeltaTime * 3f;
            var extrapolationTime = Math.Clamp(timeSinceLastSnapshot, 0f, maxExtrapolation);
            
            // DEBUG: Log first few calls to see what's happening
            if (projectileId <= 5)
            {
                InterpolationLog.Verbose("[PROJ {Id}] renderTime:{RenderTime:F4} snapTime:{SnapTime:F4} delta:{Delta:F4} extrap:{Extrap:F4} pos:({X:F1},{Y:F1},{Z:F1}) vel:({VX:F1},{VY:F1},{VZ:F1})",
                    projectileId, _currentRenderTime, latestSnapshot.Time, timeSinceLastSnapshot, extrapolationTime,
                    latestPos.X, latestPos.Y, latestPos.Z, latestVel.X, latestVel.Y, latestVel.Z);
            }
            
            // Calculate extrapolated position
            var extrapolatedPos = latestPos + latestVel * extrapolationTime;
            
            return extrapolatedPos;
        }

        private T InterpolateValue<TKey, T>(
            TKey key,
            Func<Snapshot, Dictionary<TKey, T>> dictSelector,
            Func<T, T, float, T> lerp,
            T defaultValue)
            where TKey : notnull
        {
            switch (_snapshots.Count)
            {
                case 0:
                    return defaultValue;
                // Single snapshot - no interpolation
                case 1:
                {
                    var dict = dictSelector(_snapshots[0]);
                    return dict.GetValueOrDefault(key, defaultValue);
                }
            }

            // Find surrounding snapshots
            for (int i = 0; i < _snapshots.Count - 1; i++)
            {
                var older = _snapshots[i];
                var newer = _snapshots[i + 1];

                if (_currentRenderTime >= older.Time && _currentRenderTime <= newer.Time)
                {
                    var olderDict = dictSelector(older);
                    var newerDict = dictSelector(newer);

                    if (olderDict.TryGetValue(key, out var oldVal) &&
                        newerDict.TryGetValue(key, out var newVal))
                    {
                        var duration = newer.Time - older.Time;
                        if (duration <= 0.0001f) return oldVal;

                        var t = (_currentRenderTime - older.Time) / duration;
                        return lerp(oldVal, newVal, Math.Clamp(t, 0f, 1f));
                    }

                    // Entity exists in one snapshot but not the other - use whichever we have
                    if (olderDict.TryGetValue(key, out var fallback)) return fallback;
                    if (newerDict.TryGetValue(key, out fallback)) return fallback;
                }
            }

            // Past the latest snapshot - use latest value
            var latest = _snapshots[_snapshots.Count - 1];
            var latestDict = dictSelector(latest);
            return latestDict.TryGetValue(key, out var latestValue) ? latestValue : defaultValue;
        }

        private static float LerpAngle(float a, float b, float t)
        {
            // Handle angle wrapping for smooth rotation
            var diff = b - a;
            if (diff > MathF.PI) b -= MathF.PI * 2f;
            else if (diff < -MathF.PI) b += MathF.PI * 2f;
            return a + (b - a) * t;
        }

        /// <summary>
        /// Clears all buffered snapshots. Called when disconnecting/resetting.
        /// </summary>
        public void Clear()
        {
            _snapshots.Clear();
            _currentRenderTime = 0;
        }
    }
}