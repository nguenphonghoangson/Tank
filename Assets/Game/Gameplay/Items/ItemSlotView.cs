using UnityEngine;

namespace Tank.Gameplay.Items
{
    /// <summary>One item slot on the map: the pad it sits on and the icon that bobs and turns above it. Shows what it is told to show.</summary>
    public sealed class ItemSlotView : MonoBehaviour
    {
        [SerializeField] Transform iconAnchor;
        [SerializeField] float spinDegreesPerSecond = 120f;
        [SerializeField] float bobHeight = 0.15f;
        [SerializeField] float bobSpeed = 3f;

        GameObject m_Icon;
        Vector3 m_AnchorRest;

        void Awake() { m_AnchorRest = iconAnchor != null ? iconAnchor.localPosition : Vector3.zero; }

        public void ShowIcon(GameObject iconPrefab)
        {
            Clear();
            if (iconPrefab == null || iconAnchor == null) return;
            m_Icon = Instantiate(iconPrefab, iconAnchor);
            m_Icon.transform.localPosition = Vector3.zero; m_Icon.transform.localRotation = Quaternion.identity;
        }

        public void Clear()
        {
            if (m_Icon != null) Destroy(m_Icon);
            m_Icon = null;
        }

        void Update()
        {
            if (m_Icon == null || iconAnchor == null) return;
            iconAnchor.Rotate(0f, spinDegreesPerSecond * Time.deltaTime, 0f, Space.Self);
            iconAnchor.localPosition = m_AnchorRest + new Vector3(0f, bobHeight * Mathf.Sin(Time.time * bobSpeed), 0f);
        }
    }
}
