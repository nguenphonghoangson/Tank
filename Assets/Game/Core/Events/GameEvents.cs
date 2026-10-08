using Tank.Core.Match;
using UnityEngine;

namespace Tank.Core.Events
{
    public readonly struct TankRenamed { public readonly int TankId; public readonly string Name; public TankRenamed(int id, string name) { TankId = id; Name = name; } }
    public readonly struct TankRemoved { public readonly int TankId; public TankRemoved(int id) { TankId = id; } }
    public readonly struct TankSpawned { public readonly int TankId; public TankSpawned(int id) { TankId = id; } }
    public readonly struct TankRespawned { public readonly int TankId; public TankRespawned(int id) { TankId = id; } }

    public readonly struct TankDamaged
    {
        public readonly int VictimId, AttackerId, Amount, HpLeft;
        public TankDamaged(int victim, int attacker, int amount, int hpLeft) { VictimId = victim; AttackerId = attacker; Amount = amount; HpLeft = hpLeft; }
    }

    public readonly struct TankKilled
    {
        public readonly int VictimId, KillerId;
        public readonly int[] AssistIds;
        public TankKilled(int victim, int killer, int[] assists) { VictimId = victim; KillerId = killer; AssistIds = assists; }
    }

    public readonly struct ProjectileFired
    {
        public readonly int ProjectileId, OwnerId, WeaponId; public readonly Vector2 Origin, Direction;
        public ProjectileFired(int id, int owner, int weapon, Vector2 origin, Vector2 direction) { ProjectileId = id; OwnerId = owner; WeaponId = weapon; Origin = origin; Direction = direction; }
    }

    public readonly struct ProjectileImpact
    {
        public readonly int ProjectileId, WeaponId; public readonly Vector2 Point; public readonly bool HitTank;
        public ProjectileImpact(int id, int weapon, Vector2 point, bool hitTank) { ProjectileId = id; WeaponId = weapon; Point = point; HitTank = hitTank; }
    }

    public readonly struct TankDashed
    {
        public readonly int TankId; public readonly Vector2 Position, Direction;
        public TankDashed(int id, Vector2 position, Vector2 direction) { TankId = id; Position = position; Direction = direction; }
    }

    public readonly struct CoreOffered
    {
        public readonly int TankId; public readonly int[] CoreIds; public readonly float Seconds;
        public CoreOffered(int tankId, int[] coreIds, float seconds) { TankId = tankId; CoreIds = coreIds; Seconds = seconds; }
    }
    public readonly struct CorePicked { public readonly int TankId, CoreId; public CorePicked(int tankId, int coreId) { TankId = tankId; CoreId = coreId; } }

    /// <summary>ItemId -1 means the slot is empty (waiting to respawn).</summary>
    public readonly struct ItemSlotChanged
    {
        public readonly int SlotId, ItemId; public readonly Vector2 Position;
        public ItemSlotChanged(int slotId, int itemId, Vector2 position) { SlotId = slotId; ItemId = itemId; Position = position; }
    }
    public readonly struct ItemTaken
    {
        public readonly int SlotId, TankId, ItemId; public readonly Vector2 Position;
        public ItemTaken(int slotId, int tankId, int itemId, Vector2 position) { SlotId = slotId; TankId = tankId; ItemId = itemId; Position = position; }
    }

    public readonly struct RankingChanged { }

    public readonly struct MatchPhaseChanged { public readonly MatchPhase Phase; public MatchPhaseChanged(MatchPhase phase) { Phase = phase; } }

    /// <summary>WinnerId is -1 for a draw.</summary>
    public readonly struct MatchEnded { public readonly int WinnerId; public MatchEnded(int winner) { WinnerId = winner; } }
}
