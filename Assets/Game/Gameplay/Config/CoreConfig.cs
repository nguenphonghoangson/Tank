using System;
using Tank.Core.Cores;
using UnityEngine;

namespace Tank.Gameplay.Config
{
    /// <summary>Every core: its name, text and colour for the pick screen, and the stat changes it makes. It is the ICoreCatalog the core reads.</summary>
    [CreateAssetMenu(menuName = "Tank/Config/Cores", fileName = "CoreConfig")]
    public sealed class CoreConfig : ScriptableObject, ICoreCatalog
    {
        [Serializable]
        public sealed class Entry
        {
            public string name = "Core";
            [TextArea] public string description;
            public Color color = Color.white;
            [Header("Changes (multipliers start at 1, bonuses at 0)")]
            public int maxHpBonus;
            public float speedMult = 1f, damageMult = 1f, fireIntervalMult = 1f, ammoMult = 1f, dashCooldownMult = 1f;
            public float splashBonus, lifesteal, regenPerSecond;
        }

        public Entry[] cores = new Entry[0];

        public int Count => cores.Length;
        public Entry Presentation(int id) { return cores[Mathf.Clamp(id, 0, cores.Length - 1)]; }

        public CoreSpec Get(int id)
        {
            Entry e = cores[id];
            var m = new CoreModifiers
            {
                MaxHpBonus = e.maxHpBonus, SpeedMult = e.speedMult, DamageMult = e.damageMult, FireIntervalMult = e.fireIntervalMult, AmmoMult = e.ammoMult,
                DashCooldownMult = e.dashCooldownMult, SplashBonus = e.splashBonus, Lifesteal = e.lifesteal, RegenPerSecond = e.regenPerSecond,
            };
            return new CoreSpec(id, e.name, e.description, m);
        }
    }
}
