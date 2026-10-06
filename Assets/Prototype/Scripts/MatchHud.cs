using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TankGame.Prototype
{
    /// <summary>
    /// Throwaway IMGUI HUD, laid out by HudLayout in a fixed reference height so every screen gets the same arrangement:
    /// status top-left, score bar top-centre, minimap top-right, thumbs at the bottom corners, Dash/Reload on the right edge.
    /// IMGUI allocates every frame; replace it when real UI work starts.
    /// </summary>
    public sealed class MatchHud : MonoBehaviour
    {
        public MatchManager match;
        public Camera cam;

        const float RefHeightPhone = 440f, RefHeightDesktop = 720f;

        bool Mobile => match != null && match.UseTouch;
        float S => Screen.height / (Mobile ? RefHeightPhone : RefHeightDesktop);
        float W => Screen.width / S;
        float H => Screen.height / S;

        HudLayout m_Layout;
        GUIStyle m_Label, m_Small, m_Tiny, m_Big, m_Center, m_Wrap;
        Texture2D m_White, m_Disc;
        float m_FpsSmooth = 60f;
        readonly List<int> m_Order = new List<int>();

        // ------------------------------------------------------------------ layout and input

        Rect SafeVirtual()
        {
            Rect sa = Screen.safeArea;
            float s = S;
            return new Rect(sa.x / s, (Screen.height - sa.yMax) / s, sa.width / s, sa.height / s);
        }

        Vector2 ToPx(Vector2 virt) { return new Vector2(virt.x * S, Screen.height - virt.y * S); }
        Vector2 ToVirtual(Vector2 px) { return new Vector2(px.x / S, (Screen.height - px.y) / S); }

        void Update()
        {
            if (match == null || match.Players == null) return;
            m_Layout = HudLayout.Compute(W, H, SafeVirtual(), Mobile);
            m_FpsSmooth = Mathf.Lerp(m_FpsSmooth, 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime), 0.05f);

            // what is drawn is what is touched
            MobileTankInput mi = match.mobileInput;
            if (mi != null)
            {
                mi.DashCenter = ToPx(m_Layout.dashC); mi.DashRadius = m_Layout.dashR * S;
                mi.ReloadCenter = ToPx(m_Layout.reloadC); mi.ReloadRadius = m_Layout.reloadR * S;
            }

            Pointer ptr = Pointer.current;
            bool pressed = ptr != null && ptr.press.wasPressedThisFrame;
            Vector2 p = pressed ? ToVirtual(ptr.position.ReadValue()) : Vector2.zero;

            CoreDef[] offers = match.LocalOffers;
            if (offers != null)
            {
                Keyboard kb = Keyboard.current;
                if (kb != null)
                {
                    if (kb.digit1Key.wasPressedThisFrame && offers.Length > 0) match.PickCore(0);
                    else if (kb.digit2Key.wasPressedThisFrame && offers.Length > 1) match.PickCore(1);
                    else if (kb.digit3Key.wasPressedThisFrame && offers.Length > 2) match.PickCore(2);
                }
                if (pressed && match.LocalOffers != null)
                    for (int i = 0; i < offers.Length; i++) if (CardRect(i).Contains(p)) { match.PickCore(i); break; }
            }
            else if (match.State == MatchManager.MatchState.Ended && pressed)
            {
                if (EndButton(0).Contains(p)) match.Restart();
                else if (EndButton(1).Contains(p)) match.NextMode();
            }
        }

        // ------------------------------------------------------------------ drawing

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || match == null || match.Players == null || match.Layout == null) return;
            if (m_Label == null) MakeStyles();
            EnsureDisc();
            Matrix4x4 saved = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(S, S, 1f));
            HudLayout L = m_Layout;
            if (L.W <= 0f) L = HudLayout.Compute(W, H, SafeVirtual(), Mobile);

            bool overlay = match.State != MatchManager.MatchState.Playing;
            if (overlay)
            {
                // core pick and results are full-screen: nothing else may draw underneath and clash with them
                if (match.State == MatchManager.MatchState.Picking) DrawPicking(); else DrawEnd();
            }
            else
            {
                DrawScoreBar(L);
                DrawStatus(L);
                DrawMinimap(L.minimap);
                DrawFlagMarkers(L);
                DrawLog(L);
                DrawRespawn();
                if (Mobile) DrawTouchControls(L);
                DrawStats(L);
                Keyboard kb = Keyboard.current;
                if (!Mobile && kb != null && kb.tabKey.isPressed) DrawScoreboard(new Rect(W * 0.5f - 300f, 130f, 600f, 260f));
                if (!Mobile)
                    GUI.Label(new Rect(L.safe.x + 10f, L.safe.yMax - 24f, W - 40f, 20f), "WASD move   Mouse aim   LMB fire   R reload   Space dash   Tab scoreboard   F5 restart   M next mode", m_Tiny);
            }
            GUI.matrix = saved;
        }

        void MakeStyles()
        {
            m_White = Texture2D.whiteTexture;
            m_Label = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, wordWrap = false };
            m_Label.normal.textColor = Color.white;
            m_Small = new GUIStyle(m_Label) { fontSize = 12, fontStyle = FontStyle.Normal };
            m_Tiny = new GUIStyle(m_Small) { fontSize = 11 };
            m_Big = new GUIStyle(m_Label) { fontSize = 30, alignment = TextAnchor.MiddleCenter };
            m_Center = new GUIStyle(m_Small) { alignment = TextAnchor.MiddleCenter };
            m_Wrap = new GUIStyle(m_Small) { wordWrap = true, fontSize = 14, alignment = TextAnchor.UpperLeft };
        }

        void Box(Rect r, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, m_White);
            GUI.color = prev;
        }

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

        static string Short(string s, int n) { return s.Length <= n ? s : s.Substring(0, n); }

        // ---- top centre: phase, clock, one chip per team, thin territory bar

        void DrawScoreBar(HudLayout L)
        {
            Rect b = L.scoreBar;
            Box(b, new Color(0f, 0f, 0f, 0.58f));
            bool last = match.PhaseIndex == match.phases.Length - 1;
            int secs = Mathf.CeilToInt(Mathf.Max(0f, match.PhaseTimeLeft));
            string head = match.CurrentPhase.name + "  " + (match.PhaseIndex + 1) + "/" + match.phases.Length + "   " + (secs / 60) + ":" + (secs % 60).ToString("00") +
                          (match.CurrentPhase.scoreMultiplier > 1f ? "   x" + match.CurrentPhase.scoreMultiplier.ToString("0.##") : "");
            var hs = new GUIStyle(m_Small) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            hs.normal.textColor = last ? new Color(1f, 0.6f, 0.3f) : Color.white;
            GUI.Label(new Rect(b.x, b.y, b.width, 18f), head, hs);

            int n = match.CurrentShare.Length;
            float gap = 3f, cw = (b.width - 6f - gap * (n - 1)) / n;
            int localTeam = match.LocalSlot >= 0 ? match.Players[match.LocalSlot].team : -1;
            for (int t = 0; t < n; t++)
            {
                Rect c = new Rect(b.x + 3f + t * (cw + gap), b.y + 18f, cw, 22f);
                Color tc = match.TeamColor(t);
                if (t == localTeam) Box(new Rect(c.x - 1.5f, c.y - 1.5f, c.width + 3f, c.height + 3f), Color.white);
                Box(c, new Color(tc.r * 0.75f, tc.g * 0.75f, tc.b * 0.75f, 0.95f));
                GUI.Label(c, Short(match.TeamName(t), n > 3 ? 5 : 8) + " " + match.TeamScore(t), m_Center);
            }

            float x = b.x + 3f, tw = b.width - 6f;
            for (int t = 0; t < n; t++)
            {
                float sw = tw * match.CurrentShare[t];
                if (sw <= 0.5f) continue;
                Box(new Rect(x, b.y + 41f, sw, 4f), match.TeamColor(t));
                x += sw;
            }
            Box(new Rect(x, b.y + 41f, b.x + 3f + tw - x, 4f), new Color(0.4f, 0.42f, 0.48f, 0.8f));
        }

        // ---- top left: the player's own card, buffs and build underneath

        void DrawStatus(HudLayout L)
        {
            if (match.LocalSlot < 0) return;
            TankUnit u = match.Tanks[match.LocalSlot];
            PlayerStats s = match.Players[match.LocalSlot];
            Rect r = L.status;
            Box(r, new Color(0f, 0f, 0f, 0.58f));

            Rect hp = new Rect(r.x + 6f, r.y + 6f, r.width - 12f, 20f);
            Box(hp, new Color(0f, 0f, 0f, 0.6f));
            float f = (float)u.Hp / u.maxHp;
            Box(new Rect(hp.x, hp.y, hp.width * f, hp.height), Color.Lerp(new Color(0.9f, 0.2f, 0.15f), new Color(0.3f, 0.85f, 0.35f), f));
            GUI.Label(hp, "HP " + u.Hp + " / " + u.maxHp, m_Center);

            string ammo = u.weapon.displayName + (u.IsReloading ? "  RELOADING " + Mathf.RoundToInt(u.ReloadProgress * 100f) + "%" : "  " + u.Ammo + (u.weapon.limitedAmmo ? " shots" : " / " + u.MagazineSize));
            GUI.Label(new Rect(r.x + 6f, r.y + 29f, r.width - 12f, 18f), ammo, m_Small);
            string dash = u.Skill == null ? "" : (u.Skill.Ready01 >= 1f ? "   Dash ready" : "   Dash " + Mathf.CeilToInt(u.Skill.CooldownRemaining) + "s");
            GUI.Label(new Rect(r.x + 6f, r.y + 49f, r.width - 12f, 18f), "K " + s.kills + "  D " + s.deaths + "  A " + s.assists + "   " + s.score + " pts" + (Mobile ? "" : dash), m_Tiny);

            float y = r.yMax + 6f;
            if (u.ShieldHp > 0) { GUI.Label(new Rect(r.x, y, r.width, 16f), "Shield " + u.ShieldHp + "  " + Mathf.CeilToInt(u.ShieldTimeLeft) + "s", m_Tiny); y += 15f; }
            if (u.SpeedTimeLeft > 0f) { GUI.Label(new Rect(r.x, y, r.width, 16f), "Speed x" + u.SpeedMultiplier.ToString("0.#") + "  " + Mathf.CeilToInt(u.SpeedTimeLeft) + "s", m_Tiny); y += 15f; }
            if (u.DamageTimeLeft > 0f) { GUI.Label(new Rect(r.x, y, r.width, 16f), "Damage x" + u.DamageMultiplier.ToString("0.#") + "  " + Mathf.CeilToInt(u.DamageTimeLeft) + "s", m_Tiny); y += 15f; }
            for (int c = 0; c < u.Cores.Count; c++)
            {
                Box(new Rect(r.x, y + 3f, 3f, 10f), u.Cores[c].color);
                GUI.Label(new Rect(r.x + 7f, y, r.width, 16f), u.Cores[c].name, m_Tiny);
                y += 15f;
            }
        }

        // ---- events

        void DrawLog(HudLayout L)
        {
            float y = L.log.y;
            int shown = 0;
            for (int i = 0; i < MatchManager.LogSize && shown < 3; i++)
            {
                int idx = match.LogIndexFromNewest(i);
                if (match.Log[idx] == null) continue;
                float age = Time.time - match.LogTime[idx];
                if (age > 7f) continue;
                var st = new GUIStyle(m_Tiny) { alignment = TextAnchor.MiddleCenter };
                st.normal.textColor = new Color(1f, 1f, 1f, Mathf.Clamp01(1f - (age - 4f) / 3f));
                GUI.Label(new Rect(L.log.x, y, L.log.width, 16f), match.Log[idx], st);
                y += 16f;
                shown++;
            }
        }

        void DrawRespawn()
        {
            if (match.LocalSlot < 0 || !match.Tanks[match.LocalSlot].IsDead || match.State != MatchManager.MatchState.Playing) return;
            GUI.Label(new Rect(0f, H * 0.42f, W, 44f), "DESTROYED   respawn in " + Mathf.CeilToInt(match.LocalRespawnIn), m_Big);
        }

        void DrawStats(HudLayout L)
        {
            // frame rate: always shown on a phone while the prototype is being tuned
            if (!Mobile) return;
            GUI.Label(new Rect(L.W * 0.5f - 70f, L.safe.yMax - 20f, 140f, 18f), Mathf.RoundToInt(m_FpsSmooth) + " fps   " + (1000f / Mathf.Max(1f, m_FpsSmooth)).ToString("0.0") + " ms", m_Center);
        }

        // ---- flags that are off screen

        void DrawFlagMarkers(HudLayout L)
        {
            if (cam == null) return;
            Rect[] reserved = L.Reserved();
            float lo = 22f;
            Rect area = new Rect(L.safe.x + lo, L.safe.y + lo, L.safe.width - lo * 2f, L.safe.height - lo * 2f);
            foreach (ControlPoint cp in match.Layout.controlPoints)
            {
                Vector3 sp = cam.WorldToScreenPoint(cp.transform.position);
                bool behind = sp.z < 0f;
                Vector2 v = ToVirtual(new Vector2(sp.x, sp.y));
                if (!behind && area.Contains(v)) continue;                   // on screen: the zone itself shows ownership
                if (behind) v = new Vector2(W - v.x, H - v.y);
                v = new Vector2(Mathf.Clamp(v.x, area.x, area.xMax), Mathf.Clamp(v.y, area.y, area.yMax));
                Rect mk = new Rect(v.x - 15f, v.y - 15f, 30f, 30f);
                bool blocked = false;
                foreach (Rect rs in reserved) if (rs.Overlaps(mk)) { blocked = true; break; }
                if (blocked) continue;                                        // the minimap already shows it
                Color c = cp.Owner >= 0 ? match.TeamColor(cp.Owner) : new Color(0.62f, 0.64f, 0.7f);
                float pulse = cp.Contested ? 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 10f) : 1f;
                Box(new Rect(mk.x - 2f, mk.y - 2f, 34f, 34f), new Color(0f, 0f, 0f, 0.7f));
                Box(mk, new Color(c.r, c.g, c.b, pulse));
                GUI.Label(mk, cp.label, new GUIStyle(m_Label) { alignment = TextAnchor.MiddleCenter });
            }
        }

        // ---- minimap (top right)

        void DrawMinimap(Rect r)
        {
            MapLayout layout = match.Layout;
            float half = layout.arenaHalfSize + 2f;
            float k = r.width / (2f * half);
            Box(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), new Color(0.6f, 0.65f, 0.75f, 0.6f));
            Box(r, new Color(0.03f, 0.05f, 0.08f, 0.9f));

            if (layout.minimapBlocks != null)
                foreach (Transform b in layout.minimapBlocks)
                {
                    if (b == null) continue;
                    Vector3 p = b.position, sc = b.lossyScale;
                    Box(new Rect(r.x + (p.x - sc.x * 0.5f + half) * k, r.y + (half - p.z - sc.z * 0.5f) * k, Mathf.Max(1.2f, sc.x * k), Mathf.Max(1.2f, sc.z * k)), new Color(0.38f, 0.4f, 0.46f, 0.95f));
                }

            foreach (Pickup pk in layout.pickups)
            {
                if (!pk.Available) continue;
                Vector2 c = new Vector2(r.x + (pk.transform.position.x + half) * k, r.y + (half - pk.transform.position.z) * k);
                Box(new Rect(c.x - 2f, c.y - 2f, 4f, 4f), pk.color);
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
                GUI.Label(new Rect(c.x - 5f, c.y - 8f, 20f, 18f), cp.label, m_Tiny);
            }

            for (int i = 0; i < match.Tanks.Length; i++)
            {
                TankUnit t = match.Tanks[i];
                if (t.IsDead) continue;
                Vector2 c = new Vector2(r.x + (t.transform.position.x + half) * k, r.y + (half - t.transform.position.z) * k);
                bool local = i == match.LocalSlot;
                if (local) Box(new Rect(c.x - 5f, c.y - 5f, 10f, 10f), Color.white);
                Box(new Rect(c.x - (local ? 3.5f : 2.5f), c.y - (local ? 3.5f : 2.5f), local ? 7f : 5f, local ? 7f : 5f), match.TeamColor(t.team));
            }

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
                    v.xMin = Mathf.Max(v.xMin, r.x); v.yMin = Mathf.Max(v.yMin, r.y); v.xMax = Mathf.Min(v.xMax, r.xMax); v.yMax = Mathf.Min(v.yMax, r.yMax);
                    Color vc = new Color(1f, 1f, 1f, 0.5f);
                    Box(new Rect(v.x, v.y, v.width, 1f), vc); Box(new Rect(v.x, v.yMax - 1f, v.width, 1f), vc);
                    Box(new Rect(v.x, v.y, 1f, v.height), vc); Box(new Rect(v.xMax - 1f, v.y, 1f, v.height), vc);
                }
            }
        }

        // ---- touch controls (bottom corners and right edge)

        void DrawTouchControls(HudLayout L)
        {
            MobileTankInput m = match.mobileInput;
            if (m == null || !m.enabled || match.State != MatchManager.MatchState.Playing) return;
            float R = L.stickR;
            DrawStick(m.MoveActive ? ToVirtual(m.MoveOrigin) : L.moveHint, m.MoveActive ? ToVirtual(m.MoveNow) : L.moveHint, R, new Color(1f, 1f, 1f, m.MoveActive ? 0.34f : 0.12f), "MOVE");
            DrawStick(m.AimActive ? ToVirtual(m.AimOrigin) : L.aimHint, m.AimActive ? ToVirtual(m.AimNow) : L.aimHint, R,
                m.Firing ? new Color(1f, 0.5f, 0.3f, 0.55f) : new Color(1f, 1f, 1f, m.AimActive ? 0.34f : 0.12f), "AIM + FIRE");

            TankUnit u = match.LocalSlot >= 0 ? match.Tanks[match.LocalSlot] : null;
            float ready = u != null && u.Skill != null ? u.Skill.Ready01 : 1f;
            DrawButton(L.dashC, L.dashR, "DASH", ready, m.DashHeld, new Color(0.4f, 0.9f, 1f), u != null && u.Skill != null && ready < 1f ? Mathf.CeilToInt(u.Skill.CooldownRemaining).ToString() : null);
            float reload = u != null && u.IsReloading ? u.ReloadProgress : 1f;
            DrawButton(L.reloadC, L.reloadR, "RELOAD", reload, m.ReloadHeld, new Color(1f, 0.85f, 0.4f), null);
        }

        void DrawStick(Vector2 origin, Vector2 now, float radius, Color col, string label)
        {
            Disc(origin, radius, new Color(col.r, col.g, col.b, col.a * 0.5f));
            Vector2 d = Vector2.ClampMagnitude(now - origin, radius);
            Disc(origin + d, radius * 0.42f, new Color(col.r, col.g, col.b, Mathf.Min(1f, col.a * 2f)));
            GUI.Label(new Rect(origin.x - radius, origin.y - 9f - radius * 0.0f, radius * 2f, 18f), label, new GUIStyle(m_Tiny) { alignment = TextAnchor.MiddleCenter });
        }

        void DrawButton(Vector2 c, float r, string label, float fill, bool held, Color tint, string overlayText)
        {
            Disc(c, r, new Color(0f, 0f, 0f, 0.55f));
            if (fill > 0.001f)
            {
                // fills from the bottom as the cooldown / reload finishes
                GUI.BeginGroup(new Rect(c.x - r, c.y + r - 2f * r * fill, 2f * r, 2f * r * fill));
                Disc(new Vector2(r, r - (2f * r - 2f * r * fill)), r, new Color(tint.r, tint.g, tint.b, held ? 0.95f : (fill >= 1f ? 0.6f : 0.4f)));
                GUI.EndGroup();
            }
            GUI.Label(new Rect(c.x - r, c.y - 9f, 2f * r, 18f), overlayText ?? label, new GUIStyle(m_Tiny) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold });
        }

        // ---- full-screen overlays

        Rect CardRect(int i)
        {
            float w = Mathf.Min(260f, (W - 90f) / 3f), h = Mathf.Min(160f, H * 0.42f), gap = Mathf.Min(26f, W * 0.02f);
            float x0 = (W - (3f * w + 2f * gap)) * 0.5f;
            return new Rect(x0 + i * (w + gap), H * 0.5f - h * 0.5f + 20f, w, h);
        }

        void DrawPicking()
        {
            Box(new Rect(0f, 0f, W, H), new Color(0.02f, 0.03f, 0.05f, 0.9f));
            bool last = match.PhaseIndex == match.phases.Length - 1;
            GUI.Label(new Rect(0f, H * 0.5f - 150f, W, 44f), match.CurrentPhase.name + (last ? "  -  score x" + match.CurrentPhase.scoreMultiplier.ToString("0.##") + ", faster" : ""), m_Big);
            CoreDef[] offers = match.LocalOffers;
            if (offers == null)
            {
                GUI.Label(new Rect(0f, H * 0.5f - 20f, W, 40f), "Waiting...", m_Big);
                return;
            }
            GUI.Label(new Rect(0f, H * 0.5f - 104f, W, 24f), "Choose a core (" + (Mobile ? "tap" : "1 / 2 / 3 or click") + ")   " + Mathf.CeilToInt(match.PickTimeLeft) + "s", new GUIStyle(m_Label) { alignment = TextAnchor.MiddleCenter });
            Vector2 mp = Pointer.current != null ? ToVirtual(Pointer.current.position.ReadValue()) : Vector2.zero;
            for (int i = 0; i < offers.Length; i++)
            {
                Rect r = CardRect(i);
                bool hover = !Mobile && r.Contains(mp);
                Box(new Rect(r.x - 3f, r.y - 3f, r.width + 6f, r.height + 6f), hover ? Color.white : new Color(1f, 1f, 1f, 0.15f));
                Box(r, new Color(0.08f, 0.1f, 0.14f, 0.97f));
                Box(new Rect(r.x, r.y, r.width, 8f), offers[i].color);
                GUI.Label(new Rect(r.x + 12f, r.y + 16f, r.width - 24f, 26f), (Mobile ? "" : "[" + (i + 1) + "]  ") + offers[i].name, m_Label);
                GUI.Label(new Rect(r.x + 12f, r.y + 54f, r.width - 24f, r.height - 62f), offers[i].description, m_Wrap);
            }
            TankUnit u = match.Tanks[match.LocalSlot];
            if (u.Cores.Count > 0)
            {
                string have = "Your build:  ";
                for (int c = 0; c < u.Cores.Count; c++) have += (c > 0 ? ", " : "") + u.Cores[c].name;
                GUI.Label(new Rect(0f, H * 0.5f + 110f, W, 22f), have, m_Center);
            }
        }

        Rect EndButton(int i) { return new Rect(W * 0.5f - 150f + i * 160f, H - 56f, 140f, 38f); }

        void DrawEnd()
        {
            Box(new Rect(0f, 0f, W, H), new Color(0.02f, 0.03f, 0.05f, 0.92f));
            string title = match.IsDraw ? "DRAW" : match.TeamName(match.WinnerTeam).ToUpperInvariant() + " WINS";
            GUI.Label(new Rect(0f, 14f, W, 44f), title, m_Big);
            GUI.Label(new Rect(0f, 56f, W, 20f), match.EndReason + "  -  highest total score wins", m_Center);
            float y = 82f;
            for (int t = 0; t < match.AverageShare.Length; t++)
            {
                Box(new Rect(W * 0.5f - 230f, y + 4f, 10f, 10f), match.TeamColor(t));
                GUI.Label(new Rect(W * 0.5f - 214f, y, 520f, 18f), match.TeamName(t) + "   " + match.TeamScore(t) + " pts   (flags +" + Mathf.RoundToInt(match.TeamIncome[t]) + ", players " + match.CombatScore(t) + ")   held " + Mathf.RoundToInt(match.AverageShare[t] * 100f) + "%", m_Small);
                y += 18f;
            }
            float tableH = Mathf.Min(H - y - 70f, 24f + match.Players.Length * 20f + 12f);
            DrawScoreboard(new Rect(W * 0.5f - 300f, y + 6f, 600f, tableH));
            for (int i = 0; i < 2; i++)
            {
                Rect b = EndButton(i);
                Box(new Rect(b.x - 2f, b.y - 2f, b.width + 4f, b.height + 4f), new Color(1f, 1f, 1f, 0.25f));
                Box(b, new Color(0.12f, 0.16f, 0.22f, 1f));
                GUI.Label(b, i == 0 ? "PLAY AGAIN" : "NEXT MODE", new GUIStyle(m_Label) { alignment = TextAnchor.MiddleCenter, fontSize = 14 });
            }
            if (!Mobile) GUI.Label(new Rect(0f, H - 20f, W, 18f), "F5 play again   M next mode", m_Center);
        }

        void DrawScoreboard(Rect r)
        {
            Box(r, new Color(0f, 0f, 0f, 0.72f));
            m_Order.Clear();
            for (int i = 0; i < match.Players.Length; i++) m_Order.Add(i);
            m_Order.Sort((a, b) => match.Players[b].score.CompareTo(match.Players[a].score));
            float y = r.y + 6f;
            GUI.Label(new Rect(r.x + 22f, y, r.width, 18f), "Player                 K   D   A   Cap  Def  Con  Score", m_Tiny);
            y += 20f;
            for (int n = 0; n < m_Order.Count; n++)
            {
                PlayerStats p = match.Players[m_Order[n]];
                Box(new Rect(r.x + 8f, y + 3f, 8f, 12f), match.TeamColor(p.team));
                string line = (n == 0 ? "MVP " : "     ") + Pad(p.name, 14) + Pad(p.kills.ToString(), 4) + Pad(p.deaths.ToString(), 4) + Pad(p.assists.ToString(), 4) +
                              Pad(p.captures.ToString(), 5) + Pad(p.defenses.ToString(), 5) + Pad(p.contests.ToString(), 5) + p.score;
                GUI.Label(new Rect(r.x + 22f, y, r.width, 18f), line, m_Tiny);
                y += 20f;
            }
        }

        static string Pad(string s, int width) { return s.Length >= width ? s : s + new string(' ', width - s.Length); }
    }
}
