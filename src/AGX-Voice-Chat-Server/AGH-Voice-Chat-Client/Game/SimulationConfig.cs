namespace AGH_Voice_Chat_Client.Game
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

        public const float GroundLevel = -300f; // Z coordinate of the ground
      
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

        // === Network Simulation (for testing) ===

        public const bool SimulateLatency = false;
        public const int SimulationMinLatency = 100; // ms
        public const int SimulationMaxLatency = 200; // ms
        public const bool SimulatePacketLoss = false;
        public const int SimulationPacketLossChance = 2; // 2% packet loss
    }
}