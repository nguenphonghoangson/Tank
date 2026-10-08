using Reflex.Attributes;
using Tank.Core.Events;
using UnityEngine;
using UnityEngine.UI;

namespace Tank.Gameplay.UI
{
    /// <summary>A red glow around the screen's edge when your tank is hit, stronger for a bigger hit and fading fast.</summary>
    public sealed class DamageFlashPresenter : MonoBehaviour
    {
        [SerializeField] Image vignette;
        [SerializeField] float fadePerSecond = 2.2f;

        ILocalPlayer m_Local;
        float m_Alpha;

        [Inject]
        void Construct(IEventBus bus, ILocalPlayer local)
        {
            m_Local = local;
            bus.Subscribe<TankDamaged>(e => { if (e.VictimId == m_Local.TankId && e.Amount > 0) m_Alpha = Mathf.Clamp01(Mathf.Max(m_Alpha, 0.25f + e.Amount / 60f)); });
            bus.Subscribe<TankKilled>(e => { if (e.VictimId == m_Local.TankId) m_Alpha = 1f; });
        }

        void Update()
        {
            m_Alpha = Mathf.Max(0f, m_Alpha - fadePerSecond * Time.unscaledDeltaTime);
            Color c = vignette.color; c.a = m_Alpha; vignette.color = c;
        }
    }
}
