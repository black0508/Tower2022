using GameFramework;
using GameFramework.Event;
using UnityEngine;

namespace Tower
{
    public enum EnemyRemoveReason
    {
        KilledByPlayer,
        ReachedBase,
    }

    /// <summary>
    /// 敌人离开战场事件（击杀或到达基地）。
    /// </summary>
    public sealed class EnemyRemovedEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(EnemyRemovedEventArgs).GetHashCode();

        public EnemyRemoveReason Reason { get; private set; }
        public int GoldAmount { get; private set; }
        public int BaseDamage { get; private set; }
        public Vector3 Position { get; private set; }
        public uint EnemyNetId { get; private set; }

        public override int Id => EventId;

        public static EnemyRemovedEventArgs Create(
            EnemyRemoveReason reason, int gold, int baseDmg, Vector3 pos, uint netId)
        {
            var args = ReferencePool.Acquire<EnemyRemovedEventArgs>();
            args.Reason = reason;
            args.GoldAmount = gold;
            args.BaseDamage = baseDmg;
            args.Position = pos;
            args.EnemyNetId = netId;
            return args;
        }

        public override void Clear()
        {
            Reason = default;
            GoldAmount = 0;
            BaseDamage = 0;
            Position = default;
            EnemyNetId = 0;
        }
    }
}
