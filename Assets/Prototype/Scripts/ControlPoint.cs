using UnityEngine;

namespace TankGame.Prototype
{
    /// <summary>
    /// Capture rules for one control point, free of Unity objects so they can be tested directly.
    /// States: neutral / owned / contested. Only one team present at a time changes anything; two or more freeze it.
    /// More tanks capture faster with diminishing returns (2 tanks = 1.5x, 3 = 1.75x), so stacking is not always best.
    /// </summary>
    public sealed class CapturePointModel
    {
        public const int None = -1;
        public enum Change { None, Captured, Neutralized, ContestStarted, Decayed }

        public float CaptureSeconds = 6f;
        public float DecaySeconds = 45f;   // an owned point nobody defends loses this much hold per second (1 / DecaySeconds)
        public int Owner = None;
        public float OwnerHold;          // 0..1, how secure the owner's control is
        public int Capturer = None;      // team currently filling the neutral capture bar
        public float CapProgress;        // 0..1
        public bool Contested;

        public static float StackFactor(int count) { return count <= 0 ? 0f : 2f - Mathf.Pow(0.5f, count - 1); }

        public void Reset()
        {
            Owner = None; OwnerHold = 0f; Capturer = None; CapProgress = 0f; Contested = false;
        }

        /// <param name="teamCounts">living tanks inside the zone, per team index</param>
        public Change Tick(float dt, int[] teamCounts, out int team)
        {
            team = None;
            int present = 0, sole = None, soleCount = 0;
            for (int t = 0; t < teamCounts.Length; t++)
                if (teamCounts[t] > 0) { present++; sole = t; soleCount = teamCounts[t]; }

            bool was = Contested;
            Contested = present >= 2;
            if (Contested) { team = sole; return was ? Change.None : Change.ContestStarted; }

            if (present == 0)
            {
                if (Owner == None)
                {
                    CapProgress = Mathf.Max(0f, CapProgress - dt / CaptureSeconds * 0.5f);
                    return Change.None;
                }
                // nobody defends it: the hold slowly bleeds away until the point is neutral again
                OwnerHold -= dt / DecaySeconds;
                if (OwnerHold > 0f) return Change.None;
                team = Owner;
                Owner = None; OwnerHold = 0f; Capturer = None; CapProgress = 0f;
                return Change.Decayed;
            }

            float rate = StackFactor(soleCount) / CaptureSeconds;
            if (Owner == sole)
            {
                OwnerHold = Mathf.Min(1f, OwnerHold + rate * 1.5f * dt);   // defenders re-fortify faster than attackers capture
                return Change.None;
            }
            if (Owner != None)
            {
                OwnerHold -= rate * dt;
                if (OwnerHold > 0f) return Change.None;
                Owner = None; OwnerHold = 0f; Capturer = sole; CapProgress = 0f;
                team = sole;
                return Change.Neutralized;
            }

            if (Capturer != sole) { Capturer = sole; CapProgress = 0f; }
            CapProgress += rate * dt;
            if (CapProgress < 1f) return Change.None;
            Owner = sole; OwnerHold = 1f; Capturer = None; CapProgress = 0f;
            team = sole;
            return Change.Captured;
        }
    }

    /// <summary>
    /// A capturable zone on the map. The match tells it who stands inside; it shows who owns it and how far a capture has got.
    /// Placed by the map, so any map can have any number of points.
    /// </summary>
    public sealed class ControlPoint : MonoBehaviour
    {
        public string label = "A";
        public float radius = 7f;
        public float weight = 1f;

        [Header("Visuals (assigned by the map builder)")]
        public Renderer fill;
        public Renderer ring;
        public Renderer beam;
        public Transform flag;          // cloth: rises with the hold, falls as it is lost
        public Renderer flagRenderer;

        public readonly CapturePointModel Model = new CapturePointModel();

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly Color Neutral = new Color(0.62f, 0.64f, 0.7f);
        static readonly Color ContestColor = new Color(1f, 0.9f, 0.35f);

        Color[] m_TeamColors;
        MaterialPropertyBlock m_Block;

        public int Owner => Model.Owner;
        public bool Contested => Model.Contested;

        public void Init(Color[] teamColors, float captureSeconds, float decaySeconds)
        {
            m_TeamColors = teamColors;
            Model.CaptureSeconds = captureSeconds;
            Model.DecaySeconds = decaySeconds;
            Model.Reset();
            if (m_Block == null) m_Block = new MaterialPropertyBlock();
            Refresh();
        }

        public CapturePointModel.Change Step(float dt, int[] teamCounts, out int team)
        {
            return Model.Tick(dt, teamCounts, out team);
        }

        public bool Contains(Vector3 p, float extra = 0f)
        {
            Vector3 d = p - transform.position;
            d.y = 0f;
            return d.sqrMagnitude <= (radius + extra) * (radius + extra);
        }

        void Update() { if (m_TeamColors != null) Refresh(); }

        void Refresh()
        {
            Color owner = Model.Owner >= 0 ? m_TeamColors[Model.Owner] : Neutral;
            Color fillColor;
            float fillScale;
            if (Model.Owner >= 0) { fillColor = owner; fillScale = Model.OwnerHold; }
            else if (Model.Capturer >= 0) { fillColor = m_TeamColors[Model.Capturer]; fillScale = Model.CapProgress; }
            else { fillColor = Neutral; fillScale = 0f; }

            float pulse = Model.Contested ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 10f) : 0f;
            Color ringColor = Model.Contested ? Color.Lerp(owner, ContestColor, 0.5f + 0.5f * pulse) : owner;

            fill.transform.localScale = new Vector3(radius * 2f * Mathf.Max(0.001f, fillScale), radius * 2f * Mathf.Max(0.001f, fillScale), 1f);
            fill.gameObject.SetActive(fillScale > 0.002f);
            Set(fill, new Color(fillColor.r, fillColor.g, fillColor.b, 0.5f));
            Set(ring, new Color(ringColor.r, ringColor.g, ringColor.b, Model.Contested ? 0.9f : 0.75f));
            if (flag != null)
            {
                float level = Model.Owner >= 0 ? Model.OwnerHold : (Model.Capturer >= 0 ? Model.CapProgress * 0.6f : 0f);
                Vector3 fp = flag.localPosition;
                fp.y = Mathf.Lerp(1.2f, 6.2f, level);
                flag.localPosition = fp;
                Color fc = Model.Owner >= 0 ? owner : (Model.Capturer >= 0 ? m_TeamColors[Model.Capturer] : Neutral);
                Set(flagRenderer, new Color(fc.r, fc.g, fc.b, 1f));
            }
            Color beamColor = Model.Contested ? ContestColor : owner;
            Set(beam, new Color(beamColor.r, beamColor.g, beamColor.b, Model.Owner >= 0 || Model.Contested ? 0.2f : 0.07f));
        }

        void Set(Renderer r, Color c)
        {
            if (m_Block == null) m_Block = new MaterialPropertyBlock();   // also after a script reload in Play mode
            r.GetPropertyBlock(m_Block);
            m_Block.SetColor(BaseColorId, c);
            r.SetPropertyBlock(m_Block);
        }
    }
}
