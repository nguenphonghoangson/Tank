using UnityEngine;

namespace TankGame.Prototype
{
    /// <summary>Short burst of speed in the move direction (or the hull heading when standing still).</summary>
    public sealed class DashSkill : TankSkill
    {
        public float speed = 36f;
        public float duration = 0.22f;

        void Reset() { displayName = "Dash"; }

        public override void Tick(TankUnit unit, bool pressed)
        {
            if (CooldownRemaining > 0f) CooldownRemaining -= Time.deltaTime;
            if (!pressed || CooldownRemaining > 0f) return;

            Vector2 mv = unit.Command.Move;
            Vector3 dir = mv.sqrMagnitude > 0.01f ? new Vector3(mv.x, 0f, mv.y).normalized : unit.transform.forward;
            unit.StartDash(dir, speed, duration);
            CooldownRemaining = cooldown;
        }
    }
}
