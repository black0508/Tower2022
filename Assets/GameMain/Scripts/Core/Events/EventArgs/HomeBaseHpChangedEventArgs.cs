using GameFramework;
using GameFramework.Event;

namespace Tower
{
    public sealed class HomeBaseHpChangedEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(HomeBaseHpChangedEventArgs).GetHashCode();

        public int CurrentHp { get; private set; }
        public int MaxHp { get; private set; }

        public override int Id => EventId;

        public static HomeBaseHpChangedEventArgs Create(int cur, int max)
        {
            var args = ReferencePool.Acquire<HomeBaseHpChangedEventArgs>();
            args.CurrentHp = cur;
            args.MaxHp = max;
            return args;
        }

        public override void Clear()
        {
            CurrentHp = 0;
            MaxHp = 0;
        }
    }
}
