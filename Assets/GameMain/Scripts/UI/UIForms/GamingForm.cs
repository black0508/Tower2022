using GameFramework.Event;
using UnityEngine;
using UnityEngine.UI;

namespace Tower
{
    /// <summary>
    /// 战斗主界面：金币显示 + 建造栏。
    /// </summary>
    public class GamingForm : UGUIForm
    {
        [Header("HUD")]
        [SerializeField] Text goldText;

        [Header("Build Bar")]
        [SerializeField] BuildBarUI buildBar;

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            if (buildBar != null)
            {
                buildBar.gameObject.SetActive(false);
                buildBar.Init();
            }

            GameEntry.Event.Subscribe(SharedGoldChangedEventArgs.EventId, OnSharedGoldChanged);
            GameEntry.Event.Subscribe(BuildModeChangedEventArgs.EventId, OnBuildModeChanged);
            RefreshGold(GameEntry.State != null ? GameEntry.State.sharedGold : 0);
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            GameEntry.Event.Unsubscribe(SharedGoldChangedEventArgs.EventId, OnSharedGoldChanged);
            GameEntry.Event.Unsubscribe(BuildModeChangedEventArgs.EventId, OnBuildModeChanged);
            if (buildBar != null)
                buildBar.Clear();
            base.OnClose(isShutdown, userData);
        }

        void OnSharedGoldChanged(object sender, GameEventArgs e)
        {
            if (e is SharedGoldChangedEventArgs args)
                RefreshGold(args.SharedGold);
        }

        void OnBuildModeChanged(object sender, GameEventArgs e)
        {
            if (e is not BuildModeChangedEventArgs args || buildBar == null) return;
            buildBar.gameObject.SetActive(args.IsActive);
        }

        void RefreshGold(int gold)
        {
            if (goldText != null)
                goldText.text = $"金币: {gold}";
        }
    }
}
