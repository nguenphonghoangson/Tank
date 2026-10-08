using System;

namespace Tank.Net
{
    /// <summary>What the command line asked for, so servers and test clients start with no one at the keyboard.</summary>
    public static class LaunchOptions
    {
        public enum Mode { Menu, Offline, Host, Server, Client }

        public static Mode Start { get; private set; } = Mode.Menu;
        public static string Address { get; private set; } = "localhost";
        public static bool AddressGiven { get; private set; }
        public static bool Lobby { get; private set; }                    // -lobby: a -host or -server waits in the lobby like the menu's host
        public static float StartAfterSeconds { get; private set; }       // with -lobby: the server starts the match by itself after this long
        public static int Bots { get; private set; } = -1;                 // -1: not given, use the scene's default
        public static bool Autoplay { get; private set; }                  // a bot drives the local tank (test clients)
        public static bool LogStats { get; private set; }
        public static float QuitAfterSeconds { get; private set; }
        public static float LatencyMs { get; private set; }               // one way, applied on every side, so the round trip is about twice this
        public static float LossPercent { get; private set; }
        public static int Port { get; private set; }
        public static string PlayerName { get; private set; } = string.Empty;

        static class ValueHolder { public static float F; }

        static LaunchOptions()
        {
            string[] a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length; i++)
            {
                switch (a[i])
                {
                    case "-offline": Start = Mode.Offline; break;
                    case "-host": Start = Mode.Host; break;
                    case "-server": Start = Mode.Server; break;
                    case "-client": Start = Mode.Client; break;
                    case "-autoplay": Autoplay = true; break;
                    case "-lobby": Lobby = true; break;
                    case "-startAfter": if (i + 1 < a.Length && float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float sa)) StartAfterSeconds = sa; i++; break;
                    case "-logstats": LogStats = true; break;
                    case "-address": if (i + 1 < a.Length) { Address = a[++i]; AddressGiven = true; } break;
                    case "-bots": if (i + 1 < a.Length && int.TryParse(a[i + 1], out int n)) Bots = n; i++; break;
                    case "-latencyMs": if (i + 1 < a.Length) float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out ValueHolder.F); LatencyMs = ValueHolder.F; i++; break;
                    case "-lossPct": if (i + 1 < a.Length) float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out ValueHolder.F); LossPercent = ValueHolder.F; i++; break;
                    case "-name": if (i + 1 < a.Length) PlayerName = a[++i]; break;
                    case "-port": if (i + 1 < a.Length && int.TryParse(a[i + 1], out int port)) Port = port; i++; break;
                    case "-quitAfter": if (i + 1 < a.Length && float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float s)) QuitAfterSeconds = s; i++; break;
                }
            }
        }
    }
}
