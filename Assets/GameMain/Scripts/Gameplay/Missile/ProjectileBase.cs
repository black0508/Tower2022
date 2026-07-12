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

        protected float lifetime;
        protected bool hasHitLocally;

        public override void OnStartClient()
        {
            if (!isServer)
                transform.position = startPos;
        }

        void Update()
        {
            lifetime += Time.deltaTime;
            if (lifetime > maxLifetime || Vector3.Distance(transform.position, startPos) > maxRange)
            {
                if (isServer) NetworkServer.Destroy(gameObject);
                return;
            }

            if (hasHitLocally) return;

            UpdateMovement();

            if (isServer && CheckServerHit())
            {
                RpcConfirmHit();
                NetworkServer.Destroy(gameObject);
            }
        }

        [Server]
        public void ServerLaunch(Enemy target, int launchDamage, float launchSpeed, Vector3 launchPos)
        {
            damage = launchDamage;
            speed = launchSpeed;
            startPos = launchPos;
            OnServerLaunch(target);
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
