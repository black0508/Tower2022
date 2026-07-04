using GameFramework;
using GameFramework.Event;

namespace Tower
{
    public sealed class TowerCardClickedEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(TowerCardClickedEventArgs).GetHashCode();

        public TowerBuildInfo TowerInfo { get; private set; }

        public override int Id => EventId;

        public static TowerCardClickedEventArgs Create(TowerBuildInfo towerInfo)
        {
            var args = ReferencePool.Acquire<TowerCardClickedEventArgs>();
            args.TowerInfo = towerInfo;
            return args;
        }

        public override void Clear()
        {
            TowerInfo = default;
        }
    }
}
