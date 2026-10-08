using System;
using System.Collections.Generic;
using Reflex.Attributes;
using Tank.Core.Combat;
using Tank.Core.Events;
using Tank.Core.Match;
using Tank.Gameplay.Config;
using UnityEngine;

namespace Tank.Gameplay.UI
{
    /// <summary>The live ranking in the corner: players ordered by KDA, you highlighted. It refreshes only when something it shows has changed.</summary>
    public sealed class RankingPanelPresenter : MonoBehaviour
    {
        [SerializeField] RectTransform rowsRoot;
        [SerializeField] RankingRowView rowPrefab;
        [SerializeField, Min(1)] int maxRows = 6;

        readonly List<RankingRowView> m_Rows = new List<RankingRowView>();
        IRankingQuery m_Ranking; ITankRegistry m_Tanks; ILocalPlayer m_Local; PlayerPalette m_Palette; UiTheme m_Theme;
        bool m_Dirty = true;

        [Inject]
        void Construct(IEventBus bus, IRankingQuery ranking, ITankRegistry tanks, ILocalPlayer local, PlayerPalette palette, UiTheme theme)
        {
            m_Ranking = ranking; m_Tanks = tanks; m_Local = local; m_Palette = palette; m_Theme = theme;
            bus.Subscribe<RankingChanged>(e => m_Dirty = true);
            bus.Subscribe<TankRenamed>(e => m_Dirty = true);
            bus.Subscribe<TankSpawned>(e => m_Dirty = true);
            bus.Subscribe<TankRemoved>(e => m_Dirty = true);
        }

        void LateUpdate()
        {
            if (!m_Dirty || m_Ranking == null) return;
            m_Dirty = false;
            IReadOnlyList<PlayerStats> ranked = m_Ranking.Ranked;
            int count = Mathf.Min(maxRows, ranked.Count);
            while (m_Rows.Count < count) m_Rows.Add(Instantiate(rowPrefab, rowsRoot));
            for (int i = 0; i < m_Rows.Count; i++)
            {
                bool used = i < count;
                m_Rows[i].gameObject.SetActive(used);
                if (!used) continue;
                PlayerStats s = ranked[i];
                TankModel t = m_Tanks.Find(s.Id);
                if (t == null) { m_Rows[i].gameObject.SetActive(false); continue; }
                Color c = m_Palette.Get(t.ColorSlot).color;
                m_Rows[i].Bind(i + 1, t.Name, c, s.Kills, s.Deaths, s.Assists, s.Kda, s.Id == m_Local.TankId, m_Theme.highlight);
            }
        }
    }
}
