using UnityEngine;

namespace Tank.Core.Cores
{
    /// <summary>What a set of cores does to a tank. Multipliers start at 1, bonuses at 0, and Add combines two sets.</summary>
    public struct CoreModifiers
    {
        public int MaxHpBonus;
        public float SpeedMult, DamageMult, FireIntervalMult, AmmoMult, DashCooldownMult;
        public float SplashBonus, Lifesteal, RegenPerSecond;

        public static CoreModifiers Identity
        {
            get { return new CoreModifiers { SpeedMult = 1f, DamageMult = 1f, FireIntervalMult = 1f, AmmoMult = 1f, DashCooldownMult = 1f }; }
        }

        public void Add(in CoreModifiers d)
        {
            MaxHpBonus += d.MaxHpBonus;
            SpeedMult *= d.SpeedMult; DamageMult *= d.DamageMult; FireIntervalMult *= d.FireIntervalMult; AmmoMult *= d.AmmoMult; DashCooldownMult *= d.DashCooldownMult;
            SplashBonus += d.SplashBonus; Lifesteal += d.Lifesteal; RegenPerSecond += d.RegenPerSecond;
        }
    }

    public readonly struct CoreSpec
    {
        public readonly int Id;
        public readonly string Name, Description;
        public readonly CoreModifiers Delta;
        public CoreSpec(int id, string name, string description, CoreModifiers delta) { Id = id; Name = name; Description = description; Delta = delta; }
    }

    /// <summary>Where core numbers come from. Gameplay backs it with a ScriptableObject; tests with an array.</summary>
    public interface ICoreCatalog
    {
        int Count { get; }
        CoreSpec Get(int id);
    }

    public sealed class ArrayCoreCatalog : ICoreCatalog
    {
        readonly CoreSpec[] m_Specs;
        public ArrayCoreCatalog(CoreSpec[] specs) { m_Specs = specs; }
        public int Count => m_Specs.Length;
        public CoreSpec Get(int id) { return m_Specs[id]; }
    }

    /// <summary>The cores a tank has are one bit each in a ushort, which is small enough to ride in every snapshot.</summary>
    public static class CoreMask
    {
        public static bool Has(ushort mask, int core) { return (mask & (1 << core)) != 0; }
        public static ushort With(ushort mask, int core) { return (ushort)(mask | (1 << core)); }

        public static int Count(ushort mask)
        {
            int n = 0;
            for (int m = mask; m != 0; m &= m - 1) n++;
            return n;
        }

        public static CoreModifiers Combine(ICoreCatalog catalog, ushort mask)
        {
            CoreModifiers sum = CoreModifiers.Identity;
            for (int i = 0; i < catalog.Count && i < 16; i++) if (Has(mask, i)) sum.Add(catalog.Get(i).Delta);
            return sum;
        }
    }
}
