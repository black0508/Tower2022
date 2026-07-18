using GameFramework.Event;
using Mirror;
using UnityEngine;
using UnityEngine.UI;

namespace Tower
{
    /// <summary>
    /// 选中已建塔后的操作面板：显示等级/售价/升级花费，提供升级与出售按钮。
    /// Init/Clear 由 GamingForm 驱动；通过 TowerActionSelectedEventArgs 显隐。
    /// 逻辑层只需要 GamePlayer.CmdUpgradeTower / CmdSellTower 两个接口。
    /// </summary>
    public class TowerActionPanelUI : MonoBehaviour
    {
        [SerializeField] Text infoText;
        [SerializeField] Button upgradeButton;
        [SerializeField] Text upgradeButtonText;
        [SerializeField] Button sellButton;
        [SerializeField] Text sellButtonText;

        uint m_TowerNetId;

        public void Init()
        {
            if (upgradeButton != null) upgradeButton.onClick.AddListener(OnUpgradeClicked);
            if (sellButton != null) sellButton.onClick.AddListener(OnSellClicked);

            GameEntry.Event.Subscribe(TowerActionSelectedEventArgs.EventId, OnTowerActionSelected);
            GameEntry.Event.Subscribe(SharedGoldChangedEventArgs.EventId, OnSharedGoldChanged);
            GameEntry.Event.Subscribe(BuildReasonEventArgs.EventId, OnBuildReason);

            Hide();
        }

        public void Clear()
        {
            if (upgradeButton != null) upgradeButton.onClick.RemoveListener(OnUpgradeClicked);
            if (sellButton != null) sellButton.onClick.RemoveListener(OnSellClicked);

            GameEntry.Event.Unsubscribe(TowerActionSelectedEventArgs.EventId, OnTowerActionSelected);
            GameEntry.Event.Unsubscribe(SharedGoldChangedEventArgs.EventId, OnSharedGoldChanged);
            GameEntry.Event.Unsubscribe(BuildReasonEventArgs.EventId, OnBuildReason);
        }

        void OnTowerActionSelected(object sender, GameEventArgs e)
        {
            if (e is not TowerActionSelectedEventArgs args) return;

            if (!args.HasSelection)
            {
                Hide();
                return;
            }

            m_TowerNetId = args.TowerNetId;
            gameObject.SetActive(true);
            Refresh();
        }

        void OnSharedGoldChanged(object sender, GameEventArgs e) => Refresh();

        // 升级/出售结果回来后刷新；塔已被卖掉则 Refresh 内自动关闭面板
        void OnBuildReason(object sender, GameEventArgs e) => Refresh();

        void OnUpgradeClicked()
        {
            var player = GameEntry.PlayerManager?.GetLocalPlayer();
            if (player != null && m_TowerNetId != 0)
                player.CmdUpgradeTower(m_TowerNetId);
        }

        void OnSellClicked()
        {
            var player = GameEntry.PlayerManager?.GetLocalPlayer();
            if (player != null && m_TowerNetId != 0)
                player.CmdSellTower(m_TowerNetId);
        }

        void Hide()
        {
            m_TowerNetId = 0;
            gameObject.SetActive(false);
        }

        TowerUnit ResolveTower()
        {
            if (m_TowerNetId == 0) return null;
            if (NetworkClient.spawned.TryGetValue(m_TowerNetId, out var identity) && identity != null)
                return identity.GetComponent<TowerUnit>();
            return null;
        }

        void Refresh()
        {
            if (m_TowerNetId == 0) return;

            var tower = ResolveTower();
            var cfg = GameEntry.GameConfig?.TowerConfig;
            if (tower == null || cfg == null || !cfg.TryGetTower(tower.towerConfigId, out var def))
            {
                Hide();
                return;
            }

            int level = tower.level;
            int maxLevel = cfg.GetMaxLevel(tower.towerConfigId);
            string name = string.IsNullOrEmpty(def.displayName) ? $"Tower {tower.towerConfigId}" : def.displayName;
            int sellPrice = cfg.TryGetLevel(tower.towerConfigId, level, out var cur) ? cur.sellPrice : 0;
            int gold = GameEntry.State != null ? GameEntry.State.sharedGold : 0;
            bool hasNext = cfg.TryGetLevel(tower.towerConfigId, level + 1, out var nextLevel);

            if (infoText != null)
                infoText.text = $"{name}  Lv.{level}/{maxLevel}";

            if (upgradeButton != null)
                upgradeButton.interactable = hasNext && gold >= nextLevel.cost;

            if (upgradeButtonText != null)
                upgradeButtonText.text = hasNext ? $"升级 ({nextLevel.cost})" : "已满级";

            if (sellButtonText != null)
                sellButtonText.text = $"出售 ({sellPrice})";
        }
    }
}
