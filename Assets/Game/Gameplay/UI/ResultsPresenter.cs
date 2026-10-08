using System.Collections.Generic;
using Reflex.Attributes;
using TMPro;
using Tank.Core.Combat;
using Tank.Core.Events;
using Tank.Core.Match;
using Tank.Gameplay.Config;
using Tank.Gameplay.Flow;
using UnityEngine;
using UnityEngine.UI;

namespace Tank.Gameplay.UI
{
    /// <summary>The end-of-match screen: who won, the final ranking and how long until the next match.</summary>
    public sealed class ResultsPresenter : MonoBehaviour
    {
        [SerializeField] CanvasGroup group;
        [SerializeField] TMP_Text title, subtitle, footer;
        [SerializeField] Image crown;
        [SerializeField] RectTransform rowsRoot;
        [SerializeField] RankingRowView rowPrefab;
        [SerializeField, Min(1)] int maxRows = 8;

        readonly List<RankingRowView> m_Rows = new List<RankingRowView>();
        LobbyState m_Lobby; IMatchQuery m_Match; IRankingQuery m_Ranking; ITankRegistry m_Tanks; ILocalPlayer m_Local; PlayerPalette m_Palette; MatchConfig m_Config; UiTheme m_Theme;
        float m_EndedAt; bool m_Shown, m_MatchEnded;

        [Inject]
        void Construct(IEventBus bus, IMatchQuery match, IRankingQuery ranking, ITankRegistry tanks, ILocalPlayer local, PlayerPalette palette, MatchConfig config, UiTheme theme, LobbyState lobby)
        {
            m_Lobby = lobby;
            m_Match = match; m_Ranking = ranking; m_Tanks = tanks; m_Local = local; m_Palette = palette; m_Config = config; m_Theme = theme;
            bus.Subscribe<MatchEnded>(e => { m_EndedAt = Time.unscaledTime; m_MatchEnded = true; });
            bus.Subscribe<MatchPhaseChanged>(e => { if (e.Phase != MatchPhase.Ended) m_MatchEnded = false; });
        }

        void LateUpdate()
        {
            if (m_Match == null) return;
            bool ended = m_MatchEnded && m_Match.Phase == MatchPhase.Ended && !m_Lobby.Active;       // only after a match really finished, never before the first one
            group.alpha = ended ? 1f : 0f; group.blocksRaycasts = ended; group.interactable = ended;
            if (!ended) { m_Shown = false; return; }
            if (!m_Shown) { m_Shown = true; Fill(); }
            footer.text = "Next match in " + Mathf.Max(0, Mathf.CeilToInt(m_Config.restartDelaySeconds - (Time.unscaledTime - m_EndedAt))) + "s";
        }

        void Fill()
        {
            int winner = m_Match.WinnerId;
            TankModel w = winner >= 0 ? m_Tanks.Find(winner) : null;
            bool mine = winner == m_Local.TankId;
            title.text = w == null ? "DRAW" : mine ? "YOU WIN!" : w.Name.ToUpperInvariant() + " WINS";
            title.color = w == null ? m_Theme.text : m_Palette.Get(w.ColorSlot).color;
            subtitle.text = w == null ? "Nobody came out ahead" : "KDA " + m_Ranking.Get(winner).Kda.ToString("0.00");
            crown.gameObject.SetActive(w != null);

            IReadOnlyList<PlayerStats> ranked = m_Ranking.Ranked;
            int count = Mathf.Min(maxRows, ranked.Count);
            while (m_Rows.Count < count) m_Rows.Add(Instantiate(rowPrefab, rowsRoot));
            for (int i = 0; i < m_Rows.Count; i++)
            {
                m_Rows[i].gameObject.SetActive(i < count);
                if (i >= count) continue;
                PlayerStats s = ranked[i]; TankModel t = m_Tanks.Find(s.Id);
                if (t == null) { m_Rows[i].gameObject.SetActive(false); continue; }
                m_Rows[i].Bind(i + 1, t.Name, m_Palette.Get(t.ColorSlot).color, s.Kills, s.Deaths, s.Assists, s.Kda, s.Id == m_Local.TankId, m_Theme.highlight);
            }
        }
    }
}
