using System;
using Tank.Core.Items;
using UnityEngine;

namespace Tank.Gameplay.Config
{
    /// <summary>Every item: what it does (rules) and how it looks and sounds (icon, effect, sound). It is the IItemCatalog the core reads.</summary>
    [CreateAssetMenu(menuName = "Tank/Config/Items", fileName = "ItemConfig")]
    public sealed class ItemConfig : ScriptableObject, IItemCatalog
    {
        [Serializable]
        public sealed class Entry
        {
            public string name = "Item";
            [Header("Rules")]
            public ItemEffectKind kind;
            [Tooltip("Hit points for a repair, shield points for a shield.")] public float magnitude;
            public float duration;
            [Tooltip("For a weapon pickup: the index in the weapon config.")] public int weaponId;
            public float weight = 1f;
            public float respawnSeconds = 30f;
            [Header("Presentation")]
            public GameObject iconPrefab;
            public Color color = Color.white;
            public GameObject pickupVfx;
            public SfxCue pickupSfx;
        }

        public Entry[] items = new Entry[0];

        public int Count => items.Length;
        public Entry Presentation(int id) { return items[Mathf.Clamp(id, 0, items.Length - 1)]; }

        public ItemSpec Get(int id)
        {
            Entry e = items[id];
            return new ItemSpec(id, e.name, e.kind, e.magnitude, e.duration, e.weaponId, e.weight, e.respawnSeconds);
        }
    }
}
