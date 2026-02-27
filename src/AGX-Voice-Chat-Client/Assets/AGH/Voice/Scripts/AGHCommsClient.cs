using System;
using Dissonance.Networking;
using JetBrains.Annotations;

namespace AGH.Voice
{
    /// <summary>
    /// Dissonance client that sends/receives via the AGH game connection.
    /// Sends VoiceDataPacket to the server; incoming VoiceDataFromPacket is fed by AGHCommsNetwork.
    /// </summary>
    public class AGHCommsClient : BaseClient<AGHCommsServer, AGHCommsClient, Guid>
    {
        private readonly IAGHVoiceTransport _transport;

        public AGHCommsClient([NotNull] ICommsNetworkState network, [NotNull] IAGHVoiceTransport transport)
            : base(network)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        public override void Connect()
        {
            if (!_transport.IsConnected)
            {
                FatalError("AGH transport is not connected.");
                return;
            }
            Connected();
        }

        protected override void ReadMessages()
        {
            // Incoming voice packets are drained by AGHCommsNetwork and fed to both client and server (when host).
        }

        protected override void SendReliable(ArraySegment<byte> packet)
        {
            if (!_transport.IsConnected)
                return;
            var copy = new byte[packet.Count];
            Array.Copy(packet.Array, packet.Offset, copy, 0, packet.Count);
            _transport.SendToServer(copy, reliable: true);
        }

        protected override void SendUnreliable(ArraySegment<byte> packet)
        {
            if (!_transport.IsConnected)
                return;
            var copy = new byte[packet.Count];
            Array.Copy(packet.Array, packet.Offset, copy, 0, packet.Count);
            _transport.SendToServer(copy, reliable: false);
        }
    }
}
