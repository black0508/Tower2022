using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Tower
{
    /// <summary>
    /// 塔配置资产：数据驱动名称/预制体/各级配置，加塔与升级都只改 asset 不改代码。
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

        /// <summary>取某一级配置。level 从 1 起，对应 levels[level-1]。</summary>
        public bool TryGetLevel(int towerConfigId, int level, out TowerLevelDef levelDef)
        {
            levelDef = default;
            if (!towers.TryGetValue(towerConfigId, out var def) || def.levels == null) return false;
            if (level < 1 || level > def.levels.Count) return false;
            levelDef = def.levels[level - 1];
            return true;
        }

        /// <summary>该塔最高等级（= levels 数量）；未配置返回 0。</summary>
        public int GetMaxLevel(int towerConfigId)
        {
            if (!towers.TryGetValue(towerConfigId, out var def) || def.levels == null) return 0;
            return def.levels.Count;
        }
    }

    [Serializable]
    public class TowerDef
    {
        [LabelText("名称")]
        public string displayName;

        [LabelText("预制体")]
        public GameObject prefab;

        [LabelText("等级配置 (第1项=1级)")]
        [ListDrawerSettings(ShowIndexLabels = true)]
        public List<TowerLevelDef> levels = new();
    }

    /// <summary>单级配置：造价/售价 + 该级要施加的效果列表。</summary>
    [Serializable]
    public class TowerLevelDef
    {
        [LabelText("造价/升级花费")]
        public int cost;

        [LabelText("出售金额")]
        public int sellPrice;

        [LabelText("升级效果")]
        public List<ITowerLevelEffect> effects = new();
    }

    /// <summary>
    /// 塔某一级要施加的效果（服务端执行）。后期加 Buff、换子弹等，只需新增一个实现类，
    /// 不用改升级流程。
    /// </summary>
    public interface ITowerLevelEffect
    {
        void Apply(TowerUnit tower);
    }

    /// <summary>把该级属性写入塔的 Base（SET 语义），逐级覆盖。</summary>
    [Serializable]
    public class SetTowerAttributesEffect : ITowerLevelEffect
    {
        [TableList(AlwaysExpanded = true, DrawScrollView = false)]
        [LabelText("属性")]
        public AttributeEntry[] attributes = Array.Empty<AttributeEntry>();

        public void Apply(TowerUnit tower)
        {
            if (attributes == null || tower == null || tower.Attribute == null) return;
            foreach (var e in attributes)
                tower.Attribute.SetBase(e.key, e.value, recalculate: false);
        }
    }
}
