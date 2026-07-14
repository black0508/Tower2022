using Mirror;
using UnityEngine;

namespace Tower
{
    /// <summary>
    /// 追踪型子弹的通用基类：处理目标查询、移动、朝向、本地预测命中。
    /// 子类只需实现 CheckServerHit（服务端命中判定与效果）。
    /// </summary>
    public abstract class HomingProjectileBase : ProjectileBase
    {
        protected const float HitDistance = 0.3f;

        [SyncVar] public uint targetNetId;

        [HideInInspector] public Enemy serverTarget;

        Vector3 lastKnownTargetPos;
        bool hasLastKnownTargetPos;

        protected override void OnServerLaunch(Enemy target)
        {
            targetNetId = target.netIdentity.netId;
            serverTarget = target;
        }

        protected override void UpdateMovement()
        {
            if (!TryGetTargetPos(out Vector3 targetPos))
            {
                if (isServer) NetworkServer.Destroy(gameObject);
                return;
            }

            transform.position = Vector3.MoveTowards(
                transform.position, targetPos, speed * Time.deltaTime);

            var moveDir = targetPos - transform.position;
            if (moveDir.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(moveDir);

            if (!isServer && Vector3.Distance(transform.position, targetPos) < HitDistance)
                HitVisually();
        }

        protected bool TryGetTargetPos(out Vector3 pos)
        {
            if (isServer)
            {
                if (serverTarget != null && serverTarget.hp > 0)
                {
                    lastKnownTargetPos = serverTarget.transform.position;
                    hasLastKnownTargetPos = true;
                    pos = lastKnownTargetPos;
                    return true;
                }
            }
            else if (NetworkClient.spawned.TryGetValue(targetNetId, out var identity))
            {
                lastKnownTargetPos = identity.transform.position;
                hasLastKnownTargetPos = true;
                pos = lastKnownTargetPos;
                return true;
            }

            if (hasLastKnownTargetPos)
            {
                pos = lastKnownTargetPos;
                return true;
            }

            pos = default;
            return false;
        }
    }
}
