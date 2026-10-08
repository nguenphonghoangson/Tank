using System.Collections.Generic;
using Reflex.Attributes;
using Tank.Core.Combat;
using Tank.Core.Events;
using Tank.Gameplay.Config;
using UnityEngine;

namespace Tank.Gameplay.UI
{
    /// <summary>The short list of who just destroyed whom, newest at the bottom, each line fading out after a few seconds.</summary>
    public sealed class KillFeedPresenter : MonoBehaviour
    {
        [SerializeField] RectTransform root;
        [SerializeField] KillFeedEntryView entryPrefab;
        [SerializeField] float lifetime = 5f;
        [SerializeField, Min(1)] int maxEntries = 4;

        readonly List<KillFeedEntryView> m_Entries = new List<KillFeedEntryView>();
        ITankRegistry m_Tanks; PlayerPalette m_Palette; ILocalPlayer m_Local;

        [Inject]
        void Construct(IEventBus bus, ITankRegistry tanks, PlayerPalette palette, ILocalPlayer local)
        {
            m_Tanks = tanks; m_Palette = palette; m_Local = local;
            bus.Subscribe<TankKilled>(OnKilled);
        }

        void OnKilled(TankKilled e)
        {
            TankModel victim = m_Tanks.Find(e.VictimId), killer = m_Tanks.Find(e.KillerId);
            if (victim == null) return;
            if (m_Entries.Count >= maxEntries) { Destroy(m_Entries[0].gameObject); m_Entries.RemoveAt(0); }
            KillFeedEntryView entry = Instantiate(entryPrefab, root);
            bool involvesMe = e.KillerId == m_Local.TankId || e.VictimId == m_Local.TankId;
            entry.Show(killer != null && killer.Id != victim.Id ? killer.Name : null, killer != null ? m_Palette.Get(killer.ColorSlot).color : Color.white, victim.Name, m_Palette.Get(victim.ColorSlot).color, involvesMe);
            entry.Born = Time.unscaledTime;
            m_Entries.Add(entry);
        }

        void Update()
        {
            for (int i = m_Entries.Count - 1; i >= 0; i--)
            {
                float age = Time.unscaledTime - m_Entries[i].Born;
                if (age >= lifetime) { Destroy(m_Entries[i].gameObject); m_Entries.RemoveAt(i); continue; }
                m_Entries[i].SetAlpha(Mathf.Clamp01((lifetime - age) / 0.8f));
            }
        }
    }
}
