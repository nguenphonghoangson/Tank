using UnityEngine;

namespace Tank.Gameplay.UI
{
    /// <summary>Keeps everything under it inside the part of the screen that is not behind a notch, camera hole or rounded corner.</summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        Rect m_Applied;
        Vector2Int m_Screen;

        void LateUpdate()
        {
            Rect safe = Screen.safeArea;
            if (safe == m_Applied && m_Screen.x == Screen.width && m_Screen.y == Screen.height) return;
            m_Applied = safe; m_Screen = new Vector2Int(Screen.width, Screen.height);
            var rt = (RectTransform)transform;
            rt.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
            rt.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
