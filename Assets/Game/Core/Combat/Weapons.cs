using System.Collections.Generic;

namespace Tank.Core.Combat
{
    public readonly struct WeaponSpec
    {
        public readonly int Id;
        public readonly string Name;
        public readonly int Damage, Pellets, Ammo;                       // Ammo 0 = unlimited
        public readonly float Interval, ProjectileSpeed, Range, SpreadDegrees, SplashRadius, SplashFactor;

        public WeaponSpec(int id, string name, int damage, float interval, float projectileSpeed, float range, int pellets, float spreadDegrees, int ammo, float splashRadius, float splashFactor)
        {
            Id = id; Name = name; Damage = damage; Interval = interval; ProjectileSpeed = projectileSpeed; Range = range;
            Pellets = pellets; SpreadDegrees = spreadDegrees; Ammo = ammo; SplashRadius = splashRadius; SplashFactor = splashFactor;
        }
    }

    /// <summary>Where weapon numbers come from. Gameplay backs it with a ScriptableObject; tests with an array.</summary>
    public interface IWeaponCatalog
    {
        int Count { get; }
        WeaponSpec Get(int id);
    }

    public sealed class ArrayWeaponCatalog : IWeaponCatalog
    {
        readonly WeaponSpec[] m_Specs;
        public ArrayWeaponCatalog(IEnumerable<WeaponSpec> specs) { m_Specs = new List<WeaponSpec>(specs).ToArray(); }
        public int Count => m_Specs.Length;
        public WeaponSpec Get(int id) { return m_Specs[id]; }
    }
}
