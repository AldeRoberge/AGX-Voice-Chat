using System;
using LiteNetLib.Utils;

namespace AGH_Voice_Chat_Shared.Packets
{
    /// <summary>
    /// Opaque voice data for Dissonance voice chat relay (client -> server).
    /// Server relays to all other connected clients as <see cref="VoiceDataFromPacket"/>.
    /// </summary>
    public class VoiceDataPacket : INetSerializable
    {
        public byte[] Data { get; set; } = [];
        public bool Reliable { get; set; }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(Reliable);
            writer.PutBytesWithLength(Data);
        }

        public void Deserialize(NetDataReader reader)
        {
            Reliable = reader.GetBool();
            Data = reader.GetBytesWithLength();
        }
    }
}