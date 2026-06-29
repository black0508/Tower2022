using Mirror;
using UnityEngine;

namespace Tower
{
    public class EnemySpawner : MonoBehaviour
    {
        [Header("Prefabs")]
        public GameObject enemyPrefab;

        [Header("Settings")]
        public float spawnInterval = 3f;
        public int spawnCount = 5;

        [Header("Runtime State (Inspector可观察)")]
        [SerializeField] private bool isSpawning;
        [SerializeField] private float spawnTimer;
        [SerializeField] private int spawnedCount;

        public void StartSpawning()
        {
            isSpawning = true;
            spawnTimer = 0;
            spawnedCount = 0;
        }

        public void StopSpawning()
        {
            isSpawning = false;
        }

        void Update()
        {
            if (!NetworkServer.active) return;
            if (!isSpawning) return;

            if (enemyPrefab == null)
            {
                Debug.LogError("[Server] EnemySpawner: enemyPrefab is not assigned.");
                isSpawning = false;
                return;
            }

            if (spawnedCount >= spawnCount)
            {
                isSpawning = false;
                Debug.Log("[Server] Spawn batch finished");
                return;
            }

            spawnTimer += Time.deltaTime;
            if (spawnTimer >= spawnInterval)
            {
                spawnTimer = 0;
                var go = Instantiate(enemyPrefab, transform.position, Quaternion.identity);
                NetworkServer.Spawn(go);
                spawnedCount++;
                Debug.Log($"[Server] Spawned enemy {spawnedCount}/{spawnCount}");
            }
        }
    }
}
