using UnityEngine;

namespace TankGame.Prototype
{
    /// <summary>
    /// Data for one weapon. A tank owns one WeaponDef; new weapons are new assets, not new tank code.
    /// Projectile behaviour is straight-line only for now (see Projectile).
    /// </summary>
    [CreateAssetMenu(menuName = "Tank/Weapon", fileName = "Weapon")]
    public sealed class WeaponDef : ScriptableObject
    {
        public string displayName = "Cannon";

        [Header("Damage")]
        public int damage = 25;
        public float splashRadius = 0f;
        [Range(0f, 1f)] public float splashDamageFactor = 0.5f;

        [Header("Fire")]
        public float fireInterval = 0.33f;
        public float projectileSpeed = 45f;
        public float range = 55f;
        public float recoilSpeed = 1.6f;

        [Header("Spread")]
        public int pellets = 1;
        public float spreadDegrees = 0f;

        [Header("Special weapon (picked up on the map)")]
        public bool limitedAmmo = false;    // magazineSize is the total shots, no reload; the tank falls back to its primary weapon when empty

        [Header("Look")]
        public Vector3 projectileScale = Vector3.one;
        public Color projectileColor = new Color(1f, 0.92f, 0.5f, 1f);

        [Header("Ammo (reserve is unlimited in the prototype)")]
        public int magazineSize = 10;
        public float reloadSeconds = 1.6f;

        public float ProjectileLife => projectileSpeed > 0f ? range / projectileSpeed : 1f;
    }
}
