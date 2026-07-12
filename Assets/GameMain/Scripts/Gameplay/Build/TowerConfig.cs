using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Tower
{
    /// <summary>
    /// 塔配置资产：数据驱动 cost/prefab，加塔只改 asset 不改代码。
    /// </summary>
    [CreateAssetMenu(menuName = "Tower/TowerConfig", fileName = "TowerConfig")]
    public class TowerConfig : SerializedScriptableObject
    {
        [SerializeField, DictionaryDrawerSettings(KeyLabel = "塔配置ID", ValueLabel = "配置")]
        Dictionary<int, TowerDef> towers = new();

        public bool TryGetTower(int towerConfigId, out TowerDef def)
        {
            return towers.TryGetValue(towerConfigId, out def);
        }
    }

    [Serializable]
    public struct TowerDef
    {
        [LabelText("名称")]
        public string displayName;

        [LabelText("造价")]
        public int cost;

        [LabelText("预制体")]
        public GameObject prefab;
    }
}
