using System;
using LiteNetLib.Utils;
using UnityEngine;

// Namespace and types must match AGH.Shared so LiteNetLib type hashes match the server.

namespace AGH.Shared
{
    /// <summary>
    /// Sent from client to server when joining the game.
    /// </summary>
    public class JoinRequestPacket : INetSerializable
    {
        public string PlayerName { get; set; } = string.Empty;

        public void Serialize(NetDataWriter writer)
        {
            NetIOHelper.PutLargeString(writer, PlayerName);
        }

        public void Deserialize(NetDataReader reader)
        {
            PlayerName = NetIOHelper.GetLargeString(reader);
        }
    }

    /// <summary>
    /// Sent from server to client in response to join request.
    /// </summary>
    public class JoinResponsePacket : INetSerializable
    {
        public Guid PlayerId { get; set; }
        public Vector3 SpawnPosition { get; set; }
        public uint ServerTick { get; set; }

        public void Serialize(NetDataWriter writer)
        {
            writer.PutBytesWithLength(PlayerId.ToByteArray());
            writer.Put(SpawnPosition.x);
            writer.Put(SpawnPosition.y);
            writer.Put(SpawnPosition.z);
            writer.Put(ServerTick);
        }

        public void Deserialize(NetDataReader reader)
        {
            PlayerId = new Guid(reader.GetBytesWithLength());
            SpawnPosition = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            ServerTick = reader.GetUInt();
        }
    }
}
