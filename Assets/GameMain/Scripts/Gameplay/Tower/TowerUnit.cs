using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace Tower
{
    public class TowerUnit : NetworkBehaviour
    {
        [Header("同步变量")]
        [SyncVar] public int ownerPlayerId = -1;
        [SyncVar] public int level = 1;
        [SyncVar] public int hp = 100;
        [SyncVar] public int maxHp = 100;

        [Header("属性配置")]
        public float attackInterval = 1f;
        public int damage = 25;
        public float projectileSpeed = 20f;

        [Header("引用")]
        public Transform firePoint;
        public GameObject projectilePrefab;

        [Header("运行状态")]
        [SerializeField] private List<Enemy> enemiesInRange = new();
        [SerializeField] private Enemy currentTarget;
        [SerializeField] private float attackTimer;

        void OnTriggerEnter(Collider c)
        {
            if (!isServer) return;
            if (c.TryGetComponent<Enemy>(out var e) && !enemiesInRange.Contains(e))
                enemiesInRange.Add(e);
        }

        void OnTriggerExit(Collider c)
        {
            if (!isServer) return;
            if (c.TryGetComponent<Enemy>(out var e))
                enemiesInRange.Remove(e);
        }

        void Update()
        {
            if (!isServer) return;

            enemiesInRange.RemoveAll(e => e == null || e.hp <= 0);

            if (currentTarget == null || currentTarget.hp <= 0 || !enemiesInRange.Contains(currentTarget))
                currentTarget = enemiesInRange.Count > 0 ? enemiesInRange[0] : null;

            if (currentTarget != null) {
                attackTimer += Time.deltaTime;
                if (attackTimer >= attackInterval) {
                    attackTimer = 0;
                    Fire(currentTarget);
                }
            }
        }

        [Server]
        void Fire(Enemy target)
        {
            var go = Instantiate(projectilePrefab, firePoint.position, Quaternion.identity);
            var proj = go.GetComponent<ProjectileBase>();
            if (proj == null)
            {
                Debug.LogError($"[Server] {projectilePrefab.name} missing ProjectileBase.");
                Destroy(go);
                return;
            }

            proj.ServerLaunch(target, damage, projectileSpeed, firePoint.position);
            NetworkServer.Spawn(go);
        }
    }
}
