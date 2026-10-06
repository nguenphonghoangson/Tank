using UnityEngine;

namespace TankGame.Prototype
{
    /// <summary>
    /// One active skill on a tank: cooldown plus an effect. Deliberately not a generic ability framework;
    /// a new skill is a new small subclass.
    /// </summary>
    public abstract class TankSkill : MonoBehaviour
    {
        public string displayName = "Skill";
        public float cooldown = 6f;

        public float CooldownRemaining { get; protected set; }
        public float CurrentCooldown { get; protected set; }
        public float Ready01 => CurrentCooldown > 0f ? 1f - Mathf.Clamp01(CooldownRemaining / CurrentCooldown) : 1f;

        public abstract void Tick(TankUnit unit, bool pressed);
        public virtual void ResetSkill() { CooldownRemaining = 0f; }
    }
}
