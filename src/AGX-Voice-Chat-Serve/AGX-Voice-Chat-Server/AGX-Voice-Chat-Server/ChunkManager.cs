using System.Numerics;
using AGH.Shared;
using Serilog;

namespace AGX_Voice_Chat_Server
{
    /// <summary>
    /// Server-side chunk manager.
    /// Generates and manages voxel chunks for the world.
    /// </summary>
    public class ChunkManager
    {
        private readonly Dictionary<(int, int, int), VoxelChunk> _chunks = new();

        public ChunkManager()
        {
            GenerateWorld();
        }

        private void GenerateWorld()
        {
            // Create all chunks (only one layer for now, Z=0)
            for (int cx = 0; cx < SimulationConfig.WorldChunksX; cx++)
            {
                for (int cy = 0; cy < SimulationConfig.WorldChunksY; cy++)
                {
                    var chunk = new VoxelChunk(cx, cy, 0);

                    // Generate simple terrain with varying heights
                    GenerateTerrainInChunk(chunk, cx, cy);

                    _chunks[(cx, cy, 0)] = chunk;
                }
            }

            // Create holes by removing blocks
            CreateHoles();

            // Create wall structure
            CreateWall();

            // Create fence examples
            CreateFences();

            // Create ladder on the wall
            CreateLadder();

            Log.Information("Generated {ChunksCount} chunks ({WorldChunksX}x{WorldChunksY}x1)", _chunks.Count, SimulationConfig.WorldChunksX, SimulationConfig.WorldChunksY);
        }

        private void GenerateTerrainInChunk(VoxelChunk chunk, int chunkX, int chunkY)
        {
            // Generate smooth, mostly flat terrain with occasional bumps using Perlin-like noise
            Random random = new Random(12345); // Fixed seed for consistent world

            for (int localX = 0; localX < SimulationConfig.ChunkSize; localX++)
            {
                for (int localY = 0; localY < SimulationConfig.ChunkSize; localY++)
                {
                    // Calculate world position for this column
                    float worldX = SimulationConfig.WorldMinX + (chunkX * SimulationConfig.ChunkSize + localX) * SimulationConfig.BlockSize;
                    float worldY = SimulationConfig.WorldMinY + (chunkY * SimulationConfig.ChunkSize + localY) * SimulationConfig.BlockSize;

                    // Use simple noise function for smooth, occasional bumps
                    // Scale down coordinates for smoother transitions
                    float noiseX = worldX / 400f;
                    float noiseY = worldY / 400f;

                    // Generate smooth height variation (mostly flat, occasional bumps)
                    float noise = SimplexNoise(noiseX, noiseY);

                    // Base height is 2-3 blocks (mostly flat)
                    // Add occasional bumps (0-2 additional blocks based on noise)
                    int baseHeight = 2; // Flat base
                    int bumpHeight = noise > 0.3f ? (int)((noise - 0.3f) * 3f) : 0; // Only positive bumps

                    int height = baseHeight + bumpHeight;
                    height = Math.Clamp(height, 2, 5); // Keep terrain low and flat (max 5 blocks high)

                    // Fill blocks from bottom to height
                    for (int localZ = 0; localZ < height; localZ++)
                    {
                        chunk.SetBlock(localX, localY, localZ, true);
                    }

                    // Air above
                    for (int localZ = height; localZ < SimulationConfig.ChunkHeight; localZ++)
                    {
                        chunk.SetBlock(localX, localY, localZ, false);
                    }
                }
            }
        }

        /// <summary>
        /// Simple noise function for smooth terrain generation
        /// Returns value between -1 and 1
        /// </summary>
        private float SimplexNoise(float x, float y)
        {
            // Simple implementation of 2D noise
            // This creates smooth, continuous variation
            float n = MathF.Sin(x * 1.2f + y * 0.7f) * 0.5f +
                      MathF.Sin(x * 2.4f - y * 1.3f) * 0.25f +
                      MathF.Sin(x * 0.8f + y * 2.1f) * 0.15f;

            return n / 0.9f; // Normalize to roughly -1 to 1
        }

        private void CreateHoles()
        {
            // Define hole positions in world coordinates
            var holePositions = new List<Vector2>
            {
                new(200, 200),
                new(-300, 150),
                new(400, -200),
                new(-200, -300),
                new(0, 400),
                new(-400, 0),
                new(250, -400),
                new(-150, 350)
            };

            float holeRadius = 50f;

            foreach (var holePos in holePositions)
            {
                RemoveBlocksInRadius(holePos, holeRadius);
                Log.Information("Created hole at ({HolePosX:F1}, {HolePosY:F1}) with radius {HoleRadius:F1}", holePos.X, holePos.Y, holeRadius);
            }
        }

        private void RemoveBlocksInRadius(Vector2 center, float radius)
        {
            // Iterate through all chunks and blocks
            foreach (var chunk in _chunks.Values)
            {
                for (int localX = 0; localX < SimulationConfig.ChunkSize; localX++)
                {
                    for (int localY = 0; localY < SimulationConfig.ChunkSize; localY++)
                    {
                        // Get world position of this block's center
                        float worldX = SimulationConfig.WorldMinX +
                                       (chunk.ChunkX * SimulationConfig.ChunkSize + localX + 0.5f) * SimulationConfig.BlockSize;
                        float worldY = SimulationConfig.WorldMinY +
                                       (chunk.ChunkY * SimulationConfig.ChunkSize + localY + 0.5f) * SimulationConfig.BlockSize;

                        // Check if block is within hole radius
                        float dx = worldX - center.X;
                        float dy = worldY - center.Y;
                        float distSq = dx * dx + dy * dy;

                        if (distSq < radius * radius)
                        {
                            // Remove all blocks in this column to create a deep hole
                            for (int localZ = 0; localZ < SimulationConfig.ChunkHeight; localZ++)
                            {
                                chunk.SetBlock(localX, localY, localZ, false);
                            }
                        }
                    }
                }
            }
        }

        private void CreateWall()
        {
            // Wall parameters: 10 blocks wide (X), 1 block deep (Y), 5 blocks tall (Z)
            // Position at center-top of map (Y = max)
            float wallCenterX = 0f; // Center of world X
            float wallCenterY = SimulationConfig.WorldMaxY - 50f; // Near top of map
            int wallWidth = 10;
            int wallDepth = 1;
            int wallHeight = 5;

            for (int wx = 0; wx < wallWidth; wx++)
            {
                for (int wy = 0; wy < wallDepth; wy++)
                {
                    for (int wz = 0; wz < wallHeight; wz++)
                    {
                        // Calculate world position for this wall block
                        float worldX = wallCenterX - (wallWidth / 2f * SimulationConfig.BlockSize) + (wx + 0.5f) * SimulationConfig.BlockSize;
                        float worldY = wallCenterY + (wy + 0.5f) * SimulationConfig.BlockSize;
                        float worldZ = wz * SimulationConfig.BlockSize;

                        // Convert to block coordinates
                        int blockX = (int)Math.Floor((worldX - SimulationConfig.WorldMinX) / SimulationConfig.BlockSize);
                        int blockY = (int)Math.Floor((worldY - SimulationConfig.WorldMinY) / SimulationConfig.BlockSize);
                        int blockZ = (int)Math.Floor(worldZ / SimulationConfig.BlockSize);

                        // Convert to chunk coordinates
                        int chunkX = blockX / SimulationConfig.ChunkSize;
                        int chunkY = blockY / SimulationConfig.ChunkSize;
                        int chunkZ = blockZ / SimulationConfig.ChunkHeight;
                        int localX = blockX % SimulationConfig.ChunkSize;
                        int localY = blockY % SimulationConfig.ChunkSize;
                        int localZ = blockZ % SimulationConfig.ChunkHeight;

                        // Get chunk and place block
                        var chunk = GetChunk(chunkX, chunkY, chunkZ);
                        if (chunk != null)
                        {
                            chunk.SetBlock(localX, localY, localZ, true);
                        }
                    }
                }
            }

            Log.Information("Created wall at ({WallCenterX:F1}, {WallCenterY:F1}): {WallWidth}x{WallDepth}x{WallHeight} blocks", wallCenterX, wallCenterY, wallWidth, wallDepth, wallHeight);
        }

        private void CreateFences()
        {
            // Create fence examples for each type
            // Each fence is a path that demonstrates the placement rules

            // Wood fence - straight line in the middle-left area
            CreateFencePath(VoxelType.WoodFence, new List<Vector2>
            {
                new Vector2(-400, 0),
                new Vector2(-300, 0),
                new Vector2(-200, 0),
                new Vector2(-100, 0)
            });

            // Hedge fence - L-shape in the top-left area
            CreateFencePath(VoxelType.HedgeFence, new List<Vector2>
            {
                new Vector2(-400, 400),
                new Vector2(-300, 400),
                new Vector2(-200, 400),
                new Vector2(-200, 300),
                new Vector2(-200, 200)
            });

            // Stone fence - U-shape in the bottom-right area
            CreateFencePath(VoxelType.StoneFence, new List<Vector2>
            {
                new Vector2(200, -400),
                new Vector2(300, -400),
                new Vector2(400, -400),
                new Vector2(400, -300),
                new Vector2(400, -200),
                new Vector2(300, -200),
                new Vector2(200, -200)
            });

            // Iron fence - zigzag in the right area
            CreateFencePath(VoxelType.IronFence, new List<Vector2>
            {
                new Vector2(200, 200),
                new Vector2(300, 200),
                new Vector2(300, 300),
                new Vector2(400, 300),
                new Vector2(400, 400)
            });

            Log.Information("Created fence examples (Wood, Hedge, Stone, Iron)");
        }

        private void CreateLadder()
        {
            // Create a ladder on the wall structure
            // Wall is at center X (0), near top of map Y
            float wallCenterX = 0f;
            float wallCenterY = SimulationConfig.WorldMaxY - 50f;
            int wallHeight = 5;

            // Place ladder in front of the wall (offset by one block in Y direction)
            float ladderWorldX = wallCenterX;
            float ladderWorldY = wallCenterY - SimulationConfig.BlockSize; // One block in front of wall

            // Convert to block coordinates
            int blockX = (int)Math.Floor((ladderWorldX - SimulationConfig.WorldMinX) / SimulationConfig.BlockSize);
            int blockY = (int)Math.Floor((ladderWorldY - SimulationConfig.WorldMinY) / SimulationConfig.BlockSize);

            // Convert to chunk coordinates
            int chunkX = blockX / SimulationConfig.ChunkSize;
            int chunkY = blockY / SimulationConfig.ChunkSize;
            int localX = blockX % SimulationConfig.ChunkSize;
            int localY = blockY % SimulationConfig.ChunkSize;

            var chunk = GetChunk(chunkX, chunkY, 0);
            if (chunk == null)
            {
                Log.Warning("Could not create ladder: chunk not found");
                return;
            }

            // Place ladder blocks from ground to top of wall
            int laddersPlaced = 0;
            for (int z = 0; z < wallHeight; z++)
            {
                if (z < SimulationConfig.ChunkHeight)
                {
                    chunk.SetBlockType(localX, localY, z, VoxelType.Ladder);
                    laddersPlaced++;
                }
            }

            Log.Information("Created ladder at ({LadderWorldX:F1}, {LadderWorldY:F1}), height: {LaddersPlaced} blocks (wall height: {WallHeight})", ladderWorldX, ladderWorldY, laddersPlaced, wallHeight);
        }

        private void CreateFencePath(VoxelType fenceType, List<Vector2> pathPoints)
        {
            foreach (var point in pathPoints)
            {
                PlaceFenceBlock(point.X, point.Y, fenceType);
            }
        }

        private void PlaceFenceBlock(float worldX, float worldY, VoxelType fenceType)
        {
            // Place fence at ground level (find the terrain height first)
            // Convert to block coordinates
            int blockX = (int)Math.Floor((worldX - SimulationConfig.WorldMinX) / SimulationConfig.BlockSize);
            int blockY = (int)Math.Floor((worldY - SimulationConfig.WorldMinY) / SimulationConfig.BlockSize);

            // Convert to chunk coordinates
            int chunkX = blockX / SimulationConfig.ChunkSize;
            int chunkY = blockY / SimulationConfig.ChunkSize;
            int localX = blockX % SimulationConfig.ChunkSize;
            int localY = blockY % SimulationConfig.ChunkSize;

            var chunk = GetChunk(chunkX, chunkY, 0);
            if (chunk == null)
                return;

            // Find the top solid block (terrain height)
            int terrainHeight = 0;
            for (int z = SimulationConfig.ChunkHeight - 1; z >= 0; z--)
            {
                if (chunk.GetBlock(localX, localY, z))
                {
                    terrainHeight = z + 1; // Place fence on top of terrain
                    break;
                }
            }

            // Place fence at terrain height (if within bounds)
            if (terrainHeight < SimulationConfig.ChunkHeight)
            {
                chunk.SetBlockType(localX, localY, terrainHeight, fenceType);
            }
        }

        public IEnumerable<VoxelChunk> GetAllChunks()
        {
            return _chunks.Values;
        }

        public VoxelChunk? GetChunk(int chunkX, int chunkY, int chunkZ = 0)
        {
            return _chunks.TryGetValue((chunkX, chunkY, chunkZ), out var chunk) ? chunk : null;
        }

        /// <summary>
        /// Check if a world position has a block
        /// </summary>
        public bool HasBlockAtPosition(float worldX, float worldY, float worldZ = 0)
        {
            // Convert world position to chunk and local coordinates
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
            // Convert world position to chunk and local coordinates
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

            var chunk = GetChunk(chunkX, chunkY, 0);
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