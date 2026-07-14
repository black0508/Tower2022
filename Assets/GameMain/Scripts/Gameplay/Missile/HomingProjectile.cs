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

            if (serverTarget.hp > 0)
                serverTarget.TakeDamage(damage);
            return true;
        }
    }
}
