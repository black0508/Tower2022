using Mirror;
using UnityEngine;
using UnityEngine.AI;

namespace Tower
{
    public class Enemy : NetworkBehaviour
    {
        [Header("Stats")]
        [SyncVar] public int hp = 100;
        public int maxHp = 100;
        public float baseSpeed = 3f;
        [SerializeField] int goldReward = 10;
        [SerializeField] int baseDamage = 1;

        [Header("Path")]
        [SerializeField] private string pathEndTag = "Tower.Path.End";
        [SerializeField] private float reachBaseDistance = 1f;

        NavMeshAgent agent;
        Transform pathEnd;
        bool reachedBase;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
        }

        public override void OnStartServer()
        {
            agent.speed = baseSpeed;

            var pathEndGo = GameObject.FindWithTag(pathEndTag);
            if (pathEndGo == null)
            {
                Debug.LogError($"[Server] Path end not found! Tag object as '{pathEndTag}'.");
                return;
            }

            pathEnd = pathEndGo.transform;

            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                agent.Warp(hit.position);

            agent.destination = pathEnd.position;
        }

        public override void OnStartClient()
        {
            if (!isServer)
                agent.enabled = false;
        }

        void Update()
        {
            if (!isServer) return;

            if (!reachedBase && pathEnd != null &&
                Vector3.Distance(transform.position, pathEnd.position) < reachBaseDistance)
            {
                ReachBase();
            }
        }

        [Server]
        void ReachBase()
        {
            reachedBase = true;
            GameEntry.Event.Fire(this, EnemyKilledEventArgs.Create(
                EnemyRemoveReason.ReachedBase,
                gold: 0,
                baseDmg: baseDamage,
                pos: transform.position,
                netId: netId));
            NetworkServer.Destroy(gameObject);
        }

        [Server]
        public void TakeDamage(int dmg)
        {
            hp -= dmg;
            if (hp <= 0)
            {
                GameEntry.Event.Fire(this, EnemyKilledEventArgs.Create(
                    EnemyRemoveReason.KilledByPlayer,
                    gold: goldReward,
                    baseDmg: 0,
                    pos: transform.position,
                    netId: netId));
                NetworkServer.Destroy(gameObject);
            }
        }
    }
}
