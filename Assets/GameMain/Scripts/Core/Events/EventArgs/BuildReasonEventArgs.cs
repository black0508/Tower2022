using GameFramework;
using GameFramework.Event;

namespace Tower
{
    public sealed class BuildReasonEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(BuildReasonEventArgs).GetHashCode();

        public bool Success { get; private set; }
        public string Reason { get; private set; }

        public override int Id => EventId;

        public static BuildReasonEventArgs Create(bool success = true, string reason = null)
        {
            var args = ReferencePool.Acquire<BuildReasonEventArgs>();
            args.Success = success;
            args.Reason = reason;
            return args;
        }

        public override void Clear()
        {
            Success = false;
            Reason = null;
        }
    }
}
