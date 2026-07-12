using GameFramework;
using GameFramework.Event;

namespace Tower
{
    public sealed class TowerCardClickedEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(TowerCardClickedEventArgs).GetHashCode();

        public int TowerConfigId { get; private set; }

        public override int Id => EventId;

        public static TowerCardClickedEventArgs Create(int towerConfigId)
        {
            var args = ReferencePool.Acquire<TowerCardClickedEventArgs>();
            args.TowerConfigId = towerConfigId;
            return args;
        }

        public override void Clear()
        {
            TowerConfigId = 0;
        }
    }
}
