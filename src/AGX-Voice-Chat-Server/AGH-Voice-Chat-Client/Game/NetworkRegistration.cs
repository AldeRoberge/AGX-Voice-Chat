using System.Numerics;
using AGH_Voice_Chat_Shared;
using AGH_Voice_Chat_Shared.Packets.Join;
using AGH_Voice_Chat_Shared.Packets.Ping;
using AGH_Voice_Chat_Shared.Packets.Voice;
using LiteNetLib.Utils;

namespace AGH_Voice_Chat_Client.Game
{
    public static class NetworkRegistration
    {
        public static void RegisterTypes(NetPacketProcessor packetProcessor)
        {
            packetProcessor.RegisterNestedType(
                (w, v) => { w.Put(v.X); w.Put(v.Y); },
                r => new Vector2(r.GetFloat(), r.GetFloat()));
            packetProcessor.RegisterNestedType(
                (w, v) => { w.Put(v.X); w.Put(v.Y); w.Put(v.Z); },
                r => new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat()));

            Register<JoinRequestPacket>(packetProcessor);
            Register<JoinResponsePacket>(packetProcessor);
            Register<PlayerPositionPacket>(packetProcessor);
            Register<PlayerPositionUpdatePacket>(packetProcessor);
            Register<PlayerLeftPacket>(packetProcessor);
            Register<PingPacket>(packetProcessor);
            Register<PongPacket>(packetProcessor);
            Register<PlayerInfoPacket>(packetProcessor);

            Register<VoiceDataPacket>(packetProcessor);
            Register<VoiceDataFromPacket>(packetProcessor);
            Register<VoiceDataToPeerPacket>(packetProcessor);
        }

        private static void Register<T>(NetPacketProcessor pp) where T : class, INetSerializable, new()
        {
            pp.RegisterNestedType(
                (w, p) => p.Serialize(w),
                r => { var p = new T(); p.Deserialize(r); return p; });
        }
    }
}
