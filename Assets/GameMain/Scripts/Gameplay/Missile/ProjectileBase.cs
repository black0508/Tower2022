using Mirror;
using UnityEngine;

namespace Tower
{
    public abstract class ProjectileBase : NetworkBehaviour
    {
        [Header("网络变量")]
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
        [HideInInspector] public NetworkIdentity sourceNetIdentity;

        protected float lifetime;
        protected bool hasHitLocally;

        public override void OnStartClient()
        {
            // 只复位表现，不能走 ResetForSpawn：会清掉已同步的 SyncVar（如 targetNetId）
            ResetVisualState();
            if (!isServer)
                transform.position = startPos;
        }

        void Update()
        {
            lifetime += Time.deltaTime;
            if (lifetime > maxLifetime || Vector3.Distance(transform.position, startPos) > maxRange)
            {
                if (isServer) GameEntry.NetworkPool.ServerDespawn(gameObject);
                return;
            }

            if (hasHitLocally) return;

            UpdateMovement();

            if (isServer && CheckServerHit())
            {
                RpcConfirmHit();
                GameEntry.NetworkPool.ServerDespawn(gameObject);
            }
        }

        [Server]
        public void ServerLaunch(Enemy target, int launchDamage, float launchSpeed, Vector3 launchPos, NetworkIdentity source)
        {
            ResetForSpawn();
            damage = launchDamage;
            speed = launchSpeed;
            startPos = launchPos;
            sourceNetIdentity = source;
            OnServerLaunch(target);
        }

        /// <summary>客户端/复用时复位表现（mesh、拖尾、lifetime）。</summary>
        protected virtual void ResetVisualState()
        {
            lifetime = 0f;
            hasHitLocally = false;
            if (meshRenderer != null) meshRenderer.enabled = true;
            if (trail != null)
            {
                trail.Clear();
                trail.emitting = true;
            }
        }

        /// <summary>服务端发射前完整复位（含追踪缓存 / SyncVar 占位）。</summary>
        protected virtual void ResetForSpawn()
        {
            ResetVisualState();
        }

        protected abstract void OnServerLaunch(Enemy target);
        protected abstract void UpdateMovement();
        protected abstract bool CheckServerHit();

        [ClientRpc]
        void RpcConfirmHit() => HitVisually();

        protected void HitVisually()
        {
            if (hasHitLocally) return;
            hasHitLocally = true;
            OnHitVisually();
        }

        protected virtual void OnHitVisually()
        {
            if (meshRenderer != null) meshRenderer.enabled = false;
            if (trail != null) trail.emitting = false;
            if (hitEffectPrefab != null)
                Instantiate(hitEffectPrefab, transform.position, Quaternion.identity);
        }
    }
}
