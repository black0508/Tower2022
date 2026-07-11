using GameFramework;
using GameFramework.Event;

namespace Tower
{
    /// <summary>
    /// 波次信息更新（0.5s 节流），Day 7 HUD 订阅。
    /// </summary>
    public sealed class WaveInfoUpdateEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(WaveInfoUpdateEventArgs).GetHashCode();

        public int CurrentWave { get; private set; }
        public int TotalWaves { get; private set; }
        public float SpawnProgress { get; private set; }

        public override int Id => EventId;

        public static WaveInfoUpdateEventArgs Create(int cur, int total, float progress)
        {
            var args = ReferencePool.Acquire<WaveInfoUpdateEventArgs>();
            args.CurrentWave = cur;
            args.TotalWaves = total;
            args.SpawnProgress = progress;
            return args;
        }

        public override void Clear()
        {
            CurrentWave = 0;
            TotalWaves = 0;
            SpawnProgress = 0;
        }
    }
}
