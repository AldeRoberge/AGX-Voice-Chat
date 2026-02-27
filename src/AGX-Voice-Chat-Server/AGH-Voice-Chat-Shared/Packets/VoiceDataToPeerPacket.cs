using System;
using LiteNetLib.Utils;

namespace AGH_Voice_Chat_Shared.Packets
{
    /// <summary>
    /// Voice data from host (Dissonance server) targeting one client. Server relays to that peer only.
    /// </summary>
    public class VoiceDataToPeerPacket : INetSerializable
    {
        public Guid TargetPlayerId { get; set; }
        public byte[] Data { get; set; } = [];
        public bool Reliable { get; set; }

        public void Serialize(NetDataWriter writer)
        {
            writer.PutBytesWithLength(TargetPlayerId.ToByteArray());
            writer.Put(Reliable);
            writer.PutBytesWithLength(Data);
        }

        public void Deserialize(NetDataReader reader)
        {
            TargetPlayerId = new Guid(reader.GetBytesWithLength());
            Reliable = reader.GetBool();
            Data = reader.GetBytesWithLength();
        }
    }
}
