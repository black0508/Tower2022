using GameFramework;
using GameFramework.Event;
using UnityEngine;

namespace Tower
{
    /// <summary>
    /// 波次调度器决定「该刷了」时 Fire；由 WaveManager 订阅并执行 NetworkServer.Spawn。
    /// </summary>
    public sealed class WaveSpawnEnemyEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(WaveSpawnEnemyEventArgs).GetHashCode();

        public int WaveIndex { get; private set; }
        public int SpawnIndex { get; private set; }
        public GameObject EnemyPrefab { get; private set; }
        public Vector3 SpawnPosition { get; private set; }

        public override int Id => EventId;

        public static WaveSpawnEnemyEventArgs Create(
            int waveIndex, int spawnIndex, GameObject prefab, Vector3 pos)
        {
            var args = ReferencePool.Acquire<WaveSpawnEnemyEventArgs>();
            args.WaveIndex = waveIndex;
            args.SpawnIndex = spawnIndex;
            args.EnemyPrefab = prefab;
            args.SpawnPosition = pos;
            return args;
        }

        public override void Clear()
        {
            WaveIndex = 0;
            SpawnIndex = 0;
            EnemyPrefab = null;
            SpawnPosition = default;
        }
    }
}
