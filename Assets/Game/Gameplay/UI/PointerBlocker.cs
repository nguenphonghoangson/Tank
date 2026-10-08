using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Tank.Gameplay.UI
{
    /// <summary>Asked by the touch controls when a finger lands: is it on a button or a text box? If so, the finger belongs to the interface, not to the sticks.</summary>
    public interface IPointerBlocker { bool IsOverInteractive(Vector2 screenPosition); }

    public sealed class UiPointerBlocker : IPointerBlocker
    {
        readonly List<RaycastResult> m_Hits = new List<RaycastResult>();

        public bool IsOverInteractive(Vector2 screenPosition)
        {
            EventSystem es = EventSystem.current;
            if (es == null) return false;
            m_Hits.Clear();
            es.RaycastAll(new PointerEventData(es) { position = screenPosition }, m_Hits);
            foreach (RaycastResult h in m_Hits)
                if (h.gameObject != null && h.gameObject.GetComponentInParent<UnityEngine.UI.Selectable>() != null) return true;
            return false;
        }
    }
}
