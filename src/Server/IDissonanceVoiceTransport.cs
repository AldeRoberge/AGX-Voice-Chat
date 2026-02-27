using LiteNetLib;

namespace AGH.Server
{
    /// <summary>
    /// Transport contract for the Dissonance voice module.
    /// Implemented by the server to relay voice packets to connected peers.
    /// </summary>
    public interface IDissonanceVoiceTransport
    {
        /// <summary>
        /// Returns all connected peers except the given one (e.g. the sender).
        /// </summary>
        IEnumerable<NetPeer> GetPeersExcept(NetPeer exclude);

        /// <summary>
        /// Gets the player id associated with a peer. Returns false if the peer is not in the session.
        /// </summary>
        bool TryGetPlayerId(NetPeer peer, out Guid playerId);

        /// <summary>
        /// Sends voice data to a specific peer with sender identity (relay from one client to others).
        /// </summary>
        void SendVoiceFromTo(NetPeer peer, Guid fromPlayerId, byte[] data, bool reliable);

        /// <summary>
        /// Sends voice data from the given sender to the peer with the target player id (host-to-peer).
        /// </summary>
        void SendVoiceFromToTarget(Guid targetPlayerId, Guid fromPlayerId, byte[] data, bool reliable);
    }
}
