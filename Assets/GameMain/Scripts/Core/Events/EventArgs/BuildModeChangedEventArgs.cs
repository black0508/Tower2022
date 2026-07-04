using GameFramework;
using GameFramework.Event;

namespace Tower
{
    public sealed class BuildModeChangedEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(BuildModeChangedEventArgs).GetHashCode();

        public bool IsActive { get; private set; }

        public override int Id => EventId;

        public static BuildModeChangedEventArgs Create(bool isActive)
        {
            var args = ReferencePool.Acquire<BuildModeChangedEventArgs>();
            args.IsActive = isActive;
            return args;
        }

        public override void Clear()
        {
            IsActive = false;
        }
    }
}
