using System.Collections.Generic;
using Reflex.Attributes;
using Tank.Core.Combat;
using Tank.Gameplay.Config;
using Tank.Gameplay.Views;
using Unity.Cinemachine;
using UnityEngine;

namespace Tank.Gameplay.CameraSystem
{
    /// <summary>
    /// The thing the camera looks at, and how far back it stands, as in the Tanknarok camera (see its GDD_Camera): every tank stays in view,
    /// the distance is the longest gap between any two tanks plus the minimum, plus a share of the way to the far distance that grows with it, the
    /// view is centred on the tanks' average pulled toward the outermost ones, and the centre is kept inside the arena so the camera never
    /// shows beyond its edge. A tank that is only waiting to respawn still counts, so the view does not jump on a death.
    /// Anyone outside the view gets an edge marker. The tilt never changes, only the distance along it. Cinemachine tracks this object, so the camera itself never learns about tanks.
    /// Runs before the CinemachineBrain (order 100) and after the tank views, so the camera never lags a frame behind.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public sealed class CameraFocus : MonoBehaviour
    {
        [SerializeField] CinemachineFollow follow;
        [Header("Distance (Tanknarok scene: 35 and 100)")]
        [SerializeField, Min(1f)] float minDistance = 35f;            // added to the longest gap between two tanks
        [SerializeField, Min(1f)] float farDistance = 100f;           // what the spread-proportional part reaches when the tanks are a full bounds diagonal apart
        [SerializeField, Min(1f)] float maxDistance = 220f;           // a safety limit, never reached in normal play
        [Header("View")]
        [SerializeField, Min(0f)] float farthestPull = 2.08f;         // how strongly the tanks farthest from the middle pull the view toward them (3 or more tanks)
        [SerializeField] float boundsOffsetDepth = -3f;               // Tanknarok moves the bounds' middle back by this much
        [SerializeField, Min(0f)] float singleTankExtraDepth = 2f;    // a lone tank is framed this much further up the arena
        [SerializeField, Min(0f)] float fitMargin = 3f;               // room kept around the outermost tank when fitting everyone on this screen
        [SerializeField, Min(0f)] float arenaMargin = 2f;             // how far past the playable edge the view may reach, in metres
        [Header("Smoothing")]
        [SerializeField, Min(0.01f)] float moveSmooth = 0.5f;         // the view's centre drifts (Tanknarok: lerp at 2 per second)
        [SerializeField, Min(0.01f)] float zoomSmooth = 0.12f;        // the distance follows quickly (Tanknarok: lerp at 10 per second)

        // Tanknarok's numbers (arena 26.35 x 17.7, camera bounds 22 x 14), scaled to this arena
        const float TanknarokArenaX = 26.35f, TanknarokArenaY = 17.7f, TanknarokBoundsX = 22f, TanknarokBoundsY = 14f;
        float ArenaScale => m_Arena.playableSemiAxes.y / TanknarokArenaY;
        // Tanknarok's camera bounds (22 x 14) sit inside its arena (26.35 x 17.7), scaled to this arena
        Vector2 BoundsHalf => new Vector2(m_Arena.playableSemiAxes.x * (TanknarokBoundsX / TanknarokArenaX), m_Arena.playableSemiAxes.y * (TanknarokBoundsY / TanknarokArenaY));
        float BoundsDiagonal => 4f * BoundsHalf.magnitude;            // Tanknarok: Distance(bounds, -bounds) * 2

        readonly List<Vector2> m_Points = new List<Vector2>();
        ILocalPlayer m_Local;
        TankViewSystem m_Views;
        ITankRegistry m_Tanks;
        ArenaConfig m_Arena;
        Camera m_Camera;
        Vector3 m_Direction, m_Velocity;
        float m_Distance, m_DistanceVelocity;
        bool m_Ready, m_Snapped;

        [Inject]
        void Construct(ILocalPlayer local, TankViewSystem views, ITankRegistry tanks, ArenaConfig arena) { m_Local = local; m_Views = views; m_Tanks = tanks; m_Arena = arena; }

        void LateUpdate()
        {
            if (m_Local == null || m_Views == null || follow == null) return;
            if (m_Camera == null) m_Camera = Camera.main;
            if (!m_Ready) { m_Direction = follow.FollowOffset.normalized; m_Distance = Mathf.Clamp(follow.FollowOffset.magnitude, minDistance, maxDistance); m_Ready = true; }
            if (!m_Views.TryGetPosition(m_Local.TankId, out Vector3 me)) return;
            Vector2 mine = new Vector2(me.x, me.z);

            // every tank, the local one included; one waiting to respawn still counts, so the view does not jump on a death
            m_Points.Clear();
            m_Points.Add(mine);
            for (int i = 0; i < m_Tanks.All.Count; i++)
            {
                TankModel t = m_Tanks.All[i];
                if (t.Id == m_Local.TankId || !m_Views.TryGetPosition(t.Id, out Vector3 p)) continue;
                m_Points.Add(new Vector2(p.x, p.z));
            }

            // distance, as in Tanknarok: the longest gap plus the minimum, then a share of the way to the far distance that grows with it
            float longest = LongestGap(m_Points);
            float distance = longest + minDistance;
            distance += distance / BoundsDiagonal * (farDistance - minDistance);

            Vector2 centre = WeightedCentre(m_Points);
            if (m_Points.Count == 1) centre.y += Mathf.Clamp01(centre.y / m_Arena.playableSemiAxes.y) * singleTankExtraDepth * ArenaScale;       // a lone tank sits a little ahead of the middle

            // the Tanknarok formula assumes a wide screen; stand back as far as this screen's shape needs for every tank to be inside
            // (twice: the arena clamp below can move the centre, and a farther view moves it again)
            Vector2 offset = new Vector2(0f, boundsOffsetDepth * ArenaScale);
            for (int pass = 0; pass < 2; pass++)
            {
                distance = Mathf.Max(distance, DistanceToFit(m_Points, centre));
                Vector2 half0 = VisibleHalfSize(Mathf.Min(distance, maxDistance));
                centre = ClampCentre(centre, offset, half0);
            }
            float wantedDistance = Mathf.Min(distance, maxDistance);
            Vector3 target = new Vector3(centre.x, 0f, centre.y);

            if (!m_Snapped) { m_Snapped = true; transform.position = target; m_Distance = wantedDistance; m_Velocity = Vector3.zero; m_DistanceVelocity = 0f; }
            transform.position = Vector3.SmoothDamp(transform.position, target, ref m_Velocity, moveSmooth);
            m_Distance = Mathf.SmoothDamp(m_Distance, wantedDistance, ref m_DistanceVelocity, zoomSmooth);
            follow.FollowOffset = m_Direction * m_Distance;
        }

        /// <summary>Keeps the view inside the arena bounds: the centre may go no closer to an edge than half of what the camera sees there.</summary>
        Vector2 ClampCentre(Vector2 centre, Vector2 offset, Vector2 half)
        {
            Vector2 reach = BoundsHalf;
            return new Vector2(ClampToBounds(centre.x - offset.x, reach.x, half.x) + offset.x, ClampToBounds(centre.y - offset.y, reach.y, half.y) + offset.y);
        }

        /// <summary>How far back the camera must stand, with this centre, for every point (plus a little room) to fall inside the screen.</summary>
        float DistanceToFit(List<Vector2> points, Vector2 centre)
        {
            Vector2 unit = VisibleHalfSize(1f);
            float need = 0f;
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 d = points[i] - centre;
                need = Mathf.Max(need, (Mathf.Abs(d.x) + fitMargin) / unit.x, (Mathf.Abs(d.y) + fitMargin) / unit.y);
            }
            return need;
        }

        /// <summary>Limits a centre coordinate so a view of this half-size stays inside the bounds; when the view is larger than the arena it centres on it.</summary>
        static float ClampToBounds(float value, float bound, float viewHalf)
        {
            float room = bound - viewHalf;
            return room <= 0f ? 0f : Mathf.Clamp(value, -room, room);
        }

        static float LongestGap(List<Vector2> points)
        {
            float longest = 0f;
            for (int i = 0; i < points.Count; i++)
                for (int j = i + 1; j < points.Count; j++)
                    longest = Mathf.Max(longest, (points[i] - points[j]).magnitude);
            return longest;
        }

        /// <summary>The average of the points; with three or more it leans toward the outermost ones, like Tanknarok's weighted average.</summary>
        Vector2 WeightedCentre(List<Vector2> points)
        {
            Vector2 average = Vector2.zero;
            for (int i = 0; i < points.Count; i++) average += points[i];
            average /= points.Count;
            if (points.Count < 3) return average;

            float farthest = 0f;
            for (int i = 0; i < points.Count; i++) farthest = Mathf.Max(farthest, (points[i] - average).magnitude);
            if (farthest < 0.01f) return average;

            Vector2 pull = Vector2.zero;
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 away = points[i] - average;
                pull += away * (away.magnitude / farthest * farthestPull);
            }
            return average + pull / points.Count;
        }

        /// <summary>Half the width and depth of ground the camera sees at this distance.</summary>
        Vector2 VisibleHalfSize(float distance)
        {
            float tanV = Mathf.Tan((m_Camera != null ? m_Camera.fieldOfView : 45f) * 0.5f * Mathf.Deg2Rad);
            float aspect = Mathf.Max(0.5f, (float)Screen.width / Mathf.Max(1, Screen.height));
            float pitch = Mathf.Max(0.3f, Mathf.Abs(m_Direction.y));               // a tilted view stretches the ground it sees: more depth than width
            return new Vector2(distance * tanV * aspect, distance * tanV / pitch);
        }
    }
}
