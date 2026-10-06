using UnityEngine;

namespace TankGame.Prototype
{
    /// <summary>
    /// The single place that decides where every HUD element goes, in virtual units (a fixed reference height, so the
    /// layout is the same on every screen size). Corners have one job each: status top-left, score bar top-centre,
    /// minimap top-right, thumbs (sticks) at the bottom corners, buttons in a column on the right edge between the
    /// minimap and the aim stick. Pure data, so tests can check that nothing overlaps.
    /// </summary>
    public struct HudLayout
    {
        public float W, H;
        public Rect safe;           // inside notch / home indicator
        public Rect status;         // top-left player card
        public Rect scoreBar;       // top-centre phase, timer, team scores
        public Rect log;            // under the score bar
        public Rect minimap;        // top-right
        public Vector2 reloadC, dashC;
        public float reloadR, dashR;
        public Vector2 moveHint, aimHint;
        public float stickR;
        public bool mobile;

        public const float Margin = 10f;

        public static HudLayout Compute(float w, float h, Rect safe, bool mobile)
        {
            var l = new HudLayout { W = w, H = h, safe = safe, mobile = mobile };
            float m = Margin;

            l.status = new Rect(safe.x + m, safe.y + m, mobile ? 190f : 240f, 74f);
            float mm = mobile ? 112f : 170f;
            l.minimap = new Rect(safe.xMax - m - mm, safe.y + m, mm, mm);

            float left = l.status.xMax + m, right = l.minimap.x - m;
            float barW = Mathf.Min(mobile ? 380f : 520f, right - left);
            float barX = Mathf.Clamp(w * 0.5f - barW * 0.5f, left, right - barW);
            l.scoreBar = new Rect(barX, safe.y + m, barW, 46f);
            l.log = new Rect(barX, l.scoreBar.yMax + 4f, barW, 48f);

            l.stickR = Mathf.Min(70f, h * 0.155f);
            l.dashR = 34f; l.reloadR = 26f;
            float bx = safe.xMax - m - l.dashR;
            l.reloadC = new Vector2(bx, l.minimap.yMax + 8f + l.reloadR);
            l.dashC = new Vector2(bx, l.reloadC.y + l.reloadR + 8f + l.dashR);
            l.moveHint = new Vector2(safe.x + m + l.stickR * 1.15f, safe.yMax - m - l.stickR * 1.15f);
            l.aimHint = new Vector2(safe.xMax - m - l.stickR * 1.15f, safe.yMax - m - l.stickR * 1.15f);
            return l;
        }

        /// <summary>Every rectangle a finger or the eye must find free: used by tests and by the flag markers.</summary>
        public Rect[] Reserved()
        {
            return new[] { status, scoreBar, log, minimap, Circle(reloadC, reloadR), Circle(dashC, dashR) };
        }

        public static Rect Circle(Vector2 c, float r) { return new Rect(c.x - r, c.y - r, r * 2f, r * 2f); }

        /// <summary>Thumb zones: the stick circles and the area they may drift to, kept free of other elements.</summary>
        public Rect[] ThumbZones()
        {
            return new[] { Circle(moveHint, stickR), Circle(aimHint, stickR) };
        }
    }
}
