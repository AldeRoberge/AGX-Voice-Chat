using System;
using System.Collections.Concurrent;
using Dissonance.Networking;
using JetBrains.Annotations;
using UnityEngine;

namespace AGH.Voice.Scripts
{
    /// <summary>
    /// Dissonance comms network that sends/receives voice over the AGH game connection (LiteNetLib).
    /// Unity is client-only: assign an <see cref="IAGHVoiceTransport"/> and call <see cref="RunAsClient"/> when connected.
    /// Stop with <see cref="Stop"/> or when the component is disabled.
    /// </summary>
    public class AGHCommsNetwork : BaseCommsNetwork<AGHCommsServer, AGHCommsClient, Guid, AGHVoiceParams, AGHVoiceParams>
    {
        [CanBeNull] private IAGHVoiceTransport _transport;
        private readonly ConcurrentQueue<IncomingVoicePacket> _incoming = new();

        /// <summary>
        /// Assign the transport before starting. Can be set in code or by a component that implements <see cref="IAGHVoiceTransport"/>.
        /// </summary>
        [CanBeNull]
        public IAGHVoiceTransport Transport
        {
            get => _transport;
            set => _transport = value;
        }

        protected override void OnDisable()
        {
            UnsubscribeTransport();
            base.OnDisable();
        }

        private void UnsubscribeTransport()
        {
            if (_transport != null)
            {
                _transport.VoiceDataFromReceived -= OnVoiceDataFromReceived;
                _transport = null;
            }
        }

        private void OnVoiceDataFromReceived(Guid fromPlayerId, byte[] data, bool reliable)
        {
            Debug.Log($"AGHCommsNetwork: Received voice data from player {fromPlayerId}, length {data?.Length ?? 0}, reliable {reliable}");

            if (data == null || data.Length == 0)
            {
                Debug.LogWarning("AGHCommsNetwork: Received null or empty voice data, ignoring.");
                return;
            }

            _incoming.Enqueue(new IncomingVoicePacket(fromPlayerId, data, reliable));
        }

        protected override void Update()
        {
            DrainIncomingVoicePackets();
            base.Update();
        }

        /// <summary>
        /// Drain incoming VoiceDataFromPacket and feed the Dissonance client.
        /// </summary>
        private void DrainIncomingVoicePackets()
        {
            while (_incoming.TryDequeue(out var p))
            {
                var segment = new ArraySegment<byte>(p.Data, 0, p.Data.Length);
                Client?.NetworkReceivedPacket(segment);
            }
        }

        protected override AGHCommsServer CreateServer(AGHVoiceParams connectionParameters)
        {
            var transport = connectionParameters?.Transport;
            if (transport == null)
                throw new InvalidOperationException("AGHCommsNetwork: Server requires a non-null Transport in AGHVoiceParams.");
            return new AGHCommsServer(transport);
        }

        protected override AGHCommsClient CreateClient(AGHVoiceParams connectionParameters)
        {
            var transport = connectionParameters?.Transport;
            if (transport == null)
                throw new InvalidOperationException("AGHCommsNetwork: Client requires a non-null Transport in AGHVoiceParams.");
            SubscribeTransport(transport);
            return new AGHCommsClient(this, transport);
        }

        private void SubscribeTransport(IAGHVoiceTransport transport)
        {
            UnsubscribeTransport();
            _transport = transport;
            _transport.VoiceDataFromReceived += OnVoiceDataFromReceived;
        }

        /// <summary>
        /// Start Dissonance as a client. Call when the game has connected to the AGH server and you have a transport.
        /// </summary>
        public void RunAsClient(IAGHVoiceTransport transport)
        {
            if (transport == null)
                throw new ArgumentNullException(nameof(transport));
            RunAsClient(new AGHVoiceParams(transport));
        }

        /// <summary>
        /// Stops the session and unsubscribes from the transport.
        /// </summary>
        public new void Stop()
        {
            UnsubscribeTransport();
            base.Stop();
        }

        private readonly struct IncomingVoicePacket
        {
            public readonly Guid FromPlayerId;
            public readonly byte[] Data;
            public readonly bool Reliable;

            public IncomingVoicePacket(Guid fromPlayerId, byte[] data, bool reliable)
            {
                FromPlayerId = fromPlayerId;
                Data = data;
                Reliable = reliable;
            }
        }
    }
}