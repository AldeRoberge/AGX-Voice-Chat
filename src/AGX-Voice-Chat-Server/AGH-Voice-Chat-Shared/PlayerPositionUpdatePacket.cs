using System;
using System.Numerics;
using LiteNetLib.Utils;

namespace AGH_Voice_Chat_Shared;

/// <summary>Server → client: relay of a player's position (small packet, no full snapshot).</summary>
public class PlayerPositionUpdatePacket : INetSerializable
{
    public Guid PlayerId { get; set; }
    public Vector3 Position { get; set; }

    public void Serialize(NetDataWriter writer)
    {
        writer.PutBytesWithLength(PlayerId.ToByteArray());
        writer.Put(Position.X);
        writer.Put(Position.Y);
        writer.Put(Position.Z);
    }

    public void Deserialize(NetDataReader reader)
    {
        PlayerId = new Guid(reader.GetBytesWithLength());
        Position = new Vector3(
            reader.GetFloat(),
            reader.GetFloat(),
            reader.GetFloat()
        );
    }
}
