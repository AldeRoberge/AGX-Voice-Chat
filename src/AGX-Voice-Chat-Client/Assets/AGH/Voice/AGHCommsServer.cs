using System;
using Dissonance.Networking;
using JetBrains.Annotations;

namespace AGH.Voice
{
    /// <summary>
    /// Stub required by Dissonance BaseCommsNetwork generic. Not used — Unity is client-only; AGH.Server is the only server.
    /// </summary>
    public class AGHCommsServer : BaseServer<AGHCommsServer, AGHCommsClient, Guid>
    {
        public AGHCommsServer([NotNull] IAGHVoiceTransport transport)
        {
            _ = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        protected override void ReadMessages() { }

        protected override void SendReliable(Guid connection, ArraySegment<byte> packet) { }

        protected override void SendUnreliable(Guid connection, ArraySegment<byte> packet) { }
    }
}
