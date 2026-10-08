using System.Collections.Generic;
using Reflex.Attributes;
using Tank.Core.Combat;
using Tank.Core.Events;
using Tank.Gameplay.Config;
using UnityEngine;

namespace Tank.Gameplay.Views
{
    [System.Serializable]
    public sealed class ViewPrefabs { public TankView tank; public Tank.Gameplay.Items.ItemSlotView itemSlot; }

    /// <summary>
    /// Keeps one TankView per tank model. Each frame it blends between the last two simulation ticks, so a 30 Hz simulation looks smooth at any
    /// frame rate; a respawn snaps instead of sliding across the map.
    /// </summary>
    public sealed class TankViewSystem : MonoBehaviour
    {
        sealed class Entry
        {
            public TankModel Model; public TankView View;
            public Vector2 PrevPos, CurrPos; public float PrevYaw, CurrYaw, PrevTurret, CurrTurret;
        }

        readonly Dictionary<int, Entry> m_Entries = new Dictionary<int, Entry>();
        ITankRegistry m_Tanks; ViewPrefabs m_Prefabs; PlayerPalette m_Palette; IRenderClock m_Clock; ILocalPlayer m_Local;

        [Inject]
        void Construct(IEventBus bus, ITankRegistry tanks, ViewPrefabs prefabs, PlayerPalette palette, IRenderClock clock, ILocalPlayer local)
        {
            m_Local = local;
            m_Tanks = tanks; m_Prefabs = prefabs; m_Palette = palette; m_Clock = clock;
            bus.Subscribe<TankSpawned>(e => Create(e.TankId));
            bus.Subscribe<TankRespawned>(e => Snap(e.TankId));
            bus.Subscribe<TankKilled>(e => { if (m_Entries.TryGetValue(e.VictimId, out Entry en)) en.View.SetAlive(false); });
            bus.Subscribe<TankDamaged>(e => { if (m_Entries.TryGetValue(e.VictimId, out Entry en)) en.View.SetHealth01(e.HpLeft / (float)en.Model.Health.Max); });
            bus.Subscribe<TankRenamed>(e => { if (m_Entries.TryGetValue(e.TankId, out Entry en)) en.View.SetName(e.Name, m_Palette.Get(en.Model.ColorSlot).color); });
            bus.Subscribe<TankRemoved>(e => { if (m_Entries.TryGetValue(e.TankId, out Entry en)) { Destroy(en.View.gameObject); m_Entries.Remove(e.TankId); } });
            bus.Subscribe<SimulationTicked>(e => Latch());
        }

        void Create(int id)
        {
            TankModel m = m_Tanks.Find(id);
            if (m == null || m_Entries.ContainsKey(id)) return;
            TankView view = Instantiate(m_Prefabs.tank, transform);
            view.name = "Tank_" + m.Name;
            view.SetColor(m_Palette.Get(m.ColorSlot).tankMaterial);
            view.SetName(m.Name, m_Palette.Get(m.ColorSlot).color);
            var entry = new Entry { Model = m, View = view };
            m_Entries[id] = entry;
            Snap(id);
        }

        void Snap(int id)
        {
            if (!m_Entries.TryGetValue(id, out Entry e)) return;
            e.PrevPos = e.CurrPos = e.Model.Position; e.PrevYaw = e.CurrYaw = e.Model.Body.Yaw; e.PrevTurret = e.CurrTurret = e.Model.TurretYaw;
            e.View.SetAlive(true); e.View.SetHealth01(1f);
            e.View.Present(e.CurrPos, e.CurrYaw, e.CurrTurret);
        }

        void Latch()
        {
            foreach (Entry e in m_Entries.Values)
            {
                e.PrevPos = e.CurrPos; e.PrevYaw = e.CurrYaw; e.PrevTurret = e.CurrTurret;
                e.CurrPos = e.Model.Position; e.CurrYaw = e.Model.Body.Yaw; e.CurrTurret = e.Model.TurretYaw;
            }
        }

        void LateUpdate()
        {
            if (m_Clock == null) return;
            float a = m_Clock.Alpha;
            foreach (Entry e in m_Entries.Values)
            {
                e.View.SetLocal(e.Model.Id == m_Local.TankId);
                StatusEffects s = e.Model.Status;
                e.View.SetStatus(s.Shield > 0, s.SpeedTime > 0f, s.DamageTime > 0f);
                e.View.Present(Vector2.Lerp(e.PrevPos, e.CurrPos, a), Mathf.LerpAngle(e.PrevYaw, e.CurrYaw, a), Mathf.LerpAngle(e.PrevTurret, e.CurrTurret, a));
            }
        }

        public bool TryGetPosition(int tankId, out Vector3 position)
        {
            if (m_Entries.TryGetValue(tankId, out Entry e)) { position = e.View.transform.position; return true; }
            position = Vector3.zero; return false;
        }
    }
}
