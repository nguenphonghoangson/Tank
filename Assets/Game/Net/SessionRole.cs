using Mirror;

namespace Tank.Net
{
    public enum SessionRole { Offline, Server, Host, Client }

    /// <summary>What this machine is doing in the match right now. Anything that behaves differently for a server and a client asks this, not Mirror.</summary>
    public interface ISessionRole
    {
        SessionRole Current { get; }
    }

    public sealed class MirrorSessionRole : ISessionRole
    {
        public SessionRole Current
        {
            get
            {
                if (NetworkServer.active) return NetworkClient.active ? SessionRole.Host : SessionRole.Server;
                return NetworkClient.active ? SessionRole.Client : SessionRole.Offline;
            }
        }
    }
}
