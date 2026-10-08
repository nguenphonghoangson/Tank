using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

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
                string buffs = "HP " + SpikeTank.HudHp + (SpikeTank.HudShield > 0 ? "  shield " + SpikeTank.HudShield : "") + (SpikeTank.HudDmg ? "  damage x1.5" : "") + (SpikeTank.HudWeapon != 0 ? "  " + SpikeSim.Weapons[SpikeTank.HudWeapon].name + " " + SpikeTank.HudAmmo : "") + (SpikeTank.HudSpeedTime > 0f ? "  speed " + SpikeTank.HudSpeedTime.ToString("0.0") + "s" : "");
                GUI.Label(new Rect(10f, 10f, 700f, 24f), buffs);
                Rect b = SpikeTank.DashButton;
                var r = new Rect(b.x, Screen.height - b.yMax, b.width, b.height);
                float cd = SpikeTank.HudDashCd;
                GUI.Box(r, cd > 0.05f ? "DASH\n" + cd.ToString("0.0") : "DASH");
            }
            if (me == null && NetworkClient.active) GUI.Label(new Rect(10f, 10f, 400f, 24f), "connecting...");
            if (SpikeMatch.I != null) DrawMatchHud();
        }

        GUIStyle m_Style;
        static Texture2D Dot { get { return Texture2D.whiteTexture; } }

        void Fill(Rect r, Color c) { Color old = GUI.color; GUI.color = c; GUI.DrawTexture(r, Dot); GUI.color = old; }

        void DrawMatchHud()
        {
            SpikeMatch m = SpikeMatch.I;
            float k = Mathf.Max(1f, Screen.height / 540f);
            if (m_Style == null) m_Style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            m_Style.fontSize = Mathf.RoundToInt(15f * k);

            // top centre: team scores and clock
            float w = 150f * k, h = 26f * k, cx = Screen.width * 0.5f;
            Fill(new Rect(cx - w * 1.5f, 4f * k, w * 3f, h), new Color(0f, 0f, 0f, 0.45f));
            m_Style.normal.textColor = SpikeMatch.TeamColors[0]; GUI.Label(new Rect(cx - w * 1.5f, 4f * k, w, h), SpikeMatch.TeamNames[0] + " " + m.score0, m_Style);
            m_Style.normal.textColor = Color.white; GUI.Label(new Rect(cx - w * 0.5f, 4f * k, w, h), (m.timeLeft / 60) + ":" + (m.timeLeft % 60).ToString("00"), m_Style);
            m_Style.normal.textColor = SpikeMatch.TeamColors[1]; GUI.Label(new Rect(cx + w * 0.5f, 4f * k, w, h), SpikeMatch.TeamNames[1] + " " + m.score1, m_Style);

            // flag strip: one box per flag, owner colour, fill shows hold or capture progress
            int n = m.Points.Length; float fw = 30f * k, fy = 4f * k + h + 3f * k;
            for (int i = 0; i < n && i < m.flags.Count; i++)
            {
                FlagSync f = m.flags[i];
                Rect r = new Rect(cx - n * fw * 0.5f + i * fw + 2f, fy, fw - 4f, fw - 4f);
                Fill(r, new Color(0f, 0f, 0f, 0.5f));
                int team = f.owner >= 0 ? f.owner : f.capturer;
                float level = f.owner >= 0 ? f.hold / 255f : f.progress / 255f;
                if (team >= 0) Fill(new Rect(r.x, r.yMax - r.height * level, r.width, r.height * level), SpikeMatch.TeamColors[team]);
                m_Style.normal.textColor = f.contested ? Color.yellow : Color.white; m_Style.fontSize = Mathf.RoundToInt(13f * k);
                GUI.Label(r, m.Points[i].label, m_Style);
            }

            // minimap, top right
            SpikeMapVisuals mv = SpikeMapVisuals.I;
            float S = Mathf.Min(Screen.width, Screen.height) * 0.28f;
            var map = new Rect(Screen.width - S - 8f * k, 8f * k, S, S);
            Fill(map, new Color(0f, 0f, 0f, 0.5f));
            float half = mv != null ? mv.half : 60f;
            System.Func<Vector3, Vector2> P = wp => new Vector2(map.x + (wp.x / half * 0.5f + 0.5f) * S, map.y + (0.5f - wp.z / half * 0.5f) * S);
            if (mv != null)
                foreach (Vector4 b in mv.blocks)
                {
                    Vector2 c = P(new Vector3(b.x, 0f, b.y)); float bw = b.z / half * 0.5f * S, bh = b.w / half * 0.5f * S;
                    Fill(new Rect(c.x - bw * 0.5f, c.y - bh * 0.5f, bw, bh), new Color(0.55f, 0.5f, 0.4f, 0.8f));
                }
            for (int i = 0; i < n && i < m.flags.Count; i++)
            {
                FlagSync f = m.flags[i]; Vector2 c = P(m.Points[i].transform.position); float d = 9f * k;
                Fill(new Rect(c.x - d * 0.5f, c.y - d * 0.5f, d, d), f.owner >= 0 ? SpikeMatch.TeamColors[f.owner] : new Color(0.7f, 0.7f, 0.75f));
            }
            foreach (SpikeTank t in SpikeTank.All)
            {
                if (t == null || t.ClientDead || t.RenderPos == Vector3.zero) continue;
                Vector2 c = P(t.RenderPos); float d = (t == SpikeTank.Local ? 7f : 5f) * k;
                Fill(new Rect(c.x - d * 0.5f, c.y - d * 0.5f, d, d), t == SpikeTank.Local ? Color.white : SpikeMatch.TeamColors[SpikeMatch.TeamOf(t.netId)]);
            }

            DrawCores(k);

            // Tab (or tap the clock) holds the K/D/A board; it stays up once the match is decided
            bool tab = Keyboard.current != null && Keyboard.current.tabKey.isPressed;
            if (Event.current.type == EventType.MouseDown && new Rect(cx - w * 0.5f, 4f * k, w, h).Contains(Event.current.mousePosition)) m_BoardOpen = !m_BoardOpen;
            if (m.winner != -2 || tab || m_BoardOpen) DrawKdaBoard(m, k);

            if (m.winner != -2)
            {
                m_Style.fontSize = Mathf.RoundToInt(30f * k); m_Style.normal.textColor = m.winner >= 0 ? SpikeMatch.TeamColors[m.winner] : Color.white;
                string who = m.winnerId == (SpikeTank.Local != null ? SpikeTank.Local.netId : 0u) ? "YOU WIN" : "P" + m.winnerId + " WINS";
                GUI.Label(new Rect(0f, Screen.height * 0.18f, Screen.width, 50f * k), m.winner >= 0 ? who + "  (highest KDA)" : "DRAW", m_Style);
            }
        }

        void DrawCores(float k)
        {
            string owned = "";
            for (int i = 0; i < SpikeCores.Count; i++) if (SpikeCores.Has(SpikeTank.HudCores, i)) owned += (owned.Length > 0 ? "  |  " : "") + TankGame.Prototype.CoreLibrary.All[i].name;
            var st = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(12f * k), alignment = TextAnchor.LowerLeft };
            st.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
            if (owned.Length > 0) GUI.Label(new Rect(10f, Screen.height - 56f * k, Screen.width * 0.6f, 22f * k), "Cores: " + owned, st);

            int[] offers = SpikeTank.HudOffers;
            if (offers == null || SpikeTank.Local == null) return;
            float cw = 190f * k, ch = 96f * k, gap = 12f * k, total = offers.Length * cw + (offers.Length - 1) * gap;
            float x0 = Screen.width * 0.5f - total * 0.5f, y0 = Screen.height * 0.62f;
            var head = new GUIStyle(st) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(15f * k), fontStyle = FontStyle.Bold };
            head.normal.textColor = Color.white;
            float left = Mathf.Max(0f, SpikeTank.HudOfferUntil - Time.time);
            GUI.Label(new Rect(x0, y0 - 30f * k, total, 24f * k), "Choose a core (" + Mathf.CeilToInt(left) + "s)  -  keys 1 / 2 / 3 or click", head);
            var name = new GUIStyle(st) { alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold, fontSize = Mathf.RoundToInt(14f * k), wordWrap = true };
            var desc = new GUIStyle(st) { alignment = TextAnchor.UpperCenter, fontSize = Mathf.RoundToInt(12f * k), wordWrap = true };
            for (int i = 0; i < offers.Length; i++)
            {
                TankGame.Prototype.CoreDef core = TankGame.Prototype.CoreLibrary.All[offers[i]];
                var r = new Rect(x0 + i * (cw + gap), y0, cw, ch);
                Fill(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), core.color);
                Fill(r, new Color(0.06f, 0.08f, 0.12f, 0.95f));
                name.normal.textColor = core.color; GUI.Label(new Rect(r.x + 6f * k, r.y + 6f * k, r.width - 12f * k, 24f * k), (i + 1) + "  " + core.name, name);
                desc.normal.textColor = Color.white; GUI.Label(new Rect(r.x + 8f * k, r.y + 34f * k, r.width - 16f * k, r.height - 40f * k), core.description, desc);
                bool clicked = Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition);
                bool key = Keyboard.current != null && (i == 0 ? Keyboard.current.digit1Key : i == 1 ? Keyboard.current.digit2Key : Keyboard.current.digit3Key).wasPressedThisFrame;
                if (clicked || key) { SpikeTank.Local.PickCore(i); return; }
            }
        }

        bool m_BoardOpen;
        readonly List<SpikeTank> m_Rank = new List<SpikeTank>();

        void DrawKdaBoard(SpikeMatch m, float k)
        {
            m_Rank.Clear();
            foreach (SpikeTank t in SpikeTank.All) if (t != null) m_Rank.Add(t);
            m_Rank.Sort((a, b) =>
            {
                m.TryGetKda(a.netId, out KdaSync x); m.TryGetKda(b.netId, out KdaSync y);
                int c = SpikeMatch.Ratio(y.k, y.d, y.a).CompareTo(SpikeMatch.Ratio(x.k, x.d, x.a));
                return c != 0 ? c : y.k.CompareTo(x.k);
            });
            float rowH = 22f * k, w = Mathf.Min(Screen.width - 20f, 460f * k), x0 = Screen.width * 0.5f - w * 0.5f, y0 = Screen.height * 0.28f;
            Fill(new Rect(x0, y0, w, rowH * (m_Rank.Count + 1) + 8f * k), new Color(0f, 0f, 0f, 0.72f));
            var st = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(13f * k), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            st.normal.textColor = Color.white;
            float[] col = { 0.04f, 0.38f, 0.52f, 0.66f, 0.80f };
            string[] head = { "Player", "K", "D", "A", "KDA" };
            for (int c = 0; c < 5; c++) GUI.Label(new Rect(x0 + w * col[c], y0 + 4f * k, w * 0.2f, rowH), head[c], st);
            for (int i = 0; i < m_Rank.Count; i++)
            {
                SpikeTank t = m_Rank[i]; m.TryGetKda(t.netId, out KdaSync v);
                float y = y0 + 4f * k + rowH * (i + 1);
                Fill(new Rect(x0 + 4f * k, y + 5f * k, 6f * k, rowH - 10f * k), SpikeMatch.TeamColors[SpikeMatch.TeamOf(t.netId)]);
                st.normal.textColor = t == SpikeTank.Local ? Color.yellow : Color.white;
                string[] cells = { (t.netId == m.winnerId && m.winner != -2 ? "WIN " : "") + "P" + t.netId + (t == SpikeTank.Local ? " (you)" : ""), v.k.ToString(), v.d.ToString(), v.a.ToString(), SpikeMatch.Ratio(v.k, v.d, v.a).ToString("0.00") };
                for (int c = 0; c < 5; c++) GUI.Label(new Rect(x0 + w * col[c], y, w * 0.3f, rowH), cells[c], st);
            }
        }
    }
}
