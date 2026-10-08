using System.Collections.Generic;
using TankGame.Prototype;

namespace TankGame.NetSpike
{
    /// <summary>
    /// The prototype's core pool (CoreLibrary) for the networked match. A tank's cores are one bit each in a ushort that rides in
    /// TankState and every snapshot, so the client predicts with exactly the stats the server uses.
    /// </summary>
    public static class SpikeCores
    {
        public const int Phases = 4;
        public const float OfferSeconds = 12f;       // an unanswered offer picks at random after this long
        public static int Count => CoreLibrary.All.Length;

        static readonly Dictionary<ushort, TankModifiers> s_Cache = new Dictionary<ushort, TankModifiers>();

        /// <summary>The summed modifiers of every core in the mask (cached: the sim asks every tick).</summary>
        public static TankModifiers Mods(ushort mask)
        {
            if (s_Cache.TryGetValue(mask, out TankModifiers m)) return m;
            m = new TankModifiers();
            for (int i = 0; i < Count; i++) if ((mask & (1 << i)) != 0) m.Add(CoreLibrary.All[i].delta);
            s_Cache[mask] = m;
            return m;
        }

        public static bool Has(ushort mask, int core) { return (mask & (1 << core)) != 0; }
    }
}
