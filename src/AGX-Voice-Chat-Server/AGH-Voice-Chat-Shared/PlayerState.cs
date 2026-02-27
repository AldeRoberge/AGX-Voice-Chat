using System;
using System.Numerics;
using LiteNetLib.Utils;

namespace AGH_Voice_Chat_Shared;

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