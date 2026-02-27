using System;
using System.Numerics;
using LiteNetLib.Utils;

namespace AGH_Voice_Chat_Client.Game
{
    // ============================================================================
    // CONNECTION
    // ============================================================================

    public class JoinRequestPacket : INetSerializable
    {
        public string PlayerName { get; set; } = string.Empty;
        public void Serialize(NetDataWriter writer) => writer.PutLargeString(PlayerName);
        public void Deserialize(NetDataReader reader) => PlayerName = reader.GetLargeString();
    }

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
    // POSITION (client sends; server stores and broadcasts)
    // ============================================================================

    public class PlayerPositionPacket : INetSerializable
    {
        public Vector3 Position { get; set; }
        public void Serialize(NetDataWriter writer)
        {
            writer.Put(Position.X);
            writer.Put(Position.Y);
            writer.Put(Position.Z);
        }
        public void Deserialize(NetDataReader reader)
        {
            Position = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
        }
    }

    // ============================================================================
    // SNAPSHOT (server -> client)
    // ============================================================================

    public class WorldSnapshot : INetSerializable
    {
        public uint Tick { get; set; }
        public PlayerState[] Players { get; set; } = Array.Empty<PlayerState>();

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(Tick);
            writer.Put((ushort)Players.Length);
            foreach (var p in Players)
                p.Serialize(writer);
        }
        public void Deserialize(NetDataReader reader)
        {
            Tick = reader.GetUInt();
            ushort n = reader.GetUShort();
            Players = new PlayerState[n];
            for (int i = 0; i < n; i++)
            {
                Players[i] = new PlayerState();
                Players[i].Deserialize(reader);
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
            writer.PutBytesWithLength(Id.ToByteArray());
            writer.Put(Position.X);
            writer.Put(Position.Y);
            writer.Put(Position.Z);
            writer.PutLargeString(Name);
        }
        public void Deserialize(NetDataReader reader)
        {
            Id = new Guid(reader.GetBytesWithLength());
            Position = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            Name = reader.GetLargeString();
        }
    }

    // ============================================================================
    // PLAYER INFO (name on join)
    // ============================================================================

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

    // ============================================================================
    // PING
    // ============================================================================

    public class PingPacket : INetSerializable
    {
        public long Timestamp { get; set; }
        public void Serialize(NetDataWriter writer) => writer.Put(Timestamp);
        public void Deserialize(NetDataReader reader) => Timestamp = reader.GetLong();
    }

    public class PongPacket : INetSerializable
    {
        public long Timestamp { get; set; }
        public void Serialize(NetDataWriter writer) => writer.Put(Timestamp);
        public void Deserialize(NetDataReader reader) => Timestamp = reader.GetLong();
    }
}
