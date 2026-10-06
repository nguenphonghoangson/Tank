using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TankGame.Prototype
{
    /// <summary>
    /// Throwaway IMGUI debug HUD: territory bar, team rows, local player status, control point markers, event log,
    /// scoreboard (hold Tab, and at match end). IMGUI allocates every frame; replace it when real UI work starts.
    /// </summary>
    public sealed class MatchHud : MonoBehaviour
    {
        public MatchManager match;
        public Camera cam;

        GUIStyle m_Label, m_Big, m_Small;
        Texture2D m_White, m_Disc;
        readonly List<int> m_Order = new List<int>();

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || match == null || match.Players == null || match.Layout == null) return;
            if (m_Label == null)
            {
                m_White = Texture2D.whiteTexture;
                m_Label = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
                m_Label.normal.textColor = Color.white;
                m_Big = new GUIStyle(m_Label) { fontSize = 34, alignment = TextAnchor.MiddleCenter };
                m_Small = new GUIStyle(m_Label) { fontSize = 13, fontStyle = FontStyle.Normal };
            }

            DrawTerritory();
            DrawLocalStatus();
            DrawPointMarkers();
            DrawMinimap();
            DrawLog();
            DrawRespawn();
            Keyboard kb = Keyboard.current;
            if (match.State == MatchManager.MatchState.Picking) DrawPicking();
            else if (match.State == MatchManager.MatchState.Ended) DrawEnd();
            else if (kb != null && kb.tabKey.isPressed) DrawScoreboard(new Rect(Screen.width * 0.5f - 300f, 120f, 600f, 260f), true);
            GUI.Label(new Rect(14f, Screen.height - 30f, Screen.width - 28f, 24f),
                "WASD move   Mouse aim   LMB fire   R reload   Space dash   Tab scoreboard   F5 restart   M next mode", m_Small);
        }

        // ------------------------------------------------------------------ pieces

        void Box(Rect r, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, m_White);
            GUI.color = prev;
        }

        void DrawTerritory()
        {
            float w = 560f, x = Screen.width * 0.5f - w * 0.5f, y = 12f;
            Box(new Rect(x - 2, y - 2, w + 4, 26f), new Color(0f, 0f, 0f, 0.6f));
            float cx = x;
            for (int t = 0; t < match.CurrentShare.Length; t++)
            {
                float sw = w * match.CurrentShare[t];
                if (sw <= 0.5f) continue;
                Box(new Rect(cx, y, sw, 22f), match.TeamColor(t));
                cx += sw;
            }
            Box(new Rect(cx, y, x + w - cx, 22f), new Color(0.4f, 0.42f, 0.48f, 0.8f));

            int secs = Mathf.CeilToInt(Mathf.Max(0f, match.PhaseTimeLeft));
            bool last = match.PhaseIndex == match.phases.Length - 1;
            string banner = match.CurrentPhase.name + "  " + (match.PhaseIndex + 1) + "/" + match.phases.Length + "   " + (secs / 60) + ":" + (secs % 60).ToString("00") +
                            (match.CurrentPhase.scoreMultiplier > 1f ? "   score x" + match.CurrentPhase.scoreMultiplier.ToString("0.##") : "");
            var bs = new GUIStyle(m_Label) { alignment = TextAnchor.MiddleCenter };
            bs.normal.textColor = last ? new Color(1f, 0.55f, 0.3f) : Color.white;
            GUI.Label(new Rect(x, y + 28f, w, 24f), banner, bs);

            for (int t = 0; t < match.CurrentShare.Length; t++)
            {
                float rowY = y + 54f + t * 20f;
                Box(new Rect(x, rowY + 3f, 12f, 12f), match.TeamColor(t));
                GUI.Label(new Rect(x + 18f, rowY - 2f, w, 22f),
                    match.TeamName(t) + "   score " + match.TeamScore(t) + "   (flags +" + Mathf.RoundToInt(match.TeamIncome[t]) + ")   holding " + Mathf.RoundToInt(match.CurrentShare[t] * 100f) + "%", m_Small);
            }

            if (match.DominatingTeam >= 0 && match.State == MatchManager.MatchState.Playing)
            {
                float left = Mathf.Max(0f, match.dominationSeconds - match.DominationTimer);
                GUI.Label(new Rect(x, y + 56f + match.CurrentShare.Length * 20f, w, 24f),
                    match.TeamName(match.DominatingTeam) + " dominates: wins in " + Mathf.CeilToInt(left) + "s", m_Label);
            }
        }

        void DrawLocalStatus()
        {
            if (match.LocalSlot < 0) return;
            TankUnit u = match.Tanks[match.LocalSlot];
            PlayerStats s = match.Players[match.LocalSlot];
            Box(new Rect(14f, 14f, 232f, 24f), new Color(0f, 0f, 0f, 0.55f));
            Box(new Rect(16f, 16f, 228f * u.Hp / u.maxHp, 20f), Color.Lerp(new Color(0.9f, 0.2f, 0.15f), new Color(0.3f, 0.85f, 0.35f), (float)u.Hp / u.maxHp));
            GUI.Label(new Rect(20f, 13f, 220f, 26f), "HP " + u.Hp, m_Label);
            string ammo = u.weapon.displayName + (u.IsReloading ? "  RELOADING " + Mathf.RoundToInt(u.ReloadProgress * 100f) + "%" : "  " + u.Ammo + (u.weapon.limitedAmmo ? " shots" : "/" + u.MagazineSize));
            GUI.Label(new Rect(14f, 42f, 360f, 24f), ammo, m_Label);
            float by = 108f;
            for (int c = 0; c < u.Cores.Count; c++) { GUI.Label(new Rect(14f, by, 320f, 20f), "Core: " + u.Cores[c].name, m_Small); by += 18f; }
            if (u.ShieldHp > 0) { GUI.Label(new Rect(14f, by, 300f, 20f), "Shield " + u.ShieldHp + "  " + Mathf.CeilToInt(u.ShieldTimeLeft) + "s", m_Small); by += 18f; }
            if (u.SpeedTimeLeft > 0f) { GUI.Label(new Rect(14f, by, 300f, 20f), "Speed boost  " + Mathf.CeilToInt(u.SpeedTimeLeft) + "s", m_Small); by += 18f; }
            if (u.DamageTimeLeft > 0f) { GUI.Label(new Rect(14f, by, 300f, 20f), "Damage boost  " + Mathf.CeilToInt(u.DamageTimeLeft) + "s", m_Small); by += 18f; }
            if (u.Skill != null)
            {
                Box(new Rect(16f, 70f, 120f, 10f), new Color(0f, 0f, 0f, 0.55f));
                Box(new Rect(16f, 70f, 120f * u.Skill.Ready01, 10f), u.Skill.Ready01 >= 1f ? new Color(0.4f, 0.9f, 1f) : new Color(0.5f, 0.55f, 0.6f));
                GUI.Label(new Rect(142f, 62f, 200f, 24f), u.Skill.displayName + (u.Skill.Ready01 >= 1f ? " ready" : ""), m_Small);
            }
            GUI.Label(new Rect(14f, 88f, 320f, 24f), "K " + s.kills + "  D " + s.deaths + "  A " + s.assists + "  Cap " + s.captures + "  Score " + s.score, m_Small);
        }

        void DrawPointMarkers()
        {
            if (cam == null) return;
            foreach (ControlPoint cp in match.Layout.controlPoints)
            {
                Vector3 sp = cam.WorldToScreenPoint(cp.transform.position);
                bool behind = sp.z < 0f;
                float x = sp.x, y = Screen.height - sp.y;
                const float pad = 36f;
                bool inside = !behind && x > pad && x < Screen.width - pad && y > pad && y < Screen.height - pad;
                Color c = cp.Owner >= 0 ? match.TeamColor(cp.Owner) : new Color(0.62f, 0.64f, 0.7f);
                if (inside) continue;                                   // on screen: the zone itself shows ownership
                if (behind) { x = Screen.width - x; y = Screen.height - y; }
                x = Mathf.Clamp(x, pad, Screen.width - pad);
                y = Mathf.Clamp(y, pad, Screen.height - pad);
                float pulse = cp.Contested ? 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 10f) : 1f;
                Box(new Rect(x - 17f, y - 17f, 34f, 34f), new Color(0f, 0f, 0f, 0.7f));
                Box(new Rect(x - 15f, y - 15f, 30f, 30f), new Color(c.r, c.g, c.b, pulse));
                GUI.Label(new Rect(x - 9f, y - 13f, 30f, 28f), cp.label, m_Label);
            }
        }



        // ------------------------------------------------------------------ core picking

        static Rect CardRect(int i)
        {
            const float w = 260f, h = 210f, gap = 26f;
            float x0 = (Screen.width - (3f * w + 2f * gap)) * 0.5f;
            return new Rect(x0 + i * (w + gap), Screen.height * 0.5f - h * 0.5f + 20f, w, h);
        }

        void Update()
        {
            if (match == null) return;
            CoreDef[] offers = match.LocalOffers;
            if (offers == null) return;
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (kb != null)
            {
                if (kb.digit1Key.wasPressedThisFrame && offers.Length > 0) match.PickCore(0);
                else if (kb.digit2Key.wasPressedThisFrame && offers.Length > 1) match.PickCore(1);
                else if (kb.digit3Key.wasPressedThisFrame && offers.Length > 2) match.PickCore(2);
            }
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && match.LocalOffers != null)
            {
                Vector2 m = mouse.position.ReadValue();
                Vector2 p = new Vector2(m.x, Screen.height - m.y);
                for (int i = 0; i < offers.Length; i++) if (CardRect(i).Contains(p)) { match.PickCore(i); break; }
            }
        }

        void DrawPicking()
        {
            Box(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.6f));
            bool last = match.PhaseIndex == match.phases.Length - 1;
            GUI.Label(new Rect(0f, Screen.height * 0.5f - 190f, Screen.width, 50f),
                match.CurrentPhase.name + (last ? "  -  score x" + match.CurrentPhase.scoreMultiplier.ToString("0.##") + ", faster" : ""), m_Big);
            CoreDef[] offers = match.LocalOffers;
            if (offers == null)
            {
                GUI.Label(new Rect(0f, Screen.height * 0.5f - 20f, Screen.width, 30f), "Waiting...", m_Big);
                return;
            }
            GUI.Label(new Rect(0f, Screen.height * 0.5f - 140f, Screen.width, 30f), "Choose a core (1 / 2 / 3 or click)   " + Mathf.CeilToInt(match.PickTimeLeft) + "s",
                new GUIStyle(m_Label) { alignment = TextAnchor.MiddleCenter });
            Vector2 mouse = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
            Vector2 mp = new Vector2(mouse.x, Screen.height - mouse.y);
            var wrap = new GUIStyle(m_Small) { wordWrap = true, fontSize = 15, alignment = TextAnchor.UpperLeft };
            for (int i = 0; i < offers.Length; i++)
            {
                Rect r = CardRect(i);
                bool hover = r.Contains(mp);
                Box(new Rect(r.x - 3f, r.y - 3f, r.width + 6f, r.height + 6f), hover ? Color.white : new Color(1f, 1f, 1f, 0.15f));
                Box(r, new Color(0.08f, 0.1f, 0.14f, 0.97f));
                Box(new Rect(r.x, r.y, r.width, 8f), offers[i].color);
                GUI.Label(new Rect(r.x + 14f, r.y + 18f, r.width - 28f, 30f), "[" + (i + 1) + "]  " + offers[i].name, m_Label);
                GUI.Label(new Rect(r.x + 14f, r.y + 62f, r.width - 28f, r.height - 70f), offers[i].description, wrap);
            }
            // what the tank already has
            TankUnit u = match.Tanks[match.LocalSlot];
            if (u.Cores.Count > 0)
            {
                string have = "Your build:  ";
                for (int c = 0; c < u.Cores.Count; c++) have += (c > 0 ? ", " : "") + u.Cores[c].name;
                GUI.Label(new Rect(0f, Screen.height * 0.5f + 250f, Screen.width, 26f), have, new GUIStyle(m_Small) { alignment = TextAnchor.MiddleCenter });
            }
        }

        // ------------------------------------------------------------------ minimap

        void EnsureDisc()
        {
            if (m_Disc != null) return;
            const int n = 48;
            m_Disc = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    m_Disc.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01((1f - Mathf.Sqrt(dx * dx + dy * dy)) * n * 0.5f)));
                }
            m_Disc.Apply();
        }

        void Disc(Vector2 c, float radius, Color col)
        {
            Color prev = GUI.color;
            GUI.color = col;
            GUI.DrawTexture(new Rect(c.x - radius, c.y - radius, radius * 2f, radius * 2f), m_Disc);
            GUI.color = prev;
        }

        void DrawMinimap()
        {
            EnsureDisc();
            MapLayout layout = match.Layout;
            const float size = 230f;
            Rect r = new Rect(Screen.width - size - 14f, Screen.height - size - 40f, size, size);
            float half = layout.arenaHalfSize + 2f;
            float k = size / (2f * half);
            Box(new Rect(r.x - 2f, r.y - 2f, size + 4f, size + 4f), new Color(0.6f, 0.65f, 0.75f, 0.6f));
            Box(r, new Color(0.03f, 0.05f, 0.08f, 0.88f));

            if (layout.minimapBlocks != null)
                foreach (Transform b in layout.minimapBlocks)
                {
                    if (b == null) continue;
                    Vector3 p = b.position, sc = b.lossyScale;
                    Box(new Rect(r.x + (p.x - sc.x * 0.5f + half) * k, r.y + (half - p.z - sc.z * 0.5f) * k, Mathf.Max(1.5f, sc.x * k), Mathf.Max(1.5f, sc.z * k)), new Color(0.38f, 0.4f, 0.46f, 0.95f));
                }

            foreach (Pickup pk in layout.pickups)
            {
                if (!pk.Available) continue;
                Vector2 c = new Vector2(r.x + (pk.transform.position.x + half) * k, r.y + (half - pk.transform.position.z) * k);
                Box(new Rect(c.x - 2.5f, c.y - 2.5f, 5f, 5f), pk.color);
            }

            foreach (ControlPoint cp in layout.controlPoints)
            {
                Vector2 c = new Vector2(r.x + (cp.transform.position.x + half) * k, r.y + (half - cp.transform.position.z) * k);
                float rad = cp.radius * k;
                Color col = cp.Owner >= 0 ? match.TeamColor(cp.Owner) : new Color(0.6f, 0.62f, 0.68f);
                Disc(c, rad, new Color(col.r, col.g, col.b, 0.18f));
                float level = cp.Owner >= 0 ? cp.Model.OwnerHold : cp.Model.CapProgress;
                if (cp.Owner < 0 && cp.Model.Capturer >= 0) col = match.TeamColor(cp.Model.Capturer);
                Disc(c, rad * Mathf.Clamp01(level), new Color(col.r, col.g, col.b, 0.75f));
                if (cp.Contested) Disc(c, rad * 1.15f, new Color(1f, 0.9f, 0.3f, 0.35f + 0.3f * Mathf.Sin(Time.unscaledTime * 10f)));
                GUI.Label(new Rect(c.x - 5f, c.y - 10f, 24f, 22f), cp.label, m_Small);
            }

            for (int i = 0; i < match.Tanks.Length; i++)
            {
                TankUnit t = match.Tanks[i];
                if (t.IsDead) continue;
                Vector2 c = new Vector2(r.x + (t.transform.position.x + half) * k, r.y + (half - t.transform.position.z) * k);
                bool local = i == match.LocalSlot;
                if (local) Box(new Rect(c.x - 6f, c.y - 6f, 12f, 12f), Color.white);
                Box(new Rect(c.x - (local ? 4f : 3f), c.y - (local ? 4f : 3f), local ? 8f : 6f, local ? 8f : 6f), match.TeamColor(t.team));
            }

            // what the camera currently shows
            if (cam != null)
            {
                Plane ground = new Plane(Vector3.up, Vector3.zero);
                float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
                bool ok = true;
                for (int i = 0; i < 4 && ok; i++)
                {
                    Ray ray = cam.ViewportPointToRay(new Vector3(i & 1, i >> 1, 0f));
                    if (!ground.Raycast(ray, out float d)) { ok = false; break; }
                    Vector3 p = ray.GetPoint(d);
                    minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
                }
                if (ok)
                {
                    Rect v = new Rect(r.x + (minX + half) * k, r.y + (half - maxZ) * k, (maxX - minX) * k, (maxZ - minZ) * k);
                    Color vc = new Color(1f, 1f, 1f, 0.5f);
                    Box(new Rect(v.x, v.y, v.width, 1f), vc); Box(new Rect(v.x, v.yMax - 1f, v.width, 1f), vc);
                    Box(new Rect(v.x, v.y, 1f, v.height), vc); Box(new Rect(v.xMax - 1f, v.y, 1f, v.height), vc);
                }
            }
        }

        void DrawLog()
        {
            float y = Screen.height * 0.5f - 40f;
            for (int i = 0; i < MatchManager.LogSize; i++)
            {
                int idx = match.LogIndexFromNewest(i);
                if (match.Log[idx] == null || Time.time - match.LogTime[idx] > 7f) continue;
                GUI.Label(new Rect(14f, y, 520f, 22f), match.Log[idx], m_Small);
                y += 18f;
            }
        }

        void DrawRespawn()
        {
            if (match.LocalSlot < 0 || !match.Tanks[match.LocalSlot].IsDead || match.State != MatchManager.MatchState.Playing) return;
            GUI.Label(new Rect(0f, Screen.height * 0.4f, Screen.width, 50f), "DESTROYED   respawn in " + Mathf.CeilToInt(match.LocalRespawnIn), m_Big);
        }

        void DrawEnd()
        {
            Box(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.55f));
            string title = match.IsDraw ? "DRAW" : match.TeamName(match.WinnerTeam).ToUpperInvariant() + " WINS";
            GUI.Label(new Rect(0f, 70f, Screen.width, 50f), title, m_Big);
            GUI.Label(new Rect(0f, 112f, Screen.width, 24f), match.EndReason + "  -  highest total score wins", new GUIStyle(m_Small) { alignment = TextAnchor.MiddleCenter });
            for (int t = 0; t < match.AverageShare.Length; t++)
            {
                float y = 146f + t * 22f;
                Box(new Rect(Screen.width * 0.5f - 200f, y + 4f, 12f, 12f), match.TeamColor(t));
                GUI.Label(new Rect(Screen.width * 0.5f - 180f, y - 1f, 560f, 22f),
                    match.TeamName(t) + "   score " + match.TeamScore(t) + "   (flags +" + Mathf.RoundToInt(match.TeamIncome[t]) + ", players " + match.CombatScore(t) + ")   held " + Mathf.RoundToInt(match.AverageShare[t] * 100f) + "% of the match", m_Label);
            }
            DrawScoreboard(new Rect(Screen.width * 0.5f - 300f, 146f + match.AverageShare.Length * 22f + 16f, 600f, 260f), true);
            GUI.Label(new Rect(0f, Screen.height - 70f, Screen.width, 30f), "F5 restart   M next mode", new GUIStyle(m_Label) { alignment = TextAnchor.MiddleCenter });
        }

        void DrawScoreboard(Rect r, bool background)
        {
            if (background) Box(r, new Color(0f, 0f, 0f, 0.7f));
            m_Order.Clear();
            for (int i = 0; i < match.Players.Length; i++) m_Order.Add(i);
            m_Order.Sort((a, b) => match.Players[b].score.CompareTo(match.Players[a].score));
            float y = r.y + 8f;
            GUI.Label(new Rect(r.x + 14f, y, r.width, 22f), "Player                 K   D   A   Cap  Def  Con  Score", m_Small);
            y += 24f;
            for (int n = 0; n < m_Order.Count; n++)
            {
                PlayerStats p = match.Players[m_Order[n]];
                Box(new Rect(r.x + 8f, y + 4f, 8f, 14f), match.TeamColor(p.team));
                string line = (n == 0 ? "MVP " : "     ") + Pad(p.name, 14) + Pad(p.kills.ToString(), 4) + Pad(p.deaths.ToString(), 4) + Pad(p.assists.ToString(), 4) +
                              Pad(p.captures.ToString(), 5) + Pad(p.defenses.ToString(), 5) + Pad(p.contests.ToString(), 5) + p.score;
                GUI.Label(new Rect(r.x + 22f, y, r.width, 22f), line, m_Small);
                y += 22f;
            }
        }

        static string Pad(string s, int width) { return s.Length >= width ? s : s + new string(' ', width - s.Length); }
    }
}
