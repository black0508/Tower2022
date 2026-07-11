using GameFramework;
using GameFramework.Event;

namespace Tower
{
    public sealed class PlayerReadyChangedEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(PlayerReadyChangedEventArgs).GetHashCode();

        public int PlayerId { get; private set; }
        public bool IsReady { get; private set; }

        public override int Id => EventId;

        public static PlayerReadyChangedEventArgs Create(int id, bool ready)
        {
            var args = ReferencePool.Acquire<PlayerReadyChangedEventArgs>();
            args.PlayerId = id;
            args.IsReady = ready;
            return args;
        }

        public override void Clear()
        {
            PlayerId = 0;
            IsReady = false;
        }
    }
}
