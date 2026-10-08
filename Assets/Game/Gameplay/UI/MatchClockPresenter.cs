using Reflex.Attributes;
using TMPro;
using Tank.Core.Events;
using Tank.Core.Match;
using UnityEngine;

namespace Tank.Gameplay.UI
{
    /// <summary>The match clock at the top and the big message in the middle: the 3-2-1 before the start, GO, and the end of the match.</summary>
    public sealed class MatchClockPresenter : MonoBehaviour
    {
        [SerializeField] TMP_Text timer;
        [SerializeField] TMP_Text phase;
        [SerializeField] TMP_Text banner;
        [SerializeField] CanvasGroup bannerGroup;
        [SerializeField] float goSeconds = 1.1f;

        IMatchQuery m_Match; UiTheme m_Theme;
        float m_GoUntil;
        int m_LastShownSecond = -1;
        float m_PunchStart;

        [Inject]
        void Construct(IMatchQuery match, IEventBus bus, UiTheme theme)
        {
            m_Match = match; m_Theme = theme;
            bus.Subscribe<MatchPhaseChanged>(e => { if (e.Phase == MatchPhase.Playing) m_GoUntil = Time.unscaledTime + goSeconds; });
        }

        void LateUpdate()
        {
            if (m_Match == null) return;
            int secs = Mathf.CeilToInt(Mathf.Max(0f, m_Match.TimeLeft));
            timer.text = (secs / 60) + ":" + (secs % 60).ToString("00");
            string text = null;
            switch (m_Match.Phase)
            {
                case MatchPhase.Warmup: phase.text = "GET READY"; text = secs > 0 ? secs.ToString() : "GO"; break;
                case MatchPhase.Playing: phase.text = "MATCH"; if (Time.unscaledTime < m_GoUntil) text = "GO!"; break;
                case MatchPhase.Ended: phase.text = "FINAL"; text = "MATCH OVER"; break;
            }
            timer.color = m_Match.Phase == MatchPhase.Playing && secs <= 30 ? m_Theme.bad : m_Theme.text;

            bool show = text != null;
            bannerGroup.alpha = show ? 1f : 0f;
            if (!show) { m_LastShownSecond = -1; return; }
            if (banner.text != text) { banner.text = text; m_PunchStart = Time.unscaledTime; }
            float punch = Mathf.Clamp01((Time.unscaledTime - m_PunchStart) / 0.25f);         // each new number pops in
            banner.transform.localScale = Vector3.one * Mathf.Lerp(1.5f, 1f, 1f - Mathf.Pow(1f - punch, 3f));
        }
    }
}
