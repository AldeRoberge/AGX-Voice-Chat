using System;
using LiteNetLib.Utils;

namespace AGH_Voice_Chat_Shared.Packets
{
    /// <summary>
    /// Voice data relayed from server to clients. Includes sender so Dissonance can route by peer.
    /// </summary>
    public class VoiceDataFromPacket : INetSerializable
    {
        public Guid FromPlayerId { get; set; }
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public bool Reliable { get; set; }

        public void Serialize(NetDataWriter writer)
        {
            writer.PutBytesWithLength(FromPlayerId.ToByteArray());
            writer.Put(Reliable);
            writer.PutBytesWithLength(Data);
        }

        public void Deserialize(NetDataReader reader)
        {
            FromPlayerId = new Guid(reader.GetBytesWithLength());
            Reliable = reader.GetBool();
            Data = reader.GetBytesWithLength();
        }
    }
}