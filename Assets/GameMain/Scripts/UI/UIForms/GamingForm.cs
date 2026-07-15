using GameFramework.Event;
using UnityEngine;
using UnityEngine.UI;

namespace Tower
{
    /// <summary>
    /// 战斗主界面：金币 / 波次 / 基地生命 + 建造栏。
    /// </summary>
    public class GamingForm : UGUIForm
    {
        [Header("HUD")]
        [SerializeField] Text goldText;
        [SerializeField] Text waveText;
        [SerializeField] Text hpText;

        [Header("Status")]
        [SerializeField] StatusPanelUI statusPanel;

        [Header("Build Bar")]
        [SerializeField] BuildBarUI buildBar;

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            if (waveText == null)
            {
                var t = transform.Find("WaveText");
                if (t != null) waveText = t.GetComponent<Text>();
            }

            statusPanel?.Init();

            if (buildBar != null)
            {
                buildBar.gameObject.SetActive(false);
                buildBar.Init();
            }

            GameEntry.Event.Subscribe(SharedGoldChangedEventArgs.EventId, OnSharedGoldChanged);
            GameEntry.Event.Subscribe(HomeBaseHpChangedEventArgs.EventId, OnHomeBaseHpChanged);
            GameEntry.Event.Subscribe(BuildModeChangedEventArgs.EventId, OnBuildModeChanged);
            GameEntry.Event.Subscribe(GamePhaseChangedEventArgs.EventId, OnGamePhaseChanged);
            GameEntry.Event.Subscribe(CurrentWaveChangedEventArgs.EventId, OnCurrentWaveChanged);

            var state = GameEntry.State;
            RefreshGold(state != null ? state.sharedGold : 0);
            RefreshWaveForPhase(state != null ? state.phase : GamePhase.Preparing);
            if (GameEntry.HomeBase != null)
            {
                var attrs = GameEntry.HomeBase.GetComponent<AttributeComponent>();
                if (attrs != null)
                    RefreshHp(Mathf.RoundToInt(attrs.CurrentHp), Mathf.RoundToInt(attrs.GetMaxHp()));
            }
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            GameEntry.Event.Unsubscribe(SharedGoldChangedEventArgs.EventId, OnSharedGoldChanged);
            GameEntry.Event.Unsubscribe(HomeBaseHpChangedEventArgs.EventId, OnHomeBaseHpChanged);
            GameEntry.Event.Unsubscribe(BuildModeChangedEventArgs.EventId, OnBuildModeChanged);
            GameEntry.Event.Unsubscribe(GamePhaseChangedEventArgs.EventId, OnGamePhaseChanged);
            GameEntry.Event.Unsubscribe(CurrentWaveChangedEventArgs.EventId, OnCurrentWaveChanged);

            statusPanel?.Clear();

            if (buildBar != null)
                buildBar.Clear();

            base.OnClose(isShutdown, userData);
        }

        void OnSharedGoldChanged(object sender, GameEventArgs e)
        {
            if (e is SharedGoldChangedEventArgs args)
                RefreshGold(args.SharedGold);
        }

        void OnHomeBaseHpChanged(object sender, GameEventArgs e)
        {
            if (e is HomeBaseHpChangedEventArgs args)
                RefreshHp(args.CurrentHp, args.MaxHp);
        }

        void OnBuildModeChanged(object sender, GameEventArgs e)
        {
            if (e is not BuildModeChangedEventArgs args || buildBar == null) return;
            buildBar.gameObject.SetActive(args.IsActive);
        }

        void OnGamePhaseChanged(object sender, GameEventArgs e)
        {
            if (e is not GamePhaseChangedEventArgs args) return;
            RefreshWaveForPhase(args.Phase);
        }

        void OnCurrentWaveChanged(object sender, GameEventArgs e)
        {
            if (e is not CurrentWaveChangedEventArgs) return;
            RefreshWaveForPhase(GameEntry.State != null ? GameEntry.State.phase : GamePhase.Preparing);
        }

        void RefreshGold(int gold)
        {
            if (goldText != null)
                goldText.text = $"金币: {gold}";
        }

        void RefreshWaveForPhase(GamePhase phase)
        {
            if (phase == GamePhase.Preparing)
            {
                if (waveText != null)
                    waveText.text = "战斗未开始";
                return;
            }

            var state = GameEntry.State;
            RefreshWave(state != null ? state.currentWave : 0);
        }

        void RefreshWave(int waveNumber)
        {
            if (waveText == null) return;

            if (GameEntry.State != null && GameEntry.State.phase == GamePhase.Preparing)
            {
                waveText.text = "战斗未开始";
                return;
            }

            if (waveNumber <= 0)
            {
                waveText.text = "战斗未开始";
                return;
            }

            int total = GameEntry.State?.TotalWaves ?? 0;
            waveText.text = total > 0
                ? $"波次: {waveNumber}/{total}"
                : $"波次: {waveNumber}";
        }

        void RefreshHp(int current, int max)
        {
            if (hpText != null)
                hpText.text = $"生命: {current}/{max}";
        }
    }
}
