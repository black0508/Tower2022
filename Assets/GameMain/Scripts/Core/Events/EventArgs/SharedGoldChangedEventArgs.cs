using GameFramework;
using GameFramework.Event;

namespace Tower
{
    /// <summary>
    /// 共享金币变化（GameState SyncVar hook 触发）。
    /// </summary>
    public sealed class SharedGoldChangedEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(SharedGoldChangedEventArgs).GetHashCode();

        public int SharedGold { get; private set; }

        public override int Id => EventId;

        public static SharedGoldChangedEventArgs Create(int sharedGold)
        {
            var args = ReferencePool.Acquire<SharedGoldChangedEventArgs>();
            args.SharedGold = sharedGold;
            return args;
        }

        public override void Clear()
        {
            SharedGold = 0;
        }
    }
}
