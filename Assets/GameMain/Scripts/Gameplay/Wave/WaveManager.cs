using GameFramework.Event;
using Mirror;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Tower
{
    /// <summary>
    /// 服务端波次调度（非网络对象，由 GameState.OnStartServer 调用 InitServer）。
    /// </summary>
    public class WaveManager : MonoBehaviour
    {
        const string SpawnPointTag = "Tower.Path.Start";

        public WaveConfig config;

        public int spawnedThisWave;
        public int killedThisWave;

        float waveTimer;
        float delayBeforeWaveTimer;
        bool spawnPhaseComplete;
        int waveIndex;
        Queue<ScheduledSpawn> spawnQueue;
        bool initialized;
        Transform m_SpawnPoint;

        struct ScheduledSpawn
        {
            public float spawnTime;
            public GameObject prefab;
            public int spawnIndex;
        }

        public bool IsCurrentWaveCleared =>
            spawnPhaseComplete && spawnedThisWave > 0 && killedThisWave >= spawnedThisWave;

        public bool IsLastWave => config != null && waveIndex >= config.waves.Length - 1;

        public void InitServer()
        {
            if (initialized) return;
            initialized = true;
            spawnQueue = new Queue<ScheduledSpawn>();

            var spawnGo = GameObject.FindWithTag(SpawnPointTag);
            if (spawnGo == null)
                Debug.LogError($"[Server] Spawn point not found! Tag object as '{SpawnPointTag}'.");
            else
                m_SpawnPoint = spawnGo.transform;

            GameEntry.Event.Subscribe(EnemyRemovedEventArgs.EventId, OnEnemyRemoved);
            GameEntry.Event.Subscribe(WaveSpawnEnemyEventArgs.EventId, OnSpawnScheduled);
        }

        void OnDestroy()
        {
            if (!initialized) return;
            GameEntry.Event.Unsubscribe(EnemyRemovedEventArgs.EventId, OnEnemyRemoved);
            GameEntry.Event.Unsubscribe(WaveSpawnEnemyEventArgs.EventId, OnSpawnScheduled);
        }

        public void StartWave(int index, bool skipDelayBeforeWave = false)
        {
            if (config == null || index < 0 || index >= config.waves.Length) return;

            waveIndex = index;
            var entry = config.waves[index];
            spawnedThisWave = 0;
            killedThisWave = 0;
            waveTimer = 0;
            delayBeforeWaveTimer = skipDelayBeforeWave ? 0 : entry.delayBeforeWave;
            spawnPhaseComplete = false;
            spawnQueue.Clear();

            if (entry.spawns == null || entry.spawns.Length == 0)
            {
                Debug.LogError($"[Server] Wave {index + 1}: spawns is empty.");
                return;
            }

            var scheduled = new List<ScheduledSpawn>();
            int spawnIndex = 0;
            foreach (var elem in entry.spawns)
            {
                for (int i = 0; i < elem.spawnCount; i++)
                {
                    scheduled.Add(new ScheduledSpawn
                    {
                        spawnTime = elem.spawnTime + i * elem.spawnInterval,
                        prefab = elem.enemyPrefab,
                        spawnIndex = spawnIndex++
                    });
                }
            }

            scheduled.Sort((a, b) => a.spawnTime.CompareTo(b.spawnTime));
            foreach (var s in scheduled)
                spawnQueue.Enqueue(s);

            float timelineEndTime = scheduled.Count > 0 ? scheduled[scheduled.Count - 1].spawnTime : 0;
            Debug.Log($"[Server] Wave {index + 1}/{config.waves.Length} started, plannedSpawns={spawnQueue.Count}, timelineEnd={timelineEndTime:F1}s");
        }

        public void ServerTick()
        {
            if (spawnPhaseComplete && killedThisWave >= spawnedThisWave) return;

            if (delayBeforeWaveTimer > 0)
            {
                delayBeforeWaveTimer -= Time.deltaTime;
                return;
            }

            waveTimer += Time.deltaTime;

            while (spawnQueue.Count > 0 && waveTimer >= spawnQueue.Peek().spawnTime)
            {
                var next = spawnQueue.Dequeue();
                var pos = m_SpawnPoint != null ? m_SpawnPoint.position : Vector3.zero;
                GameEntry.Event.Fire(this, WaveSpawnEnemyEventArgs.Create(
                    waveIndex, next.spawnIndex, next.prefab, pos));
            }

            if (spawnQueue.Count == 0)
                spawnPhaseComplete = true;
        }

        void OnSpawnScheduled(object sender, GameEventArgs e)
        {
            if (e is not WaveSpawnEnemyEventArgs args) return;
            if (args.EnemyPrefab == null)
            {
                Debug.LogError("[Server] WaveSpawnEnemyEventArgs: enemyPrefab is null.");
                return;
            }

            var go = Instantiate(args.EnemyPrefab, args.SpawnPosition, Quaternion.identity);
            NetworkServer.Spawn(go);
            spawnedThisWave++;
        }

        void OnEnemyRemoved(object sender, GameEventArgs e)
        {
            killedThisWave++;
        }
    }
}
