using System;

namespace AGH.Voice.Scripts
{
    /// <summary>
    /// Transport for Dissonance voice over the AGH game connection.
    /// Implement with your LiteNetLib connection: send VoiceDataPacket to the server,
    /// raise VoiceDataFromReceived when you receive VoiceDataFromPacket (see AGH.Server Voice README).
    /// </summary>
    public interface IAGHVoiceTransport
    {


        /// <summary>
        /// True when the game connection is connected and voice can be sent/received.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Send opaque Dissonance payload to the server (client → server).
        /// Implement by sending a VoiceDataPacket with Data = data, Reliable = reliable.
        /// </summary>
        void SendToServer(byte[] data, bool reliable);

        /// <summary>
        /// Raised when the server sends us voice from another player (VoiceDataFromPacket).
        /// Args: fromPlayerId, data, reliable.
        /// Call from your packet handler when you receive VoiceDataFromPacket.
        /// </summary>
        event Action<Guid, byte[], bool> VoiceDataFromReceived;
    }
}
