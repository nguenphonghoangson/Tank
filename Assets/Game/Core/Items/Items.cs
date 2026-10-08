using System.Collections.Generic;
using Tank.Core.Combat;
using Tank.Core.Common;
using Tank.Core.Events;
using UnityEngine;

namespace Tank.Core.Items
{
    public enum ItemEffectKind { Repair, Shield, Speed, Damage, Weapon }

    public readonly struct ItemSpec
    {
        public readonly int Id;
        public readonly string Name;
        public readonly ItemEffectKind Kind;
        public readonly float Magnitude;        // hit points healed, shield points, ...
        public readonly float Duration;         // seconds, for timed effects
        public readonly int WeaponId;           // for weapon pickups
        public readonly float Weight;           // how likely it is to appear, relative to the others
        public readonly float RespawnSeconds;   // how long its slot stays empty after it is taken

        public ItemSpec(int id, string name, ItemEffectKind kind, float magnitude, float duration, int weaponId, float weight, float respawnSeconds)
        {
            Id = id; Name = name; Kind = kind; Magnitude = magnitude; Duration = duration; WeaponId = weaponId; Weight = weight; RespawnSeconds = respawnSeconds;
        }
    }

    public interface IItemCatalog { int Count { get; } ItemSpec Get(int id); }

    public sealed class ArrayItemCatalog : IItemCatalog
    {
        readonly ItemSpec[] m_Specs;
        public ArrayItemCatalog(ItemSpec[] specs) { m_Specs = specs; }
        public int Count => m_Specs.Length;
        public ItemSpec Get(int id) { return m_Specs[id]; }
    }

    /// <summary>Strategy: what one kind of item does. One class per kind, so a new item is a new class and a catalogue line.</summary>
    public interface IItemEffect
    {
        ItemEffectKind Kind { get; }
        /// <returns>false when picking it up would be wasted (a repair kit at full health), so it stays where it is.</returns>
        bool TryApply(TankModel tank, in ItemSpec spec);
    }

    public sealed class RepairEffect : IItemEffect
    {
        public ItemEffectKind Kind => ItemEffectKind.Repair;
        public bool TryApply(TankModel tank, in ItemSpec spec)
        {
            if (tank.Health.Current >= tank.Health.Max) return false;
            tank.Health.Heal(Mathf.RoundToInt(spec.Magnitude));
            return true;
        }
    }

    public sealed class ShieldEffect : IItemEffect
    {
        public ItemEffectKind Kind => ItemEffectKind.Shield;
        public bool TryApply(TankModel tank, in ItemSpec spec) { tank.Status.Shield = Mathf.RoundToInt(spec.Magnitude); tank.Status.ShieldTime = spec.Duration; return true; }
    }

    public sealed class SpeedEffect : IItemEffect
    {
        public ItemEffectKind Kind => ItemEffectKind.Speed;
        public bool TryApply(TankModel tank, in ItemSpec spec) { tank.Status.SpeedTime = spec.Duration; return true; }
    }

    public sealed class DamageEffect : IItemEffect
    {
        public ItemEffectKind Kind => ItemEffectKind.Damage;
        public bool TryApply(TankModel tank, in ItemSpec spec) { tank.Status.DamageTime = spec.Duration; return true; }
    }

    public sealed class WeaponEffect : IItemEffect
    {
        readonly WeaponSystem m_Weapons;
        public WeaponEffect(WeaponSystem weapons) { m_Weapons = weapons; }
        public ItemEffectKind Kind => ItemEffectKind.Weapon;
        public bool TryApply(TankModel tank, in ItemSpec spec)
        {
            if (tank.Weapon.WeaponId == spec.WeaponId) return false;         // already holding it
            m_Weapons.Equip(tank, spec.WeaponId);
            return true;
        }
    }

    public readonly struct ItemSettings
    {
        public readonly float PickupRadius;
        public ItemSettings(float pickupRadius) { PickupRadius = pickupRadius; }
    }

    public sealed class ItemSlotLayout
    {
        public readonly Vector2[] Positions;
        public ItemSlotLayout(Vector2[] positions) { Positions = positions; }
    }

    public readonly struct ItemSlotState
    {
        public readonly int SlotId, ItemId; public readonly Vector2 Position;
        public ItemSlotState(int slotId, int itemId, Vector2 position) { SlotId = slotId; ItemId = itemId; Position = position; }
    }

    public interface IItemQuery { IReadOnlyList<ItemSlotState> Slots { get; } }

    /// <summary>
    /// The item slots on the map: each holds one item (rolled by weight), hands it to the first tank that drives over it and can use it,
    /// then stays empty until its respawn time. Only the authority runs this; everyone else follows the events.
    /// </summary>
    public sealed class ItemSystem : ITickable, IItemQuery
    {
        sealed class Slot { public int Id, Item = -1; public Vector2 Position; public float RespawnAt; }

        readonly IItemCatalog m_Catalog;
        readonly ITankRegistry m_Tanks;
        readonly IEventBus m_Bus;
        readonly IClock m_Clock;
        readonly IRandom m_Random;
        readonly ItemSettings m_Settings;
        readonly Dictionary<ItemEffectKind, IItemEffect> m_Effects = new Dictionary<ItemEffectKind, IItemEffect>();
        readonly List<Slot> m_Slots = new List<Slot>();
        readonly List<ItemSlotState> m_View = new List<ItemSlotState>();

        public ItemSystem(IItemCatalog catalog, ItemSlotLayout layout, ITankRegistry tanks, IEventBus bus, IClock clock, IRandom random, ItemSettings settings, IEnumerable<IItemEffect> effects)
        {
            m_Catalog = catalog; m_Tanks = tanks; m_Bus = bus; m_Clock = clock; m_Random = random; m_Settings = settings;
            foreach (IItemEffect e in effects) m_Effects[e.Kind] = e;
            for (int i = 0; i < layout.Positions.Length; i++) m_Slots.Add(new Slot { Id = i, Position = layout.Positions[i], RespawnAt = 0f });
            bus.Subscribe<Tank.Core.Events.MatchPhaseChanged>(e => { if (e.Phase == Tank.Core.Match.MatchPhase.Warmup) Reset(); });
        }

        public IReadOnlyList<ItemSlotState> Slots
        {
            get
            {
                m_View.Clear();
                foreach (Slot s in m_Slots) m_View.Add(new ItemSlotState(s.Id, s.Item, s.Position));
                return m_View;
            }
        }

        /// <summary>Empties every slot and fills them again: a new match starts with a full map.</summary>
        public void Reset()
        {
            foreach (Slot s in m_Slots) { s.Item = -1; s.RespawnAt = 0f; }
        }

        public void Tick(float dt)
        {
            for (int i = 0; i < m_Slots.Count; i++)
            {
                Slot s = m_Slots[i];
                if (s.Item < 0)
                {
                    if (m_Clock.Now < s.RespawnAt) continue;
                    s.Item = Roll();
                    m_Bus.Publish(new ItemSlotChanged(s.Id, s.Item, s.Position));
                    continue;
                }
                ItemSpec spec = m_Catalog.Get(s.Item);
                if (!m_Effects.TryGetValue(spec.Kind, out IItemEffect effect)) continue;
                for (int k = 0; k < m_Tanks.All.Count; k++)
                {
                    TankModel t = m_Tanks.All[k];
                    if (!t.IsAlive || (t.Position - s.Position).sqrMagnitude > m_Settings.PickupRadius * m_Settings.PickupRadius) continue;
                    if (!effect.TryApply(t, spec)) continue;
                    m_Bus.Publish(new ItemTaken(s.Id, t.Id, s.Item, s.Position));
                    s.Item = -1;
                    s.RespawnAt = m_Clock.Now + spec.RespawnSeconds * (0.8f + 0.4f * m_Random.Value());
                    m_Bus.Publish(new ItemSlotChanged(s.Id, -1, s.Position));
                    break;
                }
            }
        }

        int Roll()
        {
            float total = 0f;
            for (int i = 0; i < m_Catalog.Count; i++) total += m_Catalog.Get(i).Weight;
            float r = m_Random.Value() * total;
            for (int i = 0; i < m_Catalog.Count; i++) { float w = m_Catalog.Get(i).Weight; if (r < w) return i; r -= w; }
            return m_Catalog.Count - 1;
        }
    }
}

namespace Tank.Core.Items
{
    /// <summary>
    /// What is in each slot, known from the item events alone. The server, an offline game and a remote client all see the same events,
    /// so everyone has the same answer to "where is an item" without the client running the item rules.
    /// </summary>
    public sealed class ItemSlotMirror : IItemQuery
    {
        readonly System.Collections.Generic.Dictionary<int, ItemSlotState> m_Slots = new System.Collections.Generic.Dictionary<int, ItemSlotState>();
        readonly System.Collections.Generic.List<ItemSlotState> m_View = new System.Collections.Generic.List<ItemSlotState>();

        public ItemSlotMirror(Tank.Core.Events.IEventBus bus)
        {
            bus.Subscribe<Tank.Core.Events.ItemSlotChanged>(e => m_Slots[e.SlotId] = new ItemSlotState(e.SlotId, e.ItemId, e.Position));
        }

        public System.Collections.Generic.IReadOnlyList<ItemSlotState> Slots
        {
            get { m_View.Clear(); foreach (ItemSlotState s in m_Slots.Values) m_View.Add(s); return m_View; }
        }
    }
}
