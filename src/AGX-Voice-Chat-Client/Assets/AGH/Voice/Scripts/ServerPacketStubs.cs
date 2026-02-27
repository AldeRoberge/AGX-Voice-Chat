using System;
using LiteNetLib.Utils;
using UnityEngine;

// Stubs must match server packet wire format (AGH_Voice_Chat_Client.Game.Packets) for type hashes.

namespace AGH.Shared
{
    public class JoinRequestPacket : INetSerializable
    {
        public string PlayerName { get; set; } = string.Empty;
        public void Serialize(NetDataWriter writer) => NetIOHelper.PutLargeString(writer, PlayerName);
        public void Deserialize(NetDataReader reader) => PlayerName = NetIOHelper.GetLargeString(reader);
    }

    public class JoinResponsePacket : INetSerializable
    {
        public Guid PlayerId { get; set; }
        public Vector3 SpawnPosition { get; set; }
        public uint ServerTick { get; set; }
        public void Serialize(NetDataWriter writer) { }
        public void Deserialize(NetDataReader reader)
        {
            PlayerId = new Guid(reader.GetBytesWithLength());
            SpawnPosition = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            ServerTick = reader.GetUInt();
        }
    }

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

    public class WorldSnapshot : INetSerializable
    {
        public uint Tick { get; set; }
        public PlayerState[] Players { get; set; } = Array.Empty<PlayerState>();

        public void Serialize(NetDataWriter writer) { }

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
        public void Serialize(NetDataWriter writer) { }
        public void Deserialize(NetDataReader reader)
        {
            Id = new Guid(reader.GetBytesWithLength());
            Position = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            Name = NetIOHelper.GetLargeString(reader);
        }
    }

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

    public class PlayerInfoPacket : INetSerializable
    {
        public Guid PlayerId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int MaxHealth { get; set; }
        public void Serialize(NetDataWriter writer) { }
        public void Deserialize(NetDataReader reader)
        {
            PlayerId = new Guid(reader.GetBytesWithLength());
            Name = NetIOHelper.GetLargeString(reader);
            MaxHealth = reader.GetByte();
        }
    }
}
