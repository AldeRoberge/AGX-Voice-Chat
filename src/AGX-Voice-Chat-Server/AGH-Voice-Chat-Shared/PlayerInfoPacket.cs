using System;
using LiteNetLib.Utils;

namespace AGH_Voice_Chat_Shared;

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