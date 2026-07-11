using GameFramework;
using GameFramework.Event;

namespace Tower
{
    public sealed class GamePhaseChangedEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(GamePhaseChangedEventArgs).GetHashCode();

        public GamePhase Phase { get; private set; }

        public override int Id => EventId;

        public static GamePhaseChangedEventArgs Create(GamePhase phase)
        {
            var args = ReferencePool.Acquire<GamePhaseChangedEventArgs>();
            args.Phase = phase;
            return args;
        }

        public override void Clear()
        {
            Phase = default;
        }
    }
}
