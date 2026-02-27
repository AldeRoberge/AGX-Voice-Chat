using System.Numerics;

namespace AGH.Shared
{

    /// <summary>
    /// Represents a chunk of voxel blocks in the world.
    /// Chunks are fixed-size 3D grids of blocks.
    /// </summary>
    public class VoxelChunk
    {
        public int ChunkX { get; }
        public int ChunkY { get; }
        public int ChunkZ { get; }

        // Blocks stored as 1D array: index = x + y * ChunkSize + z * ChunkSize * ChunkSize
        // Stores the type of each block (Air, Solid, or fence types)
        public byte[] BlockTypes { get; set; }

        // Health of each block (0-100), stored same way as BlocksTypes
        public byte[] BlockHealth { get; set; }

        // Metadata for each block (e.g., Rotation), stored same way as BlockTypes

        public byte[] BlockData { get; set; }

        public VoxelChunk(int chunkX, int chunkY, int chunkZ = 0)
        {
            ChunkX = chunkX;
            ChunkY = chunkY;
            ChunkZ = chunkZ;
            int size = SimulationConfig.ChunkSize * SimulationConfig.ChunkSize * SimulationConfig.ChunkHeight;
            BlockTypes = new byte[size];
            BlockHealth = new byte[size];
            BlockData = new byte[size];

            // Initialize all blocks as air by default (byte array defaults to 0, which is VoxelType.Air)
            // Terrain generation will place blocks where needed
        }

        /// <summary>
        /// Get block at local chunk coordinates (returns true if not Air)
        /// </summary>
        public bool GetBlock(int localX, int localY, int localZ)
        {
            if (localX < 0 || localX >= SimulationConfig.ChunkSize ||
                localY < 0 || localY >= SimulationConfig.ChunkSize ||
                localZ < 0 || localZ >= SimulationConfig.ChunkHeight)
                return false;

            return BlockTypes[localX + localY * SimulationConfig.ChunkSize + localZ * SimulationConfig.ChunkSize * SimulationConfig.ChunkSize] != (byte)VoxelType.Air;
        }

        /// <summary>
        /// Set block at local chunk coordinates
        /// </summary>
        public void SetBlock(int localX, int localY, int localZ, bool exists, VoxelType blockType = VoxelType.Cube)
        {
            SetBlockType(localX, localY, localZ, exists ? blockType : VoxelType.Air);
        }

        /// <summary>
        /// Set block type at local chunk coordinates
        /// </summary>
        public void SetBlockType(int localX, int localY, int localZ, VoxelType type)
        {
            if (localX < 0 || localX >= SimulationConfig.ChunkSize ||
                localY < 0 || localY >= SimulationConfig.ChunkSize ||
                localZ < 0 || localZ >= SimulationConfig.ChunkHeight)
                return;

            int index = localX + localY * SimulationConfig.ChunkSize + localZ * SimulationConfig.ChunkSize * SimulationConfig.ChunkSize;
            BlockTypes[index] = (byte)type;
            if (type != VoxelType.Air)
            {
                BlockHealth[index] = 100; // Reset health when placing block
            }
            else
            {
                BlockHealth[index] = 0;
                BlockData[index] = 0; // Reset data when removing block
            }
        }

        public void SetBlockData(int localX, int localY, int localZ, byte data)
        {
            if (localX < 0 || localX >= SimulationConfig.ChunkSize ||
                localY < 0 || localY >= SimulationConfig.ChunkSize ||
                localZ < 0 || localZ >= SimulationConfig.ChunkHeight)
                return;

            int index = localX + localY * SimulationConfig.ChunkSize + localZ * SimulationConfig.ChunkSize * SimulationConfig.ChunkSize;
            BlockData[index] = data;

        }

        public byte GetBlockData(int localX, int localY, int localZ)
        {
            if (localX < 0 || localX >= SimulationConfig.ChunkSize ||
                localY < 0 || localY >= SimulationConfig.ChunkSize ||
                localZ < 0 || localZ >= SimulationConfig.ChunkHeight)
                return 0;

            int index = localX + localY * SimulationConfig.ChunkSize + localZ * SimulationConfig.ChunkSize * SimulationConfig.ChunkSize;
            return BlockData[index];
        }

        /// <summary>
        /// Get block type at local chunk coordinates
        /// </summary>
        public VoxelType GetBlockType(int localX, int localY, int localZ)
        {
            if (localX < 0 || localX >= SimulationConfig.ChunkSize ||
                localY < 0 || localY >= SimulationConfig.ChunkSize ||
                localZ < 0 || localZ >= SimulationConfig.ChunkHeight)
                return VoxelType.Cube;

            return (VoxelType)BlockTypes[localX + localY * SimulationConfig.ChunkSize + localZ * SimulationConfig.ChunkSize * SimulationConfig.ChunkSize];
        }


        public byte GetBlockHealth(int localX, int localY, int localZ)
        {
            if (localX < 0 || localX >= SimulationConfig.ChunkSize ||
                localY < 0 || localY >= SimulationConfig.ChunkSize ||
                localZ < 0 || localZ >= SimulationConfig.ChunkHeight)
                return 0;

            return BlockHealth[localX + localY * SimulationConfig.ChunkSize + localZ * SimulationConfig.ChunkSize * SimulationConfig.ChunkSize];
        }

        public void SetBlockHealth(int localX, int localY, int localZ, byte health)
        {
            if (localX < 0 || localX >= SimulationConfig.ChunkSize ||
                localY < 0 || localY >= SimulationConfig.ChunkSize ||
                localZ < 0 || localZ >= SimulationConfig.ChunkHeight)
                return;

            BlockHealth[localX + localY * SimulationConfig.ChunkSize + localZ * SimulationConfig.ChunkSize * SimulationConfig.ChunkSize] = health;
        }

        /// <summary>
        /// Get world position of chunk's origin
        /// </summary>
        public Vector3 GetWorldPosition()
        {
            float worldX = SimulationConfig.WorldMinX + ChunkX * SimulationConfig.ChunkSize * SimulationConfig.BlockSize;
            float worldY = SimulationConfig.WorldMinY + ChunkY * SimulationConfig.ChunkSize * SimulationConfig.BlockSize;
            float worldZ = ChunkZ * SimulationConfig.ChunkHeight * SimulationConfig.BlockSize;
            return new Vector3(worldX, worldY, worldZ);
        }

        /// <summary>
        /// Check if a block type is a fence
        /// </summary>
        public static bool IsFence(VoxelType type)
        {
            return type == VoxelType.WoodFence || type == VoxelType.HedgeFence ||
                   type == VoxelType.StoneFence || type == VoxelType.IronFence;
        }

        /// <summary>
        /// Check if a voxel type should block player movement (solid collision)
        /// Water and Ladder are passable, Cube and Ramp are solid, Fences are solid
        /// </summary>
        public static bool IsSolid(VoxelType type)
        {
            return type == VoxelType.Cube || type == VoxelType.Ramp || IsFence(type);
        }
    }
}