using System;
using Tank.Core.Combat;
using UnityEngine;

namespace Tank.Gameplay.Config
{
    /// <summary>
    /// Every weapon in one asset: the numbers the rules use plus the look and sound of the shot. It is the IWeaponCatalog the core
    /// reads, so the core never learns what a ScriptableObject is.
    /// </summary>
    [CreateAssetMenu(menuName = "Tank/Config/Weapons", fileName = "WeaponConfig")]
    public sealed class WeaponConfig : ScriptableObject, IWeaponCatalog
    {
        [Serializable]
        public sealed class Entry
        {
            public string name = "Weapon";
            [Header("Rules")]
            public int damage = 25;
            public float interval = 0.35f;
            public float projectileSpeed = 40f;
            public float range = 45f;
            public int pellets = 1;
            public float spreadDegrees;
            [Tooltip("0 = unlimited (the default weapon).")] public int ammo;
            public float splashRadius;
            [Range(0f, 1f)] public float splashFactor = 0.6f;
            [Header("Presentation")]
            public GameObject projectilePrefab;
            public GameObject muzzleVfx;
            public GameObject impactVfx;
            public SfxCue shootSfx;
            public SfxCue impactSfx;
        }

        public Entry[] weapons = new Entry[0];

        public int Count => weapons.Length;
        public Entry Presentation(int id) { return weapons[Mathf.Clamp(id, 0, weapons.Length - 1)]; }

        public WeaponSpec Get(int id)
        {
            Entry e = weapons[id];
            return new WeaponSpec(id, e.name, e.damage, e.interval, e.projectileSpeed, e.range, Mathf.Max(1, e.pellets), e.spreadDegrees, e.ammo, e.splashRadius, e.splashFactor);
        }
    }
}
