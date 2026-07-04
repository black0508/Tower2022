using GameFramework.Event;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Tower
{
    /// <summary>
    /// 建造栏中的单个塔卡片。哑控件：只负责展示、点击和自己关心的状态订阅。
    /// Init/Clear 由 BuildBarUI 统一驱动，保证扫描→初始化→订阅的顺序。
    /// </summary>
    public class TowerCardUI : MonoBehaviour
    {
        [Header("Tower Info")]
        [FormerlySerializedAs("towerType")]
        [SerializeField] int towerConfigId;
        [SerializeField] int cost = 50;

        [Header("UI Refs")]
        [SerializeField] Button button;
        [SerializeField] Image highlightFrame;

        public int TowerConfigId => towerConfigId;
        public int Cost => cost;

        public void Init()
        {
            if (button != null)
                button.onClick.AddListener(OnClick);

            GameEntry.Event.Subscribe(TowerSelectionChangedEventArgs.EventId, OnSelectionChanged);
            GameEntry.Event.Subscribe(SharedGoldChangedEventArgs.EventId, OnSharedGoldChanged);

            RefreshInteractable(GameEntry.State != null ? GameEntry.State.sharedGold : 0);
            SetSelected(false);
        }

        public void Clear()
        {
            if (button != null)
                button.onClick.RemoveListener(OnClick);
            GameEntry.Event.Unsubscribe(TowerSelectionChangedEventArgs.EventId, OnSelectionChanged);
            GameEntry.Event.Unsubscribe(SharedGoldChangedEventArgs.EventId, OnSharedGoldChanged);
        }

        public void SetSelected(bool selected)
        {
            if (highlightFrame != null)
                highlightFrame.enabled = selected;
        }

        public void RefreshInteractable(int currentGold)
        {
            if (button != null)
                button.interactable = currentGold >= cost;
        }

        void OnClick()
        {
            GameEntry.Event.Fire(this, TowerCardClickedEventArgs.Create(new TowerBuildInfo(towerConfigId, cost)));
        }

        void OnSharedGoldChanged(object sender, GameEventArgs e)
        {
            if (e is SharedGoldChangedEventArgs args)
                RefreshInteractable(args.SharedGold);
        }

        void OnSelectionChanged(object sender, GameEventArgs e)
        {
            if (e is not TowerSelectionChangedEventArgs args) return;
            SetSelected(args.SelectedTowerConfigId == towerConfigId);
        }
    }
}
