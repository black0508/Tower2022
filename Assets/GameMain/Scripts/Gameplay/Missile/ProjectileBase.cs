using Mirror;
using UnityEngine;

namespace Tower
{
    public class ProjectileBase : NetworkBehaviour
    {
        [Header("网络变量")]
        [SyncVar] public uint targetNetId;
        [SyncVar] public Vector3 startPos;
        [SyncVar] public float speed = 20f;

        [Header("引用")]
        public MeshRenderer meshRenderer;
        public TrailRenderer trail;
        public GameObject hitEffectPrefab;

        [Header("边界配置")]
        public float maxLifetime = 5f;
        public float maxRange = 50f;

        [HideInInspector] public int damage;
        [HideInInspector] public Enemy serverTarget;

        private float lifetime;
        private bool hasHitLocally;
        private Vector3 lastKnownTargetPos;
        private bool hasLastKnownTargetPos;

        public override void OnStartClient()
        {
            if (!isServer)
                transform.position = startPos;
        }

        void Update()
        {
            // 兜底剩余生存时间
            lifetime += Time.deltaTime;
            if (lifetime > maxLifetime || Vector3.Distance(transform.position, startPos) > maxRange) {
                if (isServer) NetworkServer.Destroy(gameObject);
                return;
            }

            // 如果已经命中，则不进行追踪
            if (hasHitLocally) return;

            if (!TryGetTargetPos(out Vector3 targetPos)) {
                if (isServer) NetworkServer.Destroy(gameObject);
                return;
            }

            // 追踪目标位置
            // TODO: 后续使用DOTWEEN或者手写一个插值来平滑移动
            transform.position = Vector3.MoveTowards(
                transform.position, targetPos, speed * Time.deltaTime);

            var moveDir = targetPos - transform.position;
            if (moveDir.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(moveDir);

            // 到达
            if (Vector3.Distance(transform.position, targetPos) >= 0.3f) return;

            if (isServer) {
                if (serverTarget != null && serverTarget.hp > 0)
                    serverTarget.TakeDamage(damage);
                RpcConfirmHit();  // 通知所有客户端假命中
                NetworkServer.Destroy(gameObject);
            } else {
                HitVisually();  // 客户端本地预测
            }
        }

        bool TryGetTargetPos(out Vector3 pos)
        {
            if (isServer) {
                //服务器
                if (serverTarget != null && serverTarget.hp > 0) {
                    // 实时记录最终位置
                    lastKnownTargetPos = serverTarget.transform.position;
                    hasLastKnownTargetPos = true;
                    pos = lastKnownTargetPos;
                    return true;
                }
            } else if (NetworkClient.spawned.TryGetValue(targetNetId, out var identity)) {
                // 实时记录最终位置(客户端)
                lastKnownTargetPos = identity.transform.position;
                hasLastKnownTargetPos = true;
                pos = lastKnownTargetPos;
                return true;
            }

            if (hasLastKnownTargetPos) {
                pos = lastKnownTargetPos;
                return true;
            }

            pos = default;
            return false;
        }

        [ClientRpc]
        void RpcConfirmHit()
        {
            HitVisually();
        }

        void HitVisually()
        {
            if (hasHitLocally) return;
            hasHitLocally = true;

            // 命中后，隐藏Mesh和拖尾
            if (meshRenderer != null) meshRenderer.enabled = false;
            if (trail != null) trail.emitting = false;
            if (hitEffectPrefab != null)
                Instantiate(hitEffectPrefab, transform.position, Quaternion.identity);
        }
    }
}
