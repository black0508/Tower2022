using UnityEngine;

namespace Tower
{
    public class FrostProjectile : HomingProjectileBase
    {
        [Header("Frost Config")]
        public float aoeRadius = 2f;
        public float slowMultiplier = 0.6f;
        public float slowDuration = 3f;

        protected override bool CheckServerHit()
        {
            if (serverTarget == null) return false;
            if (Vector3.Distance(transform.position, serverTarget.transform.position) >= HitDistance)
                return false;

            var hits = Physics.OverlapSphere(transform.position, aoeRadius, ~0);
            foreach (var h in hits)
            {
                if (!h.TryGetComponent<Enemy>(out var enemy) || !enemy.IsAlive) continue;

                var info = new DamageInfo
                {
                    source = sourceNetIdentity,
                    target = enemy.netIdentity,
                    amount = damage,
                    type = DamageType.Physical,
                    tags = DamageTag.Direct,
                    critChance = 0.05f,
                };
                enemy.TakeDamage(ref info);
                // Day3: AddBuff(new SlowBuff())
            }
            return true;
        }
    }
}
