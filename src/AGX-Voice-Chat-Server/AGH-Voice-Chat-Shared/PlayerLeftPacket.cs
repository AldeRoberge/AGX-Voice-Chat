using System;
using LiteNetLib.Utils;

namespace AGH_Voice_Chat_Shared;

/// <summary>Server → client: a player disconnected; remove them from the local list.</summary>
public class PlayerLeftPacket : INetSerializable
{
    public Guid PlayerId { get; set; }

    public void Serialize(NetDataWriter writer)
    {
        writer.PutBytesWithLength(PlayerId.ToByteArray());
    }

    public void Deserialize(NetDataReader reader)
    {
        PlayerId = new Guid(reader.GetBytesWithLength());
    }
}
