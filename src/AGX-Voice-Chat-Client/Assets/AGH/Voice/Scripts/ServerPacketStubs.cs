using System;
using System.Collections.Generic;
using LiteNetLib.Utils;
using UnityEngine;

// Stub packets so the voice client can read and discard server packets.
// These MUST match the server's AGH.Shared packet definitions (namespace, class name, serialization format)
// so that LiteNetLib NetPacketProcessor type hashes and wire format are identical.

namespace AGH.Shared
{
    public enum TextMessageType : byte
    {
        System = 0,
        Chat = 1,
        Warning = 2,
        Error = 3,
        Info = 4
    }

    public enum VoxelType : byte
    {
        Air = 0,
        Cube = 1,
        Ramp = 2,
        Water = 3,
        Ladder = 4,
        WoodFence = 5,
        HedgeFence = 6,
        StoneFence = 7,
        IronFence = 8
    }

    public enum StatEffectType : byte
    {
        None = 0,
        Swimming = 1,
        Climbing = 2,
        Burning = 3,
        Poisoned = 4
    }

    // ============================================================================
    // POSITION PACKET (client sends position to server)
    // ============================================================================

    public class PlayerPositionPacket : INetSerializable
    {
        public Vector3 Position { get; set; }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(Position.x);
            writer.Put(Position.y);
            writer.Put(Position.z);
        }

        public void Deserialize(NetDataReader reader)
        {
            Position = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
        }
    }

    // ============================================================================
    // SNAPSHOT PACKETS (server -> client, 30Hz)
    // ============================================================================

    public class WorldSnapshot : INetSerializable
    {
        public uint Tick { get; set; }
        public PlayerState[] Players { get; set; } = Array.Empty<PlayerState>();
        public ProjectileState[] Projectiles { get; set; } = Array.Empty<ProjectileState>();
        public BoxState[] Boxes { get; set; } = Array.Empty<BoxState>();

        public void Serialize(NetDataWriter writer)
        {
        }

        public void Deserialize(NetDataReader reader)
        {
            Tick = reader.GetUInt();

            ushort playerCount = reader.GetUShort();
            Players = new PlayerState[playerCount];
            for (int i = 0; i < playerCount; i++)
            {
                Players[i] = new PlayerState();
                Players[i].Deserialize(reader);
            }

            ushort projectileCount = reader.GetUShort();
            Projectiles = new ProjectileState[projectileCount];
            for (int i = 0; i < projectileCount; i++)
            {
                Projectiles[i] = new ProjectileState();
                Projectiles[i].Deserialize(reader);
            }

            ushort boxCount = reader.GetUShort();
            Boxes = new BoxState[boxCount];
            for (int i = 0; i < boxCount; i++)
            {
                Boxes[i] = new BoxState();
                Boxes[i].Deserialize(reader);
            }
        }
    }

    public class PlayerState : INetSerializable
    {
        public Guid Id { get; set; }
        public Vector3 Position { get; set; }
        public string Name { get; set; } = string.Empty;

        public void Serialize(NetDataWriter writer)
        {
        }

        public void Deserialize(NetDataReader reader)
        {
            Id = new Guid(reader.GetBytesWithLength());
            Position = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            Name = NetIOHelper.GetLargeString(reader);
        }
    }

    public class ProjectileState : INetSerializable
    {
        public uint Id { get; set; }
        public Vector3 Position { get; set; }
        public Vector3 Velocity { get; set; }
        public Guid OwnerId { get; set; }

        public void Serialize(NetDataWriter writer)
        {
        }

        public void Deserialize(NetDataReader reader)
        {
            Id = reader.GetUInt();
            Position = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            Velocity = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            OwnerId = new Guid(reader.GetBytesWithLength());
        }
    }

    public class BoxState : INetSerializable
    {
        public uint Id { get; set; }
        public Vector3 Position { get; set; }

        public void Serialize(NetDataWriter writer)
        {
        }

        public void Deserialize(NetDataReader reader)
        {
            Id = reader.GetUInt();
            Position = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
        }
    }

    // ============================================================================
    // PING PACKETS
    // ============================================================================

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
    // TEXT / PLAYER INFO PACKETS
    // ============================================================================

    public class PlayerInfoPacket : INetSerializable
    {
        public Guid PlayerId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int MaxHealth { get; set; }

        public void Serialize(NetDataWriter writer)
        {
            writer.PutBytesWithLength(PlayerId.ToByteArray());
            NetIOHelper.PutLargeString(writer, Name);
            writer.Put((byte)MaxHealth);
        }

        public void Deserialize(NetDataReader reader)
        {
            PlayerId = new Guid(reader.GetBytesWithLength());
            Name = NetIOHelper.GetLargeString(reader);
            MaxHealth = reader.GetByte();
        }
    }

    public class TextPacket : INetSerializable
    {
        public string Message { get; set; } = string.Empty;
        public TextMessageType MessageType { get; set; }

        public void Serialize(NetDataWriter writer)
        {
            NetIOHelper.PutLargeString(writer, Message);
            writer.Put((byte)MessageType);
        }

        public void Deserialize(NetDataReader reader)
        {
            Message = NetIOHelper.GetLargeString(reader);
            MessageType = (TextMessageType)reader.GetByte();
        }
    }

    public class ChatMessagePacket : INetSerializable
    {
        public string Message { get; set; } = string.Empty;
        public void Serialize(NetDataWriter writer) => NetIOHelper.PutLargeString(writer, Message);
        public void Deserialize(NetDataReader reader) => Message = NetIOHelper.GetLargeString(reader);
    }

    // ============================================================================
    // CHUNK / VOXEL PACKETS
    // ============================================================================

    public struct BlockUpdate
    {
        public byte LocalX, LocalY, LocalZ, Exists, BlockType, Health, Data;
    }

    public class ChunkCreatePacket : INetSerializable
    {
        public int ChunkX { get; set; }
        public int ChunkY { get; set; }
        public int ChunkZ { get; set; }
        public byte[] BlockTypes { get; set; } = Array.Empty<byte>();
        public byte[] BlockHealth { get; set; } = Array.Empty<byte>();
        public byte[] BlockData { get; set; } = Array.Empty<byte>();

        public void Serialize(NetDataWriter writer)
        {
        }

        public void Deserialize(NetDataReader reader)
        {
            ChunkX = reader.GetInt();
            ChunkY = reader.GetInt();
            ChunkZ = reader.GetInt();
            reader.GetUShort(); // blockCount
            BlockTypes = reader.GetBytesWithLength();
            BlockHealth = reader.GetBytesWithLength();
            BlockData = reader.GetBytesWithLength();
        }
    }

    public class ChunkUpdatePacket : INetSerializable
    {
        public int ChunkX { get; set; }
        public int ChunkY { get; set; }
        public int ChunkZ { get; set; }
        public BlockUpdate[] Updates { get; set; } = Array.Empty<BlockUpdate>();

        public void Serialize(NetDataWriter writer)
        {
        }

        public void Deserialize(NetDataReader reader)
        {
            ChunkX = reader.GetInt();
            ChunkY = reader.GetInt();
            ChunkZ = reader.GetInt();
            var count = reader.GetUShort();
            Updates = new BlockUpdate[count];
            for (int i = 0; i < count; i++)
            {
                Updates[i] = new BlockUpdate
                {
                    LocalX = reader.GetByte(),
                    LocalY = reader.GetByte(),
                    LocalZ = reader.GetByte(),
                    Exists = reader.GetByte(),
                    BlockType = reader.GetByte(),
                    Health = reader.GetByte(),
                    Data = reader.GetByte()
                };
            }
        }
    }

    public class VoxelPaintRequestPacket : INetSerializable
    {
        public Vector3 WorldPosition { get; set; }
        public VoxelType VoxelType { get; set; }
        public bool IsRemoving { get; set; }
        public int Rotation { get; set; }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(WorldPosition.x);
            writer.Put(WorldPosition.y);
            writer.Put(WorldPosition.z);
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

    // ============================================================================
    // STATUS EFFECT PACKETS
    // ============================================================================

    public class StatEffectChanged : INetSerializable
    {
        public Guid PlayerId { get; set; }
        private List<StatEffectType> _activeEffects = new List<StatEffectType>();

        public List<StatEffectType> GetActiveEffects() => _activeEffects;

        public void Serialize(NetDataWriter writer)
        {
            writer.PutBytesWithLength(PlayerId.ToByteArray());
            writer.Put((byte)_activeEffects.Count);
            foreach (var effect in _activeEffects)
                writer.Put((byte)effect);
        }

        public void Deserialize(NetDataReader reader)
        {
            PlayerId = new Guid(reader.GetBytesWithLength());
            byte count = reader.GetByte();
            _activeEffects = new List<StatEffectType>(count);
            for (int i = 0; i < count; i++)
                _activeEffects.Add((StatEffectType)reader.GetByte());
        }
    }

    // ============================================================================
    // INVENTORY PACKETS
    // ============================================================================

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

    public class ItemUsedEvent : INetSerializable
    {
        public Guid PlayerId { get; set; }
        public byte ItemType { get; set; }
        public uint ServerTick { get; set; }

        public void Serialize(NetDataWriter writer)
        {
            writer.PutBytesWithLength(PlayerId.ToByteArray());
            writer.Put(ItemType);
            writer.Put(ServerTick);
        }

        public void Deserialize(NetDataReader reader)
        {
            PlayerId = new Guid(reader.GetBytesWithLength());
            ItemType = reader.GetByte();
            ServerTick = reader.GetUInt();
        }
    }

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

    public class InventoryFullSyncPacket : INetSerializable
    {
        public Guid PlayerId { get; set; }

        public void Serialize(NetDataWriter writer)
        {
        }

        public void Deserialize(NetDataReader reader)
        {
            PlayerId = new Guid(reader.GetBytesWithLength());
            reader.GetBytesWithLength(); // inventory data (discarded by voice client)
        }
    }
}