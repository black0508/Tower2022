using Mirror;
using UnityEngine;
using UnityEngine.AI;

namespace Tower
{
    [RequireComponent(typeof(BuffHolder))]
    [RequireComponent(typeof(AttributeComponent))]
    [RequireComponent(typeof(NavMeshAgent))]
    public class Enemy : NetworkBehaviour, ICombatEntity
    {
        [Header("Visual")]
        [SerializeField] MeshRenderer meshRenderer;
        Color originalColor;

        [Header("Path")]
        [SerializeField] string pathEndTag = "Tower.Path.End";
        [SerializeField] float reachBaseDistance = 1f;

        BuffHolder buffHolder;
        AttributeComponent attribute;
        NavMeshAgent agent;
        Transform pathEnd;
        bool reachedBase;
        bool dead;

        public bool IsAlive => attribute != null && attribute.IsAlive && !dead;

        void Awake()
        {
            buffHolder = GetComponent<BuffHolder>();
            attribute = GetComponent<AttributeComponent>();
            agent = GetComponent<NavMeshAgent>();
            CacheOriginalColor();
        }

        public override void OnStartServer()
        {
            BindSpeedAttribute();
            InitCombatStats();
            SetupPathfinding();
        }

        public override void OnStopServer()
        {
            UnbindSpeedAttribute();
        }

        public override void OnStartClient()
        {
            if (!isServer)
            {
                agent.enabled = false;
                BindSpeedAttribute();
            }
            CacheOriginalColor();
        }

        public override void OnStopClient()
        {
            if (!isServer)
                UnbindSpeedAttribute();
        }

        void Update()
        {
            if (!isServer || dead) return;
            if (HasReachedBase())
                ReachBase();
        }

        void BindSpeedAttribute()
        {
            var speed = attribute.Get(AttributeKey.Speed);
            if (speed == null) return;
            speed.OnValueChanged -= OnSpeedChanged;
            speed.OnValueChanged += OnSpeedChanged;
            ApplySpeed(speed.Value);
        }

        void UnbindSpeedAttribute()
        {
            var speed = attribute?.Get(AttributeKey.Speed);
            if (speed != null)
                speed.OnValueChanged -= OnSpeedChanged;
        }

        void InitCombatStats()
        {
            attribute.Recalculate();
            attribute.InitHpFull();
            ApplySpeed(attribute.GetFinal(AttributeKey.Speed));
        }

        void SetupPathfinding()
        {
            var pathEndGo = GameObject.FindWithTag(pathEndTag);
            if (pathEndGo == null)
            {
                Debug.LogError($"[Server] Path end not found! Tag '{pathEndTag}'.");
                return;
            }

            pathEnd = pathEndGo.transform;
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                agent.Warp(hit.position);
            agent.destination = pathEnd.position;
        }

        bool HasReachedBase() =>
            !reachedBase && pathEnd != null &&
            Vector3.Distance(transform.position, pathEnd.position) < reachBaseDistance;

        void OnSpeedChanged(float value) => ApplySpeed(value);

        void ApplySpeed(float value)
        {
            if (agent != null)
                agent.speed = value;
        }

        void CacheOriginalColor()
        {
            if (meshRenderer != null)
                originalColor = meshRenderer.material.color;
        }

        [Server]
        void ReachBase()
        {
            reachedBase = true;
            dead = true;
            int baseDmg = Mathf.RoundToInt(attribute.GetFinal(AttributeKey.BaseDamage));
            GameEntry.Event.Fire(this, EnemyRemovedEventArgs.Create(
                EnemyRemoveReason.ReachedBase,
                gold: 0,
                baseDmg: baseDmg,
                pos: transform.position,
                netId: netId));
            NetworkServer.Destroy(gameObject);
        }

        [Server]
        public void TakeDamage(int dmg)
        {
            var info = new DamageInfo
            {
                source = null,
                target = netIdentity,
                amount = dmg,
                type = DamageType.Physical,
                tags = DamageTag.Direct,
            };
            DamageService.Apply(ref info);
        }

        [Server]
        public void TakeDamage(ref DamageInfo info)
        {
            info.target = netIdentity;
            DamageService.Apply(ref info);
        }

        [Server]
        public void OnFatalHit(ref DamageInfo info)
        {
            if (dead) return;
            dead = true;
            int gold = Mathf.RoundToInt(attribute.GetFinal(AttributeKey.GoldReward));
            GameEntry.Event.Fire(this, EnemyRemovedEventArgs.Create(
                EnemyRemoveReason.KilledByPlayer,
                gold: gold,
                baseDmg: 0,
                pos: transform.position,
                netId: netId));
            NetworkServer.Destroy(gameObject);
        }

        public void SetSlowVisual(bool slowed)
        {
            if (meshRenderer == null) return;
            meshRenderer.material.color = slowed ? Color.blue : originalColor;
        }
    }
}
