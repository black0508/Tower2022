using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace Tower
{
    [RequireComponent(typeof(BuffHolder))]
    [RequireComponent(typeof(AttributeComponent))]
    public class TowerUnit : NetworkBehaviour
    {
        [Header("同步变量")]
        [SyncVar] public int towerConfigId;
        [SyncVar] public int ownerPlayerId = -1;
        [SyncVar] public int level = 1;

        [Header("引用")]
        public Transform firePoint;
        public GameObject projectilePrefab;

        [SerializeField] List<Enemy> enemiesInRange = new();
        [SerializeField] Enemy currentTarget;
        [SerializeField] float attackTimer;

        AttributeComponent attribute;

        public AttributeComponent Attribute => attribute;

        void Awake() => attribute = GetComponent<AttributeComponent>();

        public override void OnStartServer()
        {
            ServerApplyLevel(level);
            attribute.InitHpFull();
        }

        /// <summary>应用指定等级的配置效果，最后统一 Recalculate 一次。升级唯一扩展点。</summary>
        [Server]
        public void ServerApplyLevel(int lvl)
        {
            var cfg = GameEntry.GameConfig?.TowerConfig;
            if (cfg != null && cfg.TryGetLevel(towerConfigId, lvl, out var levelDef) && levelDef.effects != null)
            {
                foreach (var eff in levelDef.effects)
                    eff?.Apply(this);
            }
            attribute.Recalculate();
        }

        /// <summary>升一级并应用该级效果（保留当前 HP，Recalculate 内已 Clamp）。</summary>
        [Server]
        public void ServerUpgrade()
        {
            level++;
            ServerApplyLevel(level);
        }

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

            enemiesInRange.RemoveAll(e => e == null || !e.IsAlive);

            if (currentTarget == null || !currentTarget.IsAlive || !enemiesInRange.Contains(currentTarget))
                currentTarget = enemiesInRange.Count > 0 ? enemiesInRange[0] : null;

            if (currentTarget == null) return;

            attackTimer += Time.deltaTime;
            float interval = attribute.GetFinal(AttributeKey.AttackInterval);
            if (interval <= 0f) interval = 1f;
            if (attackTimer >= interval)
            {
                attackTimer = 0f;
                Fire(currentTarget);
            }
        }

        [Server]
        void Fire(Enemy target)
        {
            int damage = Mathf.RoundToInt(attribute.GetFinal(AttributeKey.Damage));
            float projSpeed = attribute.GetFinal(AttributeKey.ProjectileSpeed);
            var source = netIdentity;

            GameEntry.NetworkPool.ServerSpawn(
                projectilePrefab, firePoint.position, Quaternion.identity,
                beforeSpawn: go =>
                {
                    var proj = go.GetComponent<ProjectileBase>();
                    if (proj == null)
                    {
                        Debug.LogError($"[Server] {projectilePrefab.name} missing ProjectileBase.");
                        return;
                    }
                    proj.ServerLaunch(target, damage, projSpeed, firePoint.position, source);
                });
        }
    }
}
