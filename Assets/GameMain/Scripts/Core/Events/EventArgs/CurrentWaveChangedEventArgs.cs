using GameFramework;
using GameFramework.Event;

namespace Tower
{
    /// <summary>
    /// 当前波次变化（GameState currentWave SyncVar hook 或 RpcStartBattle 触发）。
    /// </summary>
    public sealed class CurrentWaveChangedEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(CurrentWaveChangedEventArgs).GetHashCode();

        public int CurrentWave { get; private set; }

        public override int Id => EventId;

        public static CurrentWaveChangedEventArgs Create(int currentWave)
        {
            var args = ReferencePool.Acquire<CurrentWaveChangedEventArgs>();
            args.CurrentWave = currentWave;
            return args;
        }

        public override void Clear()
        {
            CurrentWave = 0;
        }
    }
}
