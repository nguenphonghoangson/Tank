using System;
using System.Net;
using Mirror;
using Mirror.Discovery;

namespace Tank.Net
{
    /// <summary>What a hosted match tells the network about itself: who hosts it, how full it is, and whether it is still in the lobby.</summary>
    public struct RoomInfo : NetworkMessage
    {
        public Uri uri;
        public long serverId;               // the same machine can answer on several network cards; this tells the answers apart
        public string hostName;
        public int players;
        public int maxPlayers;
        public bool inLobby;

        public IPEndPoint EndPoint { get; set; }        // filled in by the client from where the answer came from; not sent
    }

    /// <summary>
    /// Finds matches on the local network. A host answers broadcasts with its RoomInfo (given by Describe); a client broadcasts and raises
    /// Found for every answer. Rooms on the same Wi-Fi or LAN show up with no address typed.
    /// </summary>
    public sealed class RoomDiscovery : NetworkDiscoveryBase<ServerRequest, RoomInfo>
    {
        /// <summary>Set by whoever hosts: the room as it is right now.</summary>
        public Func<RoomInfo> Describe { get; set; }

        public event Action<RoomInfo> Found;

        protected override RoomInfo ProcessRequest(ServerRequest request, IPEndPoint endpoint)
        {
            RoomInfo info = Describe != null ? Describe() : default;
            info.serverId = ServerId;
            info.uri = transport.ServerUri();
            return info;
        }

        protected override ServerRequest GetRequest() => new ServerRequest();

        protected override void ProcessResponse(RoomInfo response, IPEndPoint endpoint)
        {
            response.EndPoint = endpoint;
            // the address the host believes it has may not be reachable from here; the one the answer came from is
            response.uri = new UriBuilder(response.uri) { Host = endpoint.Address.ToString() }.Uri;
            Found?.Invoke(response);
        }
    }
}
