using GameFramework;
using GameFramework.Event;

namespace Tower
{
    public sealed class TowerSelectionChangedEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(TowerSelectionChangedEventArgs).GetHashCode();

        /// <summary>当前选中的塔配置 ID，-1 表示无选中。</summary>
        public int SelectedTowerConfigId { get; private set; }

        public override int Id => EventId;

        public static TowerSelectionChangedEventArgs Create(int selectedTowerConfigId)
        {
            var args = ReferencePool.Acquire<TowerSelectionChangedEventArgs>();
            args.SelectedTowerConfigId = selectedTowerConfigId;
            return args;
        }

        public override void Clear()
        {
            SelectedTowerConfigId = -1;
        }
    }
}
