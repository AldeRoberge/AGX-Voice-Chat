using System.Numerics;
using LiteNetLib.Utils;

namespace AGH_Voice_Chat_Shared;

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
        Position = new Vector3(
            reader.GetFloat(),
            reader.GetFloat(),
            reader.GetFloat()
        );
    }
}