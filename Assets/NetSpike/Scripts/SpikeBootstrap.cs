using System;
using Mirror;
using UnityEngine;

namespace TankGame.NetSpike
{
    /// <summary>
    /// Starts the spike from the command line (so tests run unattended) and writes the metrics file at the end.
    /// -spikeServer | -spikeHost | -spikeClient &lt;ip&gt;   -spikeBot   -spikeSeconds N   -spikeOut path
    /// -latencyMs N (one way, applied on every side)   -lossPct P   -jitter 0..1   -port N
    /// Without arguments the Mirror HUD buttons are used.
    /// </summary>
    public sealed class SpikeBootstrap : MonoBehaviour
    {
        public NetworkManager manager;
        public LatencySimulation latencySim;

        string m_Role, m_Out;
        float m_Seconds, m_Start, m_NextRtt, m_Latency, m_Loss;
        bool m_Bot, m_Finished;

        static string Arg(string name)
        {
            string[] a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        static bool Has(string name) { return Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0; }

        static float Num(string name, float fallback)
        {
            string s = Arg(name);
            return s != null && float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : fallback;
        }

        void Start()
        {
            Application.runInBackground = true;
            Application.targetFrameRate = Application.isBatchMode ? 120 : 60;
            SpikeMetrics.Hook();
            m_Seconds = Num("-spikeSeconds", 0f);
            m_Out = Arg("-spikeOut");
            m_Bot = Has("-spikeBot");
            m_Latency = Num("-latencyMs", 0f);
            m_Loss = Num("-lossPct", 0f);
            SpikeTank.JitterTicks = Mathf.RoundToInt(Num("-jitterBuffer", 0f));

            if (m_Latency > 0f || m_Loss > 0f)
            {
                latencySim.latency = m_Latency;
                latencySim.jitter = Num("-jitter", 0.02f);
                latencySim.unreliableLoss = m_Loss;
                latencySim.unreliableScramble = 0f;
                manager.transport = latencySim;
                Transport.active = latencySim;
            }
            if (Arg("-port") != null && Transport.active is PortTransport pt) pt.Port = (ushort)Num("-port", 7777);

            if (Has("-spikeServer")) { m_Role = "server"; manager.StartServer(); }
            else if (Has("-spikeHost")) { m_Role = "host"; manager.StartHost(); }
            else if (Arg("-spikeClient") != null) { m_Role = "client"; manager.networkAddress = Arg("-spikeClient"); manager.StartClient(); }
            m_Start = Time.realtimeSinceStartup;
            if (m_Role == null && !Application.isBatchMode) m_Address = PlayerPrefs.GetString("spike.addr", "192.168.1.2");
        }

        string m_Address = "";
        void StartFromMenu(bool host)
        {
            PlayerPrefs.SetString("spike.addr", m_Address);
            m_Start = Time.realtimeSinceStartup;
            if (host) { m_Role = "host"; manager.StartHost(); }
            else { m_Role = "client"; manager.networkAddress = m_Address.Trim(); manager.StartClient(); }
        }

        void Update()
        {
            if (m_Role == null || m_Finished) return;
            if (m_Bot && SpikeTank.Local != null) SpikeTank.Local.botMode = true;
            float t = Time.realtimeSinceStartup - m_Start;
            if (NetworkClient.active && Time.realtimeSinceStartup >= m_NextRtt) { m_NextRtt = Time.realtimeSinceStartup + 1f; SpikeMetrics.RttMs.Add((float)(NetworkTime.rtt * 1000.0)); }
            if (m_Seconds > 0f && t >= m_Seconds)
            {
                m_Finished = true;
                SpikeMetrics.SecondsRun = t;
                if (!string.IsNullOrEmpty(m_Out)) SpikeMetrics.Write(m_Out, m_Role, m_Latency, m_Loss);
                Application.Quit(0);
            }
        }

        void OnGUI()
        {
            if (Application.isBatchMode) return;
            if (m_Role == null)
            {
                float k = Mathf.Max(1f, Screen.height / 540f);
                GUI.matrix = Matrix4x4.Scale(new Vector3(k, k, 1f));
                GUI.Label(new Rect(20, 20, 400, 24), "Server address (Mac IP on the same Wi-Fi):");
                m_Address = GUI.TextField(new Rect(20, 48, 260, 32), m_Address);
                if (GUI.Button(new Rect(20, 90, 125, 40), "Connect")) StartFromMenu(false);
                if (GUI.Button(new Rect(155, 90, 125, 40), "Host")) StartFromMenu(true);
                return;
            }
            SpikeTank me = SpikeTank.Local;
            string line = "rtt " + (NetworkClient.active ? Mathf.RoundToInt((float)(NetworkTime.rtt * 1000.0)).ToString() : "-") + " ms   corrections " + SpikeMetrics.ReconCount +
                          "   >0.5 m: " + SpikeMetrics.ReconOver05 + "   shots " + SpikeMetrics.Shots + "   hits " + SpikeMetrics.Hits + "   kills " + SpikeMetrics.Kills;
            GUI.Label(new Rect(10f, Screen.height - 28f, 900f, 24f), line);
            if (me != null && NetworkClient.active)
            {
                string buffs = "HP " + SpikeTank.HudHp + (SpikeTank.HudShield > 0 ? "  shield " + SpikeTank.HudShield : "") + (SpikeTank.HudDmg ? "  damage x1.5" : "") + (SpikeTank.HudSpeedTime > 0f ? "  speed " + SpikeTank.HudSpeedTime.ToString("0.0") + "s" : "");
                GUI.Label(new Rect(10f, 10f, 700f, 24f), buffs);
                Rect b = SpikeTank.DashButton;
                var r = new Rect(b.x, Screen.height - b.yMax, b.width, b.height);
                float cd = SpikeTank.HudDashCd;
                GUI.Box(r, cd > 0.05f ? "DASH\n" + cd.ToString("0.0") : "DASH");
            }
            if (me == null && NetworkClient.active) GUI.Label(new Rect(10f, 10f, 400f, 24f), "connecting...");
        }
    }
}
