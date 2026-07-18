using GameFramework;
using GameFramework.Event;

namespace Tower
{
    /// <summary>
    /// 本地玩家在场上选中/取消选中一座已建成的塔（用于升级/出售面板）。
    /// TowerNetId 为 0 表示取消选中。
    /// </summary>
    public sealed class TowerActionSelectedEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(TowerActionSelectedEventArgs).GetHashCode();

        public uint TowerNetId { get; private set; }
        public int TowerConfigId { get; private set; }
        public int Level { get; private set; }

        public bool HasSelection => TowerNetId != 0;

        public override int Id => EventId;

        public static TowerActionSelectedEventArgs Create(uint towerNetId, int towerConfigId, int level)
        {
            var args = ReferencePool.Acquire<TowerActionSelectedEventArgs>();
            args.TowerNetId = towerNetId;
            args.TowerConfigId = towerConfigId;
            args.Level = level;
            return args;
        }

        public override void Clear()
        {
            TowerNetId = 0;
            TowerConfigId = 0;
            Level = 0;
        }
    }
}
