using System;
using Sirenix.OdinInspector;

namespace Tower
{
    public enum AttributeKey
    {
        None = 0,

        [LabelText("最大生命 (通用)")]
        MaxHp = 2,

        [LabelText("移速 (敌)")]
        Speed = 10,

        [LabelText("撞家伤害 (敌)")]
        BaseDamage = 11,

        [LabelText("击杀掉落金 (敌)")]
        GoldReward = 12,

        [LabelText("攻击间隔 (塔)")]
        AttackInterval = 20,

        [LabelText("攻击伤害 (塔)")]
        Damage = 21,

        [LabelText("弹速 (塔)")]
        ProjectileSpeed = 22,

        [LabelText("射程 (塔)")]
        Range = 23,
    }

    /// <summary>Inspector 配 Base 与 SyncList 同步 Final 共用。</summary>
    [Serializable]
    public struct AttributeEntry
    {
        [HorizontalGroup("Row", Width = 0.65f)]
        [LabelText("属性"), LabelWidth(40)]
        public AttributeKey key;

        [HorizontalGroup("Row")]
        [LabelText("值"), LabelWidth(30)]
        public float value;

        // Mirror SyncList 抽屉用 ToString 画每一项；不重写只会显示类型名
        public override string ToString() => $"{key} = {value}";
    }
}
