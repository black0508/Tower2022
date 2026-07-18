using GameFramework.Event;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Tower
{
    /// <summary>
    /// 建造栏中的单个塔卡片。哑控件：只负责展示、点击和自己关心的状态订阅。
    /// 名称/造价从 TowerConfig 读取，UI 上只需配置 towerConfigId。
    /// Init/Clear 由 BuildBarUI 统一驱动，保证扫描→初始化→订阅的顺序。
    /// </summary>
    public class TowerCardUI : MonoBehaviour
    {
        [Header("Tower Info")]
        [FormerlySerializedAs("towerType")]
        [SerializeField] int towerConfigId;

        [Header("UI Refs")]
        [SerializeField] Button button;
        [SerializeField] Image highlightFrame;
        [SerializeField] Text nameText;
        [SerializeField] Text costText;

        int m_Cost = int.MaxValue;

        public int TowerConfigId => towerConfigId;

        public void Init()
        {
            ResolveTextRefs();
            RefreshFromConfig();

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
                button.interactable = currentGold >= m_Cost;
        }

        void OnClick()
        {
            GameEntry.Event.Fire(this, TowerCardClickedEventArgs.Create(towerConfigId));
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

        void ResolveTextRefs()
        {
            if (nameText == null)
            {
                var t = transform.Find("NameText");
                if (t != null) nameText = t.GetComponent<Text>();
            }

            if (costText == null)
            {
                var t = transform.Find("CostText");
                if (t != null) costText = t.GetComponent<Text>();
            }
        }

        void RefreshFromConfig()
        {
            var cfg = GameEntry.GameConfig?.TowerConfig;
            if (cfg == null || !cfg.TryGetTower(towerConfigId, out var def) || !cfg.TryGetLevel(towerConfigId, 1, out var levelDef))
            {
                Debug.LogError($"[TowerCardUI] towerConfigId={towerConfigId} not found or missing level 1 in TowerConfig.");
                m_Cost = int.MaxValue;
                if (nameText != null) nameText.text = "?";
                if (costText != null) costText.text = "-";
                return;
            }

            m_Cost = levelDef.cost;
            if (nameText != null)
                nameText.text = string.IsNullOrEmpty(def.displayName) ? $"Tower {towerConfigId}" : def.displayName;
            if (costText != null)
                costText.text = $"{levelDef.cost}金币";
        }
    }
}
