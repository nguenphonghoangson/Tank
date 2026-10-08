using System.Collections.Generic;
using Tank.Core.Combat;
using Tank.Core.Common;
using Tank.Core.Events;
using Tank.Core.Match;
using UnityEngine;

namespace Tank.Core.Cores
{
    public readonly struct CoreSettings
    {
        public readonly int Phases;
        public readonly float OfferSeconds;
        public CoreSettings(int phases, float offerSeconds) { Phases = phases; OfferSeconds = offerSeconds; }
    }

    public sealed class CoreOffer
    {
        public int TankId; public int[] CoreIds; public float ExpiresAt;
    }

    /// <summary>Who may pick, and how: a person chooses, a bot is picked for at once.</summary>
    public interface ICoreOfferPolicy { bool PicksAutomatically(TankModel tank); }

    public sealed class BotsPickAutomatically : ICoreOfferPolicy { public bool PicksAutomatically(TankModel tank) { return tank.IsBot; } }

    /// <summary>What the player's own screen may ask: pick one of the cores on offer.</summary>
    public interface ICorePicker { void Pick(int tankId, int offerIndex); }

    /// <summary>
    /// The core system: each phase of the match every tank is owed one core and is offered three it does not have yet. A pick (or, when
    /// the time runs out, a random one) adds it to the tank. "Owed" is derived (phases so far minus cores owned), so a player who joins late
    /// or a restart needs no special handling.
    /// </summary>
    public sealed class CoreOfferSystem : ITickable, ICorePicker
    {
        readonly ICoreCatalog m_Catalog;
        readonly ITankRegistry m_Tanks;
        readonly IEventBus m_Bus;
        readonly IClock m_Clock;
        readonly IRandom m_Random;
        readonly ICoreOfferPolicy m_Policy;
        readonly MatchSettings m_Match;
        readonly CoreSettings m_Settings;
        readonly Dictionary<int, CoreOffer> m_Offers = new Dictionary<int, CoreOffer>();
        readonly List<int> m_Pool = new List<int>();
        bool m_Running; float m_StartedAt;

        public CoreOfferSystem(ICoreCatalog catalog, ITankRegistry tanks, IEventBus bus, IClock clock, IRandom random, ICoreOfferPolicy policy, MatchSettings match, CoreSettings settings)
        {
            m_Catalog = catalog; m_Tanks = tanks; m_Bus = bus; m_Clock = clock; m_Random = random; m_Policy = policy; m_Match = match; m_Settings = settings;
            bus.Subscribe<MatchPhaseChanged>(OnPhase);
        }

        public int CurrentPhase
        {
            get
            {
                if (!m_Running) return -1;
                float phaseLength = m_Match.DurationSeconds / Mathf.Max(1, m_Settings.Phases);
                return Mathf.Clamp((int)((m_Clock.Now - m_StartedAt) / phaseLength), 0, m_Settings.Phases - 1);
            }
        }

        void OnPhase(MatchPhaseChanged e)
        {
            if (e.Phase == MatchPhase.Playing) { m_Running = true; m_StartedAt = m_Clock.Now; }
            else m_Running = false;
            if (e.Phase == MatchPhase.Warmup) ResetAll();
        }

        void ResetAll()
        {
            m_Offers.Clear();
            for (int i = 0; i < m_Tanks.All.Count; i++) m_Tanks.All[i].SetCores(0, m_Catalog);
        }

        public void Tick(float dt)
        {
            if (!m_Running) return;
            int phase = CurrentPhase;
            for (int i = 0; i < m_Tanks.All.Count; i++)
            {
                TankModel t = m_Tanks.All[i];
                if (m_Offers.TryGetValue(t.Id, out CoreOffer offer))
                {
                    if (m_Clock.Now >= offer.ExpiresAt) Apply(t, offer.CoreIds[RandomIndex(offer.CoreIds.Length)]);
                    continue;
                }
                if (CoreMask.Count(t.Cores) >= phase + 1 || CoreMask.Count(t.Cores) >= m_Catalog.Count) continue;     // nothing owed
                CoreOffer made = MakeOffer(t);
                if (made == null) continue;
                m_Offers[t.Id] = made;
                m_Bus.Publish(new CoreOffered(t.Id, made.CoreIds, m_Settings.OfferSeconds));
                if (m_Policy.PicksAutomatically(t)) Apply(t, made.CoreIds[RandomIndex(made.CoreIds.Length)]);
            }
        }

        int RandomIndex(int count) { return Mathf.Min(count - 1, (int)(m_Random.Value() * count)); }

        public void Pick(int tankId, int offerIndex)
        {
            if (!m_Offers.TryGetValue(tankId, out CoreOffer offer) || offerIndex < 0 || offerIndex >= offer.CoreIds.Length) return;
            TankModel t = m_Tanks.Find(tankId);
            if (t != null) Apply(t, offer.CoreIds[offerIndex]);
        }

        CoreOffer MakeOffer(TankModel t)
        {
            m_Pool.Clear();
            for (int i = 0; i < m_Catalog.Count && i < 16; i++) if (!CoreMask.Has(t.Cores, i)) m_Pool.Add(i);
            if (m_Pool.Count == 0) return null;
            for (int i = m_Pool.Count - 1; i > 0; i--) { int j = RandomIndex(i + 1); int tmp = m_Pool[i]; m_Pool[i] = m_Pool[j]; m_Pool[j] = tmp; }
            int n = Mathf.Min(3, m_Pool.Count);
            var ids = new int[n];
            for (int i = 0; i < n; i++) ids[i] = m_Pool[i];
            return new CoreOffer { TankId = t.Id, CoreIds = ids, ExpiresAt = m_Clock.Now + m_Settings.OfferSeconds };
        }

        void Apply(TankModel t, int coreId)
        {
            m_Offers.Remove(t.Id);
            t.SetCores(CoreMask.With(t.Cores, coreId), m_Catalog);
            m_Bus.Publish(new CorePicked(t.Id, coreId));
        }
    }
}
