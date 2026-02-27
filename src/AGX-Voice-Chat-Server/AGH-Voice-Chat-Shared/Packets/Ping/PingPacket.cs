using LiteNetLib.Utils;

namespace AGH_Voice_Chat_Shared.Packets.Ping;

public class PingPacket : INetSerializable
{
    public long Timestamp { get; set; }
    public void Serialize(NetDataWriter writer) => writer.Put(Timestamp);
    public void Deserialize(NetDataReader reader) => Timestamp = reader.GetLong();
}