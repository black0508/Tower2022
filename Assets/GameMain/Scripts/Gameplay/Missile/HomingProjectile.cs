using UnityEngine;

namespace Tower
{
    public class HomingProjectile : HomingProjectileBase
    {
        protected override bool CheckServerHit()
        {
            if (serverTarget == null) return false;
            if (Vector3.Distance(transform.position, serverTarget.transform.position) >= HitDistance)
                return false;

            if (serverTarget.IsAlive)
            {
                var info = new DamageInfo
                {
                    source = sourceNetIdentity,
                    target = serverTarget.netIdentity,
                    amount = damage,
                    type = DamageType.Physical,
                    tags = DamageTag.Direct,
                    critChance = 0.05f,
                };
                serverTarget.TakeDamage(ref info);
            }
            return true;
        }
    }
}
