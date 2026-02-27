using System;
using System.Numerics;
using LiteNetLib.Utils;

namespace AGH_Voice_Chat_Shared.Packets.Join;

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