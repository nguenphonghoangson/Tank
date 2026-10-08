using System.Collections.Generic;
using Reflex.Attributes;
using Tank.Core.Events;
using Tank.Gameplay.Config;
using Tank.Gameplay.Views;
using UnityEngine;

namespace Tank.Gameplay.Items
{
    /// <summary>Keeps one view per item slot and shows whatever the item events say is in it. It never asks the item system: the same events reach every machine.</summary>
    public sealed class ItemViewSystem : MonoBehaviour
    {
        readonly Dictionary<int, ItemSlotView> m_Slots = new Dictionary<int, ItemSlotView>();
        ItemConfig m_Items; ViewPrefabs m_Prefabs;

        [Inject]
        void Construct(IEventBus bus, ItemConfig items, ViewPrefabs prefabs)
        {
            m_Items = items; m_Prefabs = prefabs;
            bus.Subscribe<ItemSlotChanged>(OnSlotChanged);
        }

        void OnSlotChanged(ItemSlotChanged e)
        {
            if (!m_Slots.TryGetValue(e.SlotId, out ItemSlotView view))
            {
                view = Instantiate(m_Prefabs.itemSlot, new Vector3(e.Position.x, 0f, e.Position.y), Quaternion.identity, transform);
                view.name = "ItemSlot_" + e.SlotId;
                m_Slots[e.SlotId] = view;
            }
            if (e.ItemId < 0) view.Clear(); else view.ShowIcon(m_Items.Presentation(e.ItemId).iconPrefab);
        }
    }
}
