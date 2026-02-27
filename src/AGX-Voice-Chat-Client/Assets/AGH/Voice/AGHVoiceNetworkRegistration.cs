using AGH.Shared;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace AGH.Voice
{
    /// <summary>
    /// Registers AGH voice packet types with a LiteNetLib NetPacketProcessor so the wire format matches the server.
    /// Call from your game's network setup (same processor you use for other game packets).
    /// </summary>
    public static class AGHVoiceNetworkRegistration
    {
        /// <summary>
        /// Register VoiceDataPacket, VoiceDataFromPacket, VoiceDataToPeerPacket with the processor.
        /// Must use the same order as the server (AGH.Shared.NetworkRegistration) so type ids match.
        /// </summary>
        public static void RegisterVoicePackets(NetPacketProcessor processor)
        {
            if (processor == null)
            {
                Debug.LogError("AGHVoiceNetworkRegistration: NetPacketProcessor is null.");
                return;
            }

            processor.RegisterNestedType(
                (writer, p) => p.Serialize(writer),
                reader =>
                {
                    var p = new VoiceDataPacket();
                    p.Deserialize(reader);
                    return p;
                });

            processor.RegisterNestedType(
                (writer, p) => p.Serialize(writer),
                reader =>
                {
                    var p = new VoiceDataFromPacket();
                    p.Deserialize(reader);
                    return p;
                });

            processor.RegisterNestedType(
                (writer, p) => p.Serialize(writer),
                reader =>
                {
                    var p = new VoiceDataToPeerPacket();
                    p.Deserialize(reader);
                    return p;
                });
        }

        /// <summary>
        /// Subscribe to VoiceDataFromPacket and invoke the callback so the voice transport can receive relayed voice.
        /// Call once during client setup; in the callback call your IAGHVoiceTransport's RaiseVoiceDataFrom.
        /// </summary>
        public static void SubscribeVoiceFrom(
            NetPacketProcessor processor,
            System.Action<VoiceDataFromPacket, NetPeer> onVoiceDataFrom)
        {
            if (processor == null || onVoiceDataFrom == null)
                return;

            processor.SubscribeReusable(onVoiceDataFrom);
        }
    }
}
