using System;
using System.Collections.Generic;
using System.Numerics;
using AGH.Shared.Items;
using LiteNetLib.Utils;


namespace AGH.Shared
{
    // ============================================================================
    // CONNECTION PACKETS
    // ============================================================================

    /// <summary>
    /// Sent from client to server when joining the game.
    /// </summary>
    public class JoinRequestPacket : INetSerializable
    {
        public string PlayerName { get; set; } = string.Empty;

        public void Serialize(NetDataWriter writer)
        {
            writer.PutLargeString(PlayerName);
        }

        public void Deserialize(NetDataReader reader)
        {
            PlayerName = reader.GetLargeString();
        }
    }

    /// <summary>
    /// Sent from server to client in response to join request.
    /// Provides player ID, spawn position, and current server tick for synchronization.
    /// </summary>
    public class JoinResponsePacket : INetSerializable
    {
        public Guid PlayerId { get; set; }
        public Vector3 SpawnPosition { get; set; }
        public uint ServerTick { get; set; }

        public void Serialize(NetDataWriter writer)
        {
            writer.PutBytesWithLength(PlayerId.ToByteArray());
            writer.Put(SpawnPosition.X);
            writer.Put(SpawnPosition.Y);
            writer.Put(SpawnPosition.Z);
            writer.Put(ServerTick);
        }

        public void Deserialize(NetDataReader reader)
        {
            PlayerId = new Guid(reader.GetBytesWithLength());
            SpawnPosition = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            ServerTick = reader.GetUInt();
        }
    }

    // ============================================================================
    // INPUT PACKETS
    // ============================================================================

    /// <summary>
    /// Client input command sent to server every tick.
    /// Contains movement direction, rotation, and action states with tick stamp.
    /// Server processes these strictly in tick order.
    /// </summary>
    public class InputCommand : INetSerializable
    {
        public uint Tick { get; set; }
        public Vector3 MoveDirection { get; set; }
        public float Rotation { get; set; }
        public bool Fire { get; set; } // Immediate fire action
        public bool Jump { get; set; } // Jump input (server validates if grounded)
        public bool IsDashing { get; set; } // Dash input
        public bool IsCrouching { get; set; } // Crouch input

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(Tick);
            writer.Put(MoveDirection.X);
            writer.Put(MoveDirection.Y);
            writer.Put(MoveDirection.Z);
            writer.Put(Rotation);
            writer.Put(Fire);
            writer.Put(Jump);
            writer.Put(IsDashing);
            writer.Put(IsCrouching);
        }

        public void Deserialize(NetDataReader reader)
        {
            Tick = reader.GetUInt();
            MoveDirection = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            Rotation = reader.GetFloat();
            Fire = reader.GetBool();
            Jump = reader.GetBool();
            IsDashing = reader.GetBool();
            IsCrouching = reader.GetBool();
        }
    }

    // ============================================================================
    // SNAPSHOT PACKETS
    // ============================================================================

    /// <summary>
    /// Authoritative world state snapshot from server.
    /// Contains all players, projectiles, and the last processed input tick for reconciliation.
    /// Sent at 30Hz (every 2nd server tick).
    /// </summary>
    public class WorldSnapshot : INetSerializable
    {
        public uint Tick { get; set; }
        public uint LastProcessedInputTick { get; set; }
        public PlayerState[] Players { get; set; } = Array.Empty<PlayerState>();
        public ProjectileState[] Projectiles { get; set; } = Array.Empty<ProjectileState>();
        public BoxState[] Boxes { get; set; } = Array.Empty<BoxState>();

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(Tick);
            writer.Put(LastProcessedInputTick);

            // Players
            writer.Put((ushort)Players.Length);
            foreach (var player in Players)
                player.Serialize(writer);

            // Projectiles
            writer.Put((ushort)Projectiles.Length);
            foreach (var projectile in Projectiles)
                projectile.Serialize(writer);

            // Boxes
            writer.Put((ushort)Boxes.Length);
            foreach (var box in Boxes)
                box.Serialize(writer);
        }

        public void Deserialize(NetDataReader reader)
        {
            Tick = reader.GetUInt();
            LastProcessedInputTick = reader.GetUInt();

            // Players
            ushort playerCount = reader.GetUShort();
            Players = new PlayerState[playerCount];
            for (int i = 0; i < playerCount; i++)
            {
                Players[i] = new PlayerState();
                Players[i].Deserialize(reader);
            }

            // Projectiles
            ushort projectileCount = reader.GetUShort();
            Projectiles = new ProjectileState[projectileCount];
            for (int i = 0; i < projectileCount; i++)
            {
                Projectiles[i] = new ProjectileState();
                Projectiles[i].Deserialize(reader);
            }

            // Boxes
            ushort boxCount = reader.GetUShort();
            Boxes = new BoxState[boxCount];
            for (int i = 0; i < boxCount; i++)
            {
                Boxes[i] = new BoxState();
                Boxes[i].Deserialize(reader);
            }
        }
    }

    /// <summary>
    /// Authoritative player state from server.
    /// Used for both reconciliation (local player) and rendering (remote players).
    /// </summary>
    public class PlayerState : INetSerializable
    {
        public Guid Id { get; set; }
        public Vector3 Position { get; set; }
        public Vector3 Velocity { get; set; }
        public float Rotation { get; set; }
        public string Name { get; set; } = string.Empty;
        public byte[] HealthData { get; set; } = Array.Empty<byte>();

        public void Serialize(NetDataWriter writer)
        {
            writer.PutBytesWithLength(Id.ToByteArray());
            writer.Put(Position.X);
            writer.Put(Position.Y);
            writer.Put(Position.Z);
            writer.Put(Velocity.X);
            writer.Put(Velocity.Y);
            writer.Put(Velocity.Z);
            writer.Put(Rotation);
            writer.PutLargeString(Name);
            writer.PutBytesWithLength(HealthData);
        }

        public void Deserialize(NetDataReader reader)
        {
            Id = new Guid(reader.GetBytesWithLength());
            Position = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            Velocity = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            Rotation = reader.GetFloat();
            Name = reader.GetLargeString();
            HealthData = reader.GetBytesWithLength();
        }
    }

    /// <summary>
    /// Authoritative projectile state from server.
    /// Server is sole authority for spawning, movement, and hit detection.
    /// Optimized for snapshot bandwidth - only position and velocity sent.
    /// </summary>
    public class ProjectileState : INetSerializable
    {
        public uint Id { get; set; } // Server-assigned unique ID
        public Vector3 Position { get; set; }
        public Vector3 Velocity { get; set; }
        public Guid OwnerId { get; set; }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(Id);
            writer.Put(Position.X);
            writer.Put(Position.Y);
            writer.Put(Position.Z);
            writer.Put(Velocity.X);
            writer.Put(Velocity.Y);
            writer.Put(Velocity.Z);
            writer.PutBytesWithLength(OwnerId.ToByteArray());
        }

        public void Deserialize(NetDataReader reader)
        {
            Id = reader.GetUInt();
            Position = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            Velocity = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            OwnerId = new Guid(reader.GetBytesWithLength());
        }
    }

    // ============================================================================
    // TEXT MESSAGE PACKETS
    // ============================================================================

    /// <summary>
    /// Static player information sent separately from snapshots.
    /// Contains data that doesn't change frequently (name, max health).
    /// Sent reliably when a player joins or when data changes.
    /// </summary>
    public class PlayerInfoPacket : INetSerializable
    {
        public Guid PlayerId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int MaxHealth { get; set; }

        public void Serialize(NetDataWriter writer)
        {
            writer.PutBytesWithLength(PlayerId.ToByteArray());
            writer.PutLargeString(Name);
            writer.Put((byte)MaxHealth);
        }

        public void Deserialize(NetDataReader reader)
        {
            PlayerId = new Guid(reader.GetBytesWithLength());
            Name = reader.GetLargeString();
            MaxHealth = reader.GetByte();
        }
    }

    /// <summary>
    /// Text message packet sent from server to client.
    /// Used for chat, notifications, and system messages.
    /// </summary>
    public class TextPacket : INetSerializable
    {
        public string Message { get; set; } = string.Empty;
        public TextMessageType MessageType { get; set; } = TextMessageType.System;

        public void Serialize(NetDataWriter writer)
        {
            writer.PutLargeString(Message);
            writer.Put((byte)MessageType);
        }

        public void Deserialize(NetDataReader reader)
        {
            Message = reader.GetLargeString();
            MessageType = (TextMessageType)reader.GetByte();
        }
    }

    public enum TextMessageType : byte
    {
        System = 0,
        Chat = 1,
        Warning = 2,
        Error = 3,
        Info = 4
    }

    /// <summary>
    /// Packet sent from client to server containing a chat message.
    /// </summary>
    public class ChatMessagePacket : INetSerializable
    {
        public string Message { get; set; } = string.Empty;

        public void Serialize(NetDataWriter writer)
        {
            writer.PutLargeString(Message);
        }

        public void Deserialize(NetDataReader reader)
        {
            Message = reader.GetLargeString();
        }
    }

    // Voice packet types (VoiceDataPacket, VoiceDataFromPacket, VoiceDataToPeerPacket) are in AGH.Voice.Shared.

    // ============================================================================
    // PING PACKETS (unchanged - used for latency measurement)
    // ============================================================================

    /// <summary>
    /// State for a box entity.
    /// </summary>
    public class BoxState : INetSerializable
    {
        public uint Id { get; set; }
        public Vector3 Position { get; set; }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(Id);
            writer.Put(Position.X);
            writer.Put(Position.Y);
            writer.Put(Position.Z);
        }

        public void Deserialize(NetDataReader reader)
        {
            Id = reader.GetUInt();
            Position = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
        }
    }

    public class PingPacket : INetSerializable
    {
        public long Timestamp { get; set; }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(Timestamp);
        }

        public void Deserialize(NetDataReader reader)
        {
            Timestamp = reader.GetLong();
        }
    }

    public class PongPacket : INetSerializable
    {
        public long Timestamp { get; set; }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(Timestamp);
        }

        public void Deserialize(NetDataReader reader)
        {
            Timestamp = reader.GetLong();
        }
    }

    // ============================================================================
    // CHUNK PACKETS
    // ============================================================================

    /// <summary>
    /// Sent from server to client to create/initialize a chunk.
    /// Contains all block data for the chunk.
    /// </summary>
    public class ChunkCreatePacket : INetSerializable
    {
        public int ChunkX { get; set; }
        public int ChunkY { get; set; }
        public int ChunkZ { get; set; }
        public bool[] Blocks { get; set; } = Array.Empty<bool>();
        public byte[] BlockTypes { get; set; } = Array.Empty<byte>();
        public byte[] BlockHealth { get; set; } = Array.Empty<byte>();
        public byte[] BlockData { get; set; } = Array.Empty<byte>();

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(ChunkX);
            writer.Put(ChunkY);
            writer.Put(ChunkZ);

            // Calculate expected block count
            int blockCount = SimulationConfig.ChunkSize * SimulationConfig.ChunkSize * SimulationConfig.ChunkHeight;
            writer.Put((ushort)blockCount);

            // Serialize BlockTypes (one byte per block)
            if (BlockTypes.Length != blockCount)
            {
                BlockTypes = new byte[blockCount];
            }

            writer.PutBytesWithLength(BlockTypes);

            // Serialize BlockHealth (one byte per block)
            if (BlockHealth.Length != blockCount)
            {
                BlockHealth = new byte[blockCount];
            }

            writer.PutBytesWithLength(BlockHealth);

            // Serialize BlockData (one byte per block)
            if (BlockData.Length != blockCount)
            {
                BlockData = new byte[blockCount];
            }

            writer.PutBytesWithLength(BlockData);
        }

        public void Deserialize(NetDataReader reader)
        {
            ChunkX = reader.GetInt();
            ChunkY = reader.GetInt();
            ChunkZ = reader.GetInt();

            int blockCount = reader.GetUShort();

            // Deserialize BlockTypes
            BlockTypes = reader.GetBytesWithLength();
            if (BlockTypes.Length != blockCount)
            {
                Console.WriteLine("[ChunkCreatePacket] BlockTypes length mismatch: expected {Expected}, got {Actual}", blockCount, BlockTypes.Length);
                BlockTypes = new byte[blockCount];
            }

            // Deserialize BlockHealth
            BlockHealth = reader.GetBytesWithLength();
            if (BlockHealth.Length != blockCount)
            {
                Console.WriteLine("[ChunkCreatePacket] BlockHealth length mismatch: expected {Expected}, got {Actual}", blockCount, BlockHealth.Length);
                BlockHealth = new byte[blockCount];
            }

            // Deserialize BlockData
            BlockData = reader.GetBytesWithLength();
            if (BlockData.Length != blockCount)
            {
                Console.WriteLine("[ChunkCreatePacket] BlockData length mismatch: expected {Expected}, got {Actual}", blockCount, BlockData.Length);
                BlockData = new byte[blockCount];
            }
        }
    }

    /// <summary>
    /// Sent from server to client to update specific blocks in a chunk.
    /// Used when blocks are added/removed dynamically.
    /// </summary>
    public class ChunkUpdatePacket : INetSerializable
    {
        public int ChunkX { get; set; }
        public int ChunkY { get; set; }
        public int ChunkZ { get; set; }
        public BlockUpdate[] Updates { get; set; } = Array.Empty<BlockUpdate>();

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(ChunkX);
            writer.Put(ChunkY);
            writer.Put(ChunkZ);
            writer.Put((ushort)Updates.Length);

            foreach (var update in Updates)
            {
                Console.WriteLine("[SERIALIZE] ChunkUpdate: LocalX={X}, LocalY={Y}, LocalZ={Z}, Exists={Exists}, BlockType={BlockType}, Health={Health}",
                    update.LocalX, update.LocalY, update.LocalZ, update.Exists, update.BlockType, update.Health);

                writer.Put(update.LocalX);
                writer.Put(update.LocalY);
                writer.Put(update.LocalZ);
                writer.Put(update.Exists); // Now byte, not bool
                writer.Put(update.BlockType);
                writer.Put(update.Health);
                writer.Put(update.Data);
            }
        }

        public void Deserialize(NetDataReader reader)
        {
            ChunkX = reader.GetInt();
            ChunkY = reader.GetInt();
            ChunkZ = reader.GetInt();

            ushort count = reader.GetUShort();
            Updates = new BlockUpdate[count];

            for (int i = 0; i < count; i++)
            {
                byte localX = reader.GetByte();
                byte localY = reader.GetByte();
                byte localZ = reader.GetByte();
                byte exists = reader.GetByte(); // Now byte, not bool
                byte blockType = reader.GetByte();
                byte health = reader.GetByte();
                byte data = reader.GetByte();

                Console.WriteLine("[DESERIALIZE] ChunkUpdate #{Index}: LocalX={X}, LocalY={Y}, LocalZ={Z}, Exists={Exists}, BlockType={BlockType}, Health={Health}, Data={Data}",
                    i, localX, localY, localZ, exists, blockType, health, data);

                Updates[i] = new BlockUpdate
                {
                    LocalX = localX,
                    LocalY = localY,
                    LocalZ = localZ,
                    Exists = exists,
                    BlockType = blockType,
                    Health = health,
                    Data = data
                };
            }
        }
    }

    public struct BlockUpdate
    {
        public byte LocalX;
        public byte LocalY;
        public byte LocalZ;
        public byte Exists; // Changed from bool to byte (0 or 1) to fix alignment
        public byte BlockType;
        public byte Health;
        public byte Data;
    }

    // ============================================================================
    // VOXEL PAINTING PACKETS
    // ============================================================================

    /// <summary>
    /// Client request to modify a voxel (place or remove)
    /// </summary>
    public class VoxelPaintRequestPacket : INetSerializable
    {
        public Vector3 WorldPosition { get; set; }
        public VoxelType VoxelType { get; set; }
        public bool IsRemoving { get; set; } // true = delete, false = place
        public int Rotation { get; set; } // For ramps (0-3 for 4 directions)

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(WorldPosition.X);
            writer.Put(WorldPosition.Y);
            writer.Put(WorldPosition.Z);
            writer.Put((byte)VoxelType);
            writer.Put(IsRemoving);
            writer.Put(Rotation);
        }

        public void Deserialize(NetDataReader reader)
        {
            WorldPosition = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            VoxelType = (VoxelType)reader.GetByte();
            IsRemoving = reader.GetBool();
            Rotation = reader.GetInt();
        }
    }

    /// <summary>
    /// All types of voxels/blocks that can exist in the world
    /// </summary>
    public enum VoxelType : byte
    {
        Air = 0,
        Cube = 1, // Solid block (renamed from Solid)
        Ramp = 2,
        Water = 3,
        Ladder = 4,
        WoodFence = 5,
        HedgeFence = 6,
        StoneFence = 7,
        IronFence = 8
    }

    // ============================================================================
    // STATUS EFFECT PACKETS
    // ============================================================================

    /// <summary>
    /// Sent from server to client when a player's status effects change.
    /// Server is authoritative for status effects.
    /// </summary>
    public class StatEffectChanged : INetSerializable
    {
        public Guid PlayerId { get; set; }

        // Use private field to prevent LiteNetLib auto-serialization

        private List<StatEffectType> _activeEffects = new List<StatEffectType>();

        // Public accessor (not an auto-property to avoid auto-serialization)

        public List<StatEffectType> GetActiveEffects() => _activeEffects;
        public void SetActiveEffects(List<StatEffectType> effects) => _activeEffects = effects;

        public void Serialize(NetDataWriter writer)
        {
            writer.PutBytesWithLength(PlayerId.ToByteArray());
            writer.Put((byte)_activeEffects.Count);
            foreach (var effect in _activeEffects)
            {
                writer.Put((byte)effect);
            }
        }

        public void Deserialize(NetDataReader reader)
        {
            PlayerId = new Guid(reader.GetBytesWithLength());
            byte count = reader.GetByte();
            _activeEffects = new List<StatEffectType>(count);
            for (int i = 0; i < count; i++)
            {
                _activeEffects.Add((StatEffectType)reader.GetByte());
            }
        }
    }

    // ============================================================================
    // INVENTORY PACKETS
    // ============================================================================

    /// <summary>
    /// Client action to use the currently active item.
    /// </summary>
    public class ItemUseAction : INetSerializable
    {
        public uint Tick { get; set; }
        public int SlotIndex { get; set; }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(Tick);
            writer.Put(SlotIndex);
        }

        public void Deserialize(NetDataReader reader)
        {
            Tick = reader.GetUInt();
            SlotIndex = reader.GetInt();
        }
    }

    /// <summary>
    /// Server event when an item was successfully used.
    /// </summary>
    public class ItemUsedEvent : INetSerializable
    {
        public Guid PlayerId { get; set; }
        public ItemType ItemType { get; set; }
        public uint ServerTick { get; set; }

        public void Serialize(NetDataWriter writer)
        {
            writer.PutBytesWithLength(PlayerId.ToByteArray());
            writer.Put((byte)ItemType);
            writer.Put(ServerTick);
        }

        public void Deserialize(NetDataReader reader)
        {
            PlayerId = new Guid(reader.GetBytesWithLength());
            ItemType = (ItemType)reader.GetByte();
            ServerTick = reader.GetUInt();
        }
    }

    /// <summary>
    /// Client action to switch inventory slot.
    /// </summary>
    public class InventorySlotSwitchedAction : INetSerializable
    {
        public int SlotIndex { get; set; }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(SlotIndex);
        }

        public void Deserialize(NetDataReader reader)
        {
            SlotIndex = reader.GetInt();
        }
    }

    /// <summary>
    /// Server event when a player switched inventory slot.
    /// </summary>
    public class InventorySlotSwitchedEvent : INetSerializable
    {
        public Guid PlayerId { get; set; }
        public int SlotIndex { get; set; }

        public void Serialize(NetDataWriter writer)
        {
            writer.PutBytesWithLength(PlayerId.ToByteArray());
            writer.Put(SlotIndex);
        }

        public void Deserialize(NetDataReader reader)
        {
            PlayerId = new Guid(reader.GetBytesWithLength());
            SlotIndex = reader.GetInt();
        }
    }

    /// <summary>
    /// Full inventory sync sent on player join.
    /// </summary>
    public class InventoryFullSyncPacket : INetSerializable
    {
        public Guid PlayerId { get; set; }

        // Use private field to prevent LiteNetLib auto-serialization

        private InventoryComponent? _inventory;

        // Public accessor (not a property to avoid auto-serialization)

        public InventoryComponent? GetInventory() => _inventory;
        public void SetInventory(InventoryComponent? inventory) => _inventory = inventory;

        public void Serialize(NetDataWriter writer)
        {
            writer.PutBytesWithLength(PlayerId.ToByteArray());
            if (_inventory != null)
            {
                var data = InventoryComponent.Serialize(_inventory);
                Console.WriteLine("Serializing inventory: {Bytes} bytes, {Slots} slots", data.Length, _inventory.Slots?.Length ?? 0);
                writer.PutBytesWithLength(data);
            }
            else
            {
                Console.WriteLine("Serializing NULL inventory!");
                writer.PutBytesWithLength(Array.Empty<byte>());
            }
        }

        public void Deserialize(NetDataReader reader)
        {
            PlayerId = new Guid(reader.GetBytesWithLength());
            var data = reader.GetBytesWithLength();
            Console.WriteLine("Received {Bytes} bytes to deserialize", data.Length);
            if (data.Length > 0)
            {
                try
                {
                    _inventory = InventoryComponent.Deserialize(data);
                    if (_inventory == null)
                    {
                        Console.WriteLine("InventoryComponent.Deserialize returned NULL for {Bytes} bytes!", data.Length);
                    }
                    else
                    {
                        Console.WriteLine("SUCCESS: Deserialized inventory with {Slots} slots, ActiveSlot={ActiveSlot}", _inventory.Slots?.Length ?? 0, _inventory.ActiveSlotIndex);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                    _inventory = null;
                }
            }
            else
            {
                Console.WriteLine("Received empty inventory data (0 bytes)");
            }
        }
    }
}