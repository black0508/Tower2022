using Mirror;
using UnityEngine;

namespace Tower
{
    public static class DamageService
    {
        const float CritMultiplier = 1.8f;

        [Server]
        public static void Apply(ref DamageInfo info)
        {
            if (info.target == null) return;

            var targetAttrs = info.target.GetComponent<AttributeComponent>();
            if (targetAttrs == null || !targetAttrs.IsAlive) return;

            var dstHolder = info.target.GetComponent<BuffHolder>();

            var state = GameEntry.State;
            if (state != null)
            {
                var ids = state.activeMutationIds;
                for (int i = 0; i < ids.Count; i++)
                {
                    var mut = MutationRegistry.Get(ids[i]);
                    if (mut == null) continue;
                    mut.OnDamageDealt(ref info);
                    if (mut.Matches(info.target)) mut.OnDamageTaken(ref info);
                }
                if (info.cancelled) return;
            }

            if (info.source != null)
            {
                var srcHolder = info.source.GetComponent<BuffHolder>();
                if (srcHolder != null)
                    foreach (var buff in srcHolder.GetSortedServerBuffs())
                        buff.OnDamageDealt(ref info);
                if (info.cancelled) return;
            }

            if (dstHolder != null)
                foreach (var buff in dstHolder.GetSortedServerBuffs())
                    buff.OnDamageTaken(ref info);
            if (info.cancelled) return;

            if (info.IsHeal)
            {
                targetAttrs.ModHp(Mathf.Abs(info.amount));
                return;
            }

            float finalDamage = Mathf.Abs(info.amount);
            if (info.critChance > 0f && Random.value <= info.critChance)
            {
                info.isCritical = true;
                finalDamage *= CritMultiplier;
            }

            bool wouldKill = finalDamage >= targetAttrs.CurrentHp;
            if (wouldKill)
            {
                if (info.source != null)
                {
                    var srcHolder = info.source.GetComponent<BuffHolder>();
                    if (srcHolder != null)
                        foreach (var buff in srcHolder.GetSortedServerBuffs())
                            buff.OnKill(ref info);
                }
                if (dstHolder != null)
                    foreach (var buff in dstHolder.GetSortedServerBuffs())
                        buff.OnBeKilled(ref info);

                if (info.cancelled) return;
            }

            targetAttrs.ModHp(-finalDamage);

            if (targetAttrs.CurrentHp <= 0f)
            {
                info.killed = true;
                info.target.GetComponent<ICombatEntity>()?.OnFatalHit(ref info);
            }
        }
    }
}
