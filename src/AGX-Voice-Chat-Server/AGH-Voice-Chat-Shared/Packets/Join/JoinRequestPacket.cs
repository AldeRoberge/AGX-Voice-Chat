using LiteNetLib.Utils;

namespace AGH_Voice_Chat_Shared.Packets.Join;

public class JoinRequestPacket : INetSerializable
{
    public string PlayerName { get; set; } = string.Empty;
    public void Serialize(NetDataWriter writer) => writer.PutLargeString(PlayerName);
    public void Deserialize(NetDataReader reader) => PlayerName = reader.GetLargeString();
}