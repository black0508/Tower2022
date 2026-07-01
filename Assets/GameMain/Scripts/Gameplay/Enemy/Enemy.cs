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

        [Header("Path")]
        [SerializeField] private string baseTag = "Tower.Path.End";
        [SerializeField] private float reachBaseDistance = 1f;

        private NavMeshAgent agent;
        private Transform baseTarget;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
        }

        public override void OnStartServer()
        {
            agent.speed = baseSpeed;

            var baseGo = GameObject.FindWithTag(baseTag);
            if (baseGo == null)
            {
                Debug.LogError($"[Server] Base not found! Tag object as '{baseTag}'.");
                return;
            }

            baseTarget = baseGo.transform;

            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                agent.Warp(hit.position);

            agent.destination = baseTarget.position;
        }

        public override void OnStartClient()
        {
            if (!isServer)
                agent.enabled = false;
        }

        void Update()
        {
            if (!isServer) return;

            if (baseTarget != null &&
                Vector3.Distance(transform.position, baseTarget.position) < reachBaseDistance)
            {
                ReachBase();
            }
        }

        [Server]
        void ReachBase()
        {
            Debug.Log("[Server] Enemy reached base!");
            // TODO: Day 4 接上 Base.TakeDamage(1)
            NetworkServer.Destroy(gameObject);
        }

        [Server]
        public void TakeDamage(int dmg)
        {
            hp -= dmg;
            if (hp <= 0)
            {
                Debug.Log($"[Server] Enemy {netId} died");
                GameEntry.Event.Fire(this, EnemyKilledEventArgs.Create(goldReward));
                NetworkServer.Destroy(gameObject);
            }
        }
    }
}
