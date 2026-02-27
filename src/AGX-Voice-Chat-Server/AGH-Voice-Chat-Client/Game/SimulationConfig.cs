namespace AGH.Shared
{
    /// <summary>
    /// Core simulation configuration for deterministic server-authoritative networking.
    /// Both server and client must use these exact values for prediction/reconciliation to work.
    /// </summary>
    public static class SimulationConfig
    {
        // === Tick Configuration ===d
        public const int ServerTickRate = 100; // Server simulation tick rate (20 Hz)
        public const float FixedDeltaTime = 1f / ServerTickRate; // Time per tick
        public const int SnapshotRate = 5; // Snapshots per second (every 4 ticks at 20 Hz)
        public const int TicksPerSnapshot = ServerTickRate / SnapshotRate;

        // === Player Physics ===

        public const float PlayerSpeed = 300f; // Units per second
        public const float ClimbSpeed = 200f; // Limit vertical speed on ladders
        public const float DashDistance = 50f; // Instant dash distance (units)
        public const float PlayerRadius = 14f; // Collision radius (reduced to fit through single-block gaps)

        // === Gravity & Jumping ===

        public const float Gravity = 980f; // Units per second squared (downward acceleration)
        public const float JumpVelocity = 400f; // Initial upward velocity when jumping
        public const float GroundLevel = -300f; // Z coordinate of the ground
        public const float CeilingLevel = 200f; // Z coordinate of the ceiling
        public const float DeathLevel = -200f; // Z coordinate below which players respawn (fell off the world)

        // === Projectile Physics ===

        public const float ProjectileSpeed = 800f; // Units per second
        public const float ProjectileRadius = 5f; // Collision radius
        public const float ProjectileMaxLifetime = 3f; // Seconds before auto-destroy
        public const float FireCooldown = 0.1f; // Seconds between shots (10 shots per second - faster fire rate)
        public const float ProjectileSpawnZOffset = 16f; // Offset Z height from player position for projectile spawning

        // === Collision Configuration ===

        public const float MaxStepHeight = BlockSize * 0.5f; // 16 units - auto step-up height (0.5 blocks)
        public const float BoxSize = 32f; // Size of the box entity (cube)

        // === Voxel/Chunk Configuration ===


        public const int ChunkSize = 16; // Blocks per chunk dimension (horizontal X and Y)
        public const int ChunkHeight = 8; // Height of chunks in blocks (vertical Z)
        public const float BlockSize = 32f; // Size of each voxel block in world units

        // === World Dimensions ===


        public const int WorldChunksX = 4; // Number of chunks in X direction
        public const int WorldChunksY = 4; // Number of chunks in Y direction
        public const int WorldBlocksX = WorldChunksX * ChunkSize; // Total blocks in X (64)
        public const int WorldBlocksY = WorldChunksY * ChunkSize; // Total blocks in Y (64)
        public const float WorldWidth = WorldBlocksX * BlockSize; // Total world width (2048)
        public const float WorldHeight = WorldBlocksY * BlockSize; // Total world height (2048)
        public const float WorldMinX = -WorldWidth / 2; // -1024
        public const float WorldMaxX = WorldWidth / 2; // 1024
        public const float WorldMinY = -WorldHeight / 2; // -1024
        public const float WorldMaxY = WorldHeight / 2; // 1024


        public const float HardWorldLimit = 10000f; // Limit for teleporting out-of-bounds players

        // === Client Prediction ===

        public const int MaxInputHistory = 200; // Max stored inputs for reconciliation
        public const float ReconciliationThreshold = 5f; // Min error to trigger reconciliation (units)
        public const int MaxInputBufferSize = 20; // Max queued inputs before dropping

        // === Interpolation ===


        public const int MaxInterpolationSnapshots = 32; // Buffer size

        // === Network Simulation (for testing) ===

        public const bool SimulateLatency = false;
        public const int SimulationMinLatency = 100; // ms
        public const int SimulationMaxLatency = 200; // ms
        public const bool SimulatePacketLoss = false;
        public const int SimulationPacketLossChance = 2; // 2% packet loss
    }
}