using System;
using System.Collections.Generic;
using AGH.Shared;
using static AGH_VOice_Chat_Client.LoggingConfig;

namespace AGH_VOice_Chat_Client
{
    /// <summary>
    /// Client-side chunk manager.
    /// Receives and stores chunks from server.
    /// </summary>
    public class ClientChunkManager
    {
        private readonly Dictionary<(int, int, int), VoxelChunk> _chunks = new();

        public void AddChunk(int chunkX, int chunkY, int chunkZ, byte[] blockTypes, byte[] blockHealth, byte[] blockData)
        {
            var chunk = new VoxelChunk(chunkX, chunkY, chunkZ);
            int expectedSize = SimulationConfig.ChunkSize * SimulationConfig.ChunkSize * SimulationConfig.ChunkHeight;

            // Validate and assign arrays - only replace if correct size, otherwise keep initialized arrays

            if (blockTypes != null && blockTypes.Length == expectedSize)
            {
                chunk.BlockTypes = blockTypes;
            }
            else
            {
                ChunksLog.Warning("BlockTypes array invalid for chunk ({ChunkX},{ChunkY},{ChunkZ}). Expected: {Expected}, Got: {Actual}. Using default.",
                    chunkX, chunkY, chunkZ, expectedSize, blockTypes?.Length ?? 0);
            }


            if (blockHealth != null && blockHealth.Length == expectedSize)
            {
                chunk.BlockHealth = blockHealth;
            }
            else
            {
                ChunksLog.Warning("BlockHealth array invalid for chunk ({ChunkX},{ChunkY},{ChunkZ}). Expected: {Expected}, Got: {Actual}. Using default.",
                    chunkX, chunkY, chunkZ, expectedSize, blockHealth?.Length ?? 0);
            }


            if (blockData != null && blockData.Length == expectedSize)
            {
                chunk.BlockData = blockData;
            }
            else
            {
                ChunksLog.Warning("BlockData array invalid for chunk ({ChunkX},{ChunkY},{ChunkZ}). Expected: {Expected}, Got: {Actual}. Using default.",
                    chunkX, chunkY, chunkZ, expectedSize, blockData?.Length ?? 0);
            }


            _chunks[(chunkX, chunkY, chunkZ)] = chunk;
        }

        public void AddChunk(int chunkX, int chunkY, int chunkZ, bool[] blocks, byte[] blockTypes, byte[] blockHealth, byte[] blockData)
        {
            var chunk = new VoxelChunk(chunkX, chunkY, chunkZ);
            int expectedSize = SimulationConfig.ChunkSize * SimulationConfig.ChunkSize * SimulationConfig.ChunkHeight;

            // Validate and assign arrays - only replace if correct size, otherwise keep initialized arrays

            if (blockTypes != null && blockTypes.Length == expectedSize)
            {
                chunk.BlockTypes = blockTypes;
            }
            else
            {
                ChunksLog.Warning("BlockTypes array invalid for chunk ({ChunkX},{ChunkY},{ChunkZ}). Expected: {Expected}, Got: {Actual}. Using default.",
                    chunkX, chunkY, chunkZ, expectedSize, blockTypes?.Length ?? 0);
            }


            if (blockHealth != null && blockHealth.Length == expectedSize)
            {
                chunk.BlockHealth = blockHealth;
            }
            else
            {
                ChunksLog.Warning("BlockHealth array invalid for chunk ({ChunkX},{ChunkY},{ChunkZ}). Expected: {Expected}, Got: {Actual}. Using default.",
                    chunkX, chunkY, chunkZ, expectedSize, blockHealth?.Length ?? 0);
            }


            if (blockData != null && blockData.Length == expectedSize)
            {
                chunk.BlockData = blockData;
            }
            else
            {
                ChunksLog.Warning("BlockData array invalid for chunk ({ChunkX},{ChunkY},{ChunkZ}). Expected: {Expected}, Got: {Actual}. Using default.",
                    chunkX, chunkY, chunkZ, expectedSize, blockData?.Length ?? 0);
            }


            _chunks[(chunkX, chunkY, chunkZ)] = chunk;
        }

        public void UpdateChunk(int chunkX, int chunkY, int chunkZ, BlockUpdate[] updates)
        {
            if (!_chunks.TryGetValue((chunkX, chunkY, chunkZ), out var chunk))
            {
                ChunksLog.Warning("Cannot update chunk ({ChunkX},{ChunkY},{ChunkZ}) - chunk does not exist! Updates: {Count}",
                    chunkX, chunkY, chunkZ, updates.Length);
                return;
            }

            ChunksLog.Information("Updating chunk ({ChunkX},{ChunkY},{ChunkZ}) with {Count} block updates",
                chunkX, chunkY, chunkZ, updates.Length);

            foreach (var update in updates)
            {
                ChunksLog.Information("Raw update data: LocalX={X}, LocalY={Y}, LocalZ={Z}, Exists={Exists}, BlockType(byte)={BlockTypeByte}, BlockType(enum)={BlockTypeEnum}, Health={Health}",
                    update.LocalX, update.LocalY, update.LocalZ, update.Exists, update.BlockType, (VoxelType)update.BlockType, update.Health);

                // Convert Exists byte to bool (0 = false, non-zero = true)
                chunk.SetBlock(update.LocalX, update.LocalY, update.LocalZ, update.Exists != 0, (VoxelType)update.BlockType);
                chunk.SetBlockHealth(update.LocalX, update.LocalY, update.LocalZ, update.Health);
                chunk.SetBlockData(update.LocalX, update.LocalY, update.LocalZ, update.Data);


                ChunksLog.Information("After update: Block at ({X},{Y},{Z}) - Type={Type}, Health={Health}",
                    update.LocalX, update.LocalY, update.LocalZ, chunk.GetBlockType(update.LocalX, update.LocalY, update.LocalZ), chunk.GetBlockHealth(update.LocalX, update.LocalY, update.LocalZ));
            }
        }

        public VoxelChunk? GetChunk(int chunkX, int chunkY, int chunkZ = 0)
        {
            return _chunks.TryGetValue((chunkX, chunkY, chunkZ), out var chunk) ? chunk : null;
        }

        public IEnumerable<VoxelChunk> GetAllChunks()
        {
            return _chunks.Values;
        }

        public bool HasBlockAtPosition(float worldX, float worldY, float worldZ = 0)
        {
            int blockX = (int)Math.Floor((worldX - SimulationConfig.WorldMinX) / SimulationConfig.BlockSize);
            int blockY = (int)Math.Floor((worldY - SimulationConfig.WorldMinY) / SimulationConfig.BlockSize);
            int blockZ = (int)Math.Floor(worldZ / SimulationConfig.BlockSize);

            int chunkX = blockX / SimulationConfig.ChunkSize;
            int chunkY = blockY / SimulationConfig.ChunkSize;
            int chunkZ = blockZ / SimulationConfig.ChunkHeight;
            int localX = blockX % SimulationConfig.ChunkSize;
            int localY = blockY % SimulationConfig.ChunkSize;
            int localZ = blockZ % SimulationConfig.ChunkHeight;

            var chunk = GetChunk(chunkX, chunkY, chunkZ);
            return chunk?.GetBlock(localX, localY, localZ) ?? false;
        }

        /// <summary>
        /// Get the block type at a world position
        /// </summary>
        public VoxelType GetBlockTypeAtPosition(float worldX, float worldY, float worldZ = 0)
        {
            int blockX = (int)Math.Floor((worldX - SimulationConfig.WorldMinX) / SimulationConfig.BlockSize);
            int blockY = (int)Math.Floor((worldY - SimulationConfig.WorldMinY) / SimulationConfig.BlockSize);
            int blockZ = (int)Math.Floor(worldZ / SimulationConfig.BlockSize);

            int chunkX = blockX / SimulationConfig.ChunkSize;
            int chunkY = blockY / SimulationConfig.ChunkSize;
            int chunkZ = blockZ / SimulationConfig.ChunkHeight;
            int localX = blockX % SimulationConfig.ChunkSize;
            int localY = blockY % SimulationConfig.ChunkSize;
            int localZ = blockZ % SimulationConfig.ChunkHeight;

            var chunk = GetChunk(chunkX, chunkY, chunkZ);
            if (chunk == null)
                return VoxelType.Cube; // Default to Cube if out of bounds

            return chunk.GetBlockType(localX, localY, localZ);
        }

        /// <summary>
        /// Check if a world position has a SOLID block (not water/ladder)
        /// </summary>
        public bool HasSolidBlockAtPosition(float worldX, float worldY, float worldZ = 0)
        {
            var blockType = GetBlockTypeAtPosition(worldX, worldY, worldZ);

            // First check if there's a block at all (not Air)

            if (!HasBlockAtPosition(worldX, worldY, worldZ))
                return false;

            // Then check if it's a solid type

            return VoxelChunk.IsSolid(blockType);
        }

        /// <summary>
        /// Get the height of the terrain (top solid block) at a world XY position
        /// Returns the Z coordinate of the top of the highest solid block
        /// </summary>
        public float GetTerrainHeight(float worldX, float worldY)
        {
            // Convert to block coordinates
            int blockX = (int)Math.Floor((worldX - SimulationConfig.WorldMinX) / SimulationConfig.BlockSize);
            int blockY = (int)Math.Floor((worldY - SimulationConfig.WorldMinY) / SimulationConfig.BlockSize);

            int chunkX = blockX / SimulationConfig.ChunkSize;
            int chunkY = blockY / SimulationConfig.ChunkSize;
            int localX = blockX % SimulationConfig.ChunkSize;
            int localY = blockY % SimulationConfig.ChunkSize;

            var chunk = GetChunk(chunkX, chunkY);
            if (chunk == null)
                return SimulationConfig.GroundLevel;

            // Search from top to bottom for the first SOLID block (not water/ladder)
            for (int localZ = SimulationConfig.ChunkHeight - 1; localZ >= 0; localZ--)
            {
                if (chunk.GetBlock(localX, localY, localZ))
                {
                    var blockType = chunk.GetBlockType(localX, localY, localZ);
                    // Only count solid blocks as terrain
                    if (VoxelChunk.IsSolid(blockType))
                    {
                        // Return the top of this block
                        return (localZ + 1) * SimulationConfig.BlockSize;
                    }
                }
            }

            // No solid blocks found (hole or only water/ladder), return ground level
            return SimulationConfig.GroundLevel;
        }
    }
}
