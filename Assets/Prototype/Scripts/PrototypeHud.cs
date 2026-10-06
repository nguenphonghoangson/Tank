using UnityEngine;

namespace TankGame.Prototype
{
    /// <summary>Throwaway IMGUI debug HUD (HP, kills, controls, damage vignette). Separate so it can be switched off: IMGUI allocates every frame.</summary>
    public sealed class PrototypeHud : MonoBehaviour
    {
        public PrototypeGame game;

        string m_KillsText = "Kills 0", m_HpText = "";
        int m_LastKills = -1, m_LastHp = -1;
        GUIStyle m_Style;

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            TankUnit player = game.player;
            if (m_Style == null)
            {
                m_Style = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
                m_Style.normal.textColor = Color.white;
            }
            if (player.Hp != m_LastHp) { m_LastHp = player.Hp; m_HpText = "HP " + m_LastHp; }
            if (game.Kills != m_LastKills) { m_LastKills = game.Kills; m_KillsText = "Kills " + m_LastKills; }

            Color prev = GUI.color;
            if (game.DamageFlash > 0f)
            {
                GUI.color = new Color(1f, 0.1f, 0.05f, 0.35f * game.DamageFlash);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            }
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(14, 14, 220, 22), Texture2D.whiteTexture);
            GUI.color = Color.Lerp(new Color(0.9f, 0.2f, 0.15f), new Color(0.3f, 0.85f, 0.35f), (float)player.Hp / player.maxHp);
            GUI.DrawTexture(new Rect(16, 16, 216f * player.Hp / player.maxHp, 18), Texture2D.whiteTexture);
            GUI.color = prev;
            GUI.Label(new Rect(20, 12, 200, 26), m_HpText, m_Style);
            GUI.Label(new Rect(14, 40, 220, 26), m_KillsText, m_Style);
            GUI.Label(new Rect(14, Screen.height - 34, 700, 26), "WASD move   Mouse aim   LMB / Space fire   R reset", m_Style);
            if (player.IsDead) GUI.Label(new Rect(Screen.width * 0.5f - 90, Screen.height * 0.45f, 240, 30), "DESTROYED", m_Style);
        }
    }
}
