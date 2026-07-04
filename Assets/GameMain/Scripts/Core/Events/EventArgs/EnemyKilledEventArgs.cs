using GameFramework;
using GameFramework.Event;

namespace Tower
{
    /// <summary>
    /// 敌人被击杀事件。
    /// </summary>
    public sealed class EnemyKilledEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(EnemyKilledEventArgs).GetHashCode();

        public int GoldAmount { get; private set; }

        public override int Id => EventId;

        public static EnemyKilledEventArgs Create(int goldAmount)
        {
            var args = ReferencePool.Acquire<EnemyKilledEventArgs>();
            args.GoldAmount = goldAmount;
            return args;
        }

        public override void Clear()
        {
            GoldAmount = 0;
        }
    }
}
