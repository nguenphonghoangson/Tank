using UnityEngine;

namespace Tank.Gameplay.Views
{
    /// <summary>What the rest of the game may ask of a tank on screen.</summary>
    public interface ITankView
    {
        void Present(Vector2 position, float hullYaw, float turretYaw);
        void SetColor(Material playerMaterial);
        void SetAlive(bool alive);
        void SetHealth01(float value);
        void SetStatus(bool shield, bool speed, bool damage);
        void SetName(string playerName, Color color);
        void SetLocal(bool local);
    }

    /// <summary>
    /// The tank on screen, assembled from components that each do one thing: motion, model, colour, health bar, hitbox. This class only
    /// forwards to them, so any one can be changed or replaced without touching the others.
    /// </summary>
    [RequireComponent(typeof(TankMotionView), typeof(TankColorView), typeof(TankHitbox))]
    public sealed class TankView : MonoBehaviour, ITankView
    {
        [SerializeField] TankMotionView motion;
        [SerializeField] TankVisual visual;
        [SerializeField] TankColorView color;
        [SerializeField] TankHealthBarView healthBar;
        [SerializeField] TankHitbox hitbox;
        [SerializeField] TankDamageView damage;
        [SerializeField] TankDustView dust;
        [SerializeField] TankStatusView status;
        [SerializeField] TankNameView nameView;
        [SerializeField] TankLocalMarker localMarker;
        bool m_Local; Color m_Tint = Color.white;

        public TankHitbox Hitbox => hitbox;

        public void Present(Vector2 position, float hullYaw, float turretYaw) { motion.Present(position, hullYaw, turretYaw); }
        public void SetColor(Material playerMaterial) { color.Apply(playerMaterial); }
        public void SetHealth01(float value)
        {
            if (healthBar != null) healthBar.Set01(value);
            if (damage != null) { int stage = damage.SetHealth01(value); if (dust != null) dust.SetStage(stage); }
        }

        public void SetName(string playerName, Color color)
        {
            m_Tint = color;
            if (nameView != null) nameView.Set(playerName, color);
            if (localMarker != null) localMarker.SetColor(Color.Lerp(color, Color.white, 0.35f));
        }

        /// <summary>The tank the player drives gets a ground ring and a larger name.</summary>
        public void SetLocal(bool local)
        {
            if (m_Local == local) return;
            m_Local = local;
            if (localMarker != null) localMarker.Show(local);
            if (nameView != null) nameView.SetEmphasis(local);
        }

        public void SetStatus(bool shield, bool speed, bool damageBoost) { if (status != null) status.SetStatus(shield, speed, damageBoost); }

        public void SetAlive(bool alive)
        {
            visual.Show(alive);
            if (healthBar != null) healthBar.Show(alive);
            if (dust != null) dust.Show(alive);
            if (nameView != null) nameView.Show(alive);
            if (!alive && status != null) status.Hide();
        }
    }
}
