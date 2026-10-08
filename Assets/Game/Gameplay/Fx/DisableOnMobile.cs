using UnityEngine;

namespace Tank.Gameplay.Fx
{
    /// <summary>Switches its object off on phones and tablets, for effects (like bloom) that cost more than they give on a small GPU.</summary>
    public sealed class DisableOnMobile : MonoBehaviour
    {
        void Awake() { if (Application.isMobilePlatform) gameObject.SetActive(false); }
    }
}
