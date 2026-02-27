using System;
using AGH_Voice_Chat_Client.Game;
using AGH_Voice_Chat_Shared.Packets;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace AGH.Voice.Scripts
{
    /// <summary>
    /// Implements <see cref="IAGHVoiceTransport"/> over LiteNetLib.
    /// Call <see cref="SetLiteNet"/> with your server peer and packet processor after connecting.
    /// Optionally assign <see cref="positionSource"/> to auto-send position; or call <see cref="SendPosition"/> from your game.
    /// </summary>
    public class AGHVoiceTransportAdapter : MonoBehaviour, IAGHVoiceTransport
    {
        [SerializeField] private string localPlayerIdGuid = "";

        [Tooltip("If set, position is sent to the server at ~20Hz. Otherwise call SendPosition from your game.")] [SerializeField]
        private Transform positionSource;

        [SerializeField] private float positionSendInterval = 0.05f;
        private float _positionSendAccumulator;

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

        /// <summary>
        /// Send current position to the server (client-authoritative). Call from your game or assign positionSource to auto-send.
        /// </summary>
        public void SendPosition(Vector3 position)
        {
            if (!_isConnected || _serverPeer == null || _packetProcessor == null)
                return;
            var packet = new PlayerPositionPacket { Position = new System.Numerics.Vector3(position.x, position.y, position.z) };
            var writer = new NetDataWriter();
            _packetProcessor.Write(writer, packet);
            _serverPeer.Send(writer, 0, DeliveryMethod.Unreliable);
        }

        private void Update()
        {
            if (positionSource != null && _isConnected && _serverPeer != null && _packetProcessor != null)
            {
                _positionSendAccumulator += Time.deltaTime;
                if (_positionSendAccumulator >= positionSendInterval)
                {
                    _positionSendAccumulator = 0f;
                    SendPosition(positionSource.position);
                }
            }
        }
    }
}