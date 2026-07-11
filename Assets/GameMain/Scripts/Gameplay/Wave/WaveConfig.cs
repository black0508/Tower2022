using System;
using System.Linq;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Tower
{
    [CreateAssetMenu(fileName = "WaveConfig", menuName = "TowerDefense/WaveConfig")]
    public class WaveConfig : SerializedScriptableObject
    {
        [LabelText("波次列表")]
        [ListDrawerSettings(ShowIndexLabels = true, ListElementLabelName = "Label")]
        public WaveEntry[] waves;
    }

    [Serializable]
    public class WaveEntry
    {
        [LabelText("波前等待"), SuffixLabel("秒")]
        public float delayBeforeWave = 3f;

        [LabelText("刷怪时间线")]
        [TableList(ShowIndexLabels = true, ShowPaging = false)]
        public WaveSpawnElement[] spawns;

        public string Label
        {
            get
            {
                int total = spawns?.Sum(s => s.spawnCount) ?? 0;
                return $"Wave · {total} 只";
            }
        }
    }

    [Serializable]
    public class WaveSpawnElement
    {
        [LabelText("时刻"), SuffixLabel("秒"), MinValue(0)]
        public float spawnTime = 2f;

        [LabelText("数量"), MinValue(1)]
        public int spawnCount = 1;

        [LabelText("间隔"), SuffixLabel("秒"), MinValue(0)]
        public float spawnInterval = 2f;

        [LabelText("敌人"), AssetsOnly]
        public GameObject enemyPrefab;

        public string Label => $"{spawnTime}s 起 · {spawnCount} 只 · 隔 {spawnInterval}s";
    }
}
