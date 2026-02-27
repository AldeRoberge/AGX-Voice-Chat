using System;
using AGH_Voice_Chat_Shared.Packets;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace AGH.Voice
{
    /// <summary>
    /// Implements <see cref="IAGHVoiceTransport"/> over LiteNetLib.
    /// Call <see cref="SetLiteNet"/> with your server peer and packet processor after connecting.
    /// Register voice packets with <see cref="AGHVoiceNetworkRegistration.RegisterVoicePackets"/> and subscribe to
    /// VoiceDataFromPacket, then call <see cref="RaiseVoiceDataFrom"/> when received.
    /// </summary>
    public class AGHVoiceTransportAdapter : MonoBehaviour, IAGHVoiceTransport
    {
        [SerializeField] private string localPlayerIdGuid = "";

        private bool _isConnected;
        private NetPeer _serverPeer;
        private NetPacketProcessor _packetProcessor;

        public bool IsConnected
        {
            get => _isConnected;
            set => _isConnected = value;
        }

        public event Action<Guid, byte[], bool> VoiceDataFromReceived;

        /// <summary>
        /// Wire to your game connection. Call after you have a connected server peer and a packet processor
        /// that has had <see cref="AGHVoiceNetworkRegistration.RegisterVoicePackets"/> called.
        /// </summary>
        public void SetLiteNet(NetPeer serverPeer, NetPacketProcessor packetProcessor)
        {
            _serverPeer = serverPeer;
            _packetProcessor = packetProcessor;
        }

        /// <summary>
        /// Call when you receive a VoiceDataFromPacket from the server (e.g. from your packet subscription).
        /// </summary>
        public void RaiseVoiceDataFrom(Guid fromPlayerId, byte[] data, bool reliable)
        {
            VoiceDataFromReceived?.Invoke(fromPlayerId, data, reliable);
        }

        public void SendToServer(byte[] data, bool reliable)
        {
            if (data == null || data.Length == 0)
                return;
            if (_serverPeer == null || _packetProcessor == null)
            {
                Debug.LogWarning("AGHVoiceTransportAdapter: SetLiteNet(serverPeer, processor) not called or peer disconnected.");
                return;
            }

            var packet = new VoiceDataPacket { Data = data, Reliable = reliable };
            var writer = new NetDataWriter();
            _packetProcessor.Write(writer, packet);
            const byte channel = 0;
            _serverPeer.Send(writer, channel, reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable);
        }

    }
}
