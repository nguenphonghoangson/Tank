using Tank.Core.Combat;
using Tank.Core.Movement;
using UnityEngine;

namespace Tank.Gameplay.Config
{
    /// <summary>Tuning for one tank. Designers edit this asset; the simulation only ever sees the Core settings it converts to.</summary>
    [CreateAssetMenu(menuName = "Tank/Config/Tank", fileName = "TankConfig")]
    public sealed class TankConfig : ScriptableObject
    {
        [Header("Health")]
        public int maxHp = 100;
        public float respawnSeconds = 2.5f;

        [Header("Movement (direct drive)")]
        public float maxSpeed = 13f;
        public float acceleration = 100f;
        public float braking = 7f;
        public float rotationSpeed = 15f;

        [Header("Combat")]
        public float turretTurnSpeed = 720f;
        // collision radius, hit radius and muzzle position are not here: they live on the tank prefab's TankHitbox, where they can be seen

        [Header("Item buffs")]
        public float speedBuffMultiplier = 1.4f;
        public float damageBuffMultiplier = 1.5f;

        [Header("Dash")]
        public float dashSpeed = 28f;
        public float dashDuration = 0.22f;
        public float dashCooldown = 4f;

        public MovementSettings ToMovement(Tank.Gameplay.Views.TankHitbox hitbox) { return new MovementSettings(maxSpeed, acceleration, braking, rotationSpeed, hitbox.collisionRadius); }
        public TankSettings ToTank(Tank.Gameplay.Views.TankHitbox hitbox) { return new TankSettings(maxHp, turretTurnSpeed, dashSpeed, dashDuration, dashCooldown, hitbox.hitRadius, hitbox.MuzzleForward, respawnSeconds, speedBuffMultiplier, damageBuffMultiplier); }
    }
}
