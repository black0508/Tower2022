using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework.Event;
using UnityEngine;
using UnityEngine.UI;

namespace Tower
{
    /// <summary>
    /// 中央阶段提示：Preparing 就绪文案、波间倒计时。
    /// Init/Clear 由 GamingForm 统一驱动；自行订阅关心的 GF 事件。
    /// </summary>
    public class StatusPanelUI : MonoBehaviour
    {
        [SerializeField] Text statusText;

        CancellationTokenSource m_BetweenWavesCts;

        public void Init()
        {
            if (statusText == null)
                statusText = GetComponentInChildren<Text>(true);

            GameEntry.Event.Subscribe(GamePhaseChangedEventArgs.EventId, OnGamePhaseChanged);
            GameEntry.Event.Subscribe(PlayerReadyChangedEventArgs.EventId, OnPlayerReadyChanged);

            var state = GameEntry.State;
            UpdateStatusUI(state != null ? state.phase : GamePhase.Preparing);
        }

        public void Clear()
        {
            GameEntry.Event.Unsubscribe(GamePhaseChangedEventArgs.EventId, OnGamePhaseChanged);
            GameEntry.Event.Unsubscribe(PlayerReadyChangedEventArgs.EventId, OnPlayerReadyChanged);
            StopBetweenWavesCountdown();
        }

        void OnGamePhaseChanged(object sender, GameEventArgs e)
        {
            if (e is not GamePhaseChangedEventArgs args) return;

            UpdateStatusUI(args.Phase);
        }

        void OnPlayerReadyChanged(object sender, GameEventArgs e)
        {
            if (GameEntry.State?.phase == GamePhase.Preparing)
                UpdateStatusUI(GamePhase.Preparing);
        }

        void UpdateStatusUI(GamePhase phase)
        {
            StopBetweenWavesCountdown();

            switch (phase)
            {
                case GamePhase.Preparing:
                    gameObject.SetActive(true);
                    if (statusText != null)
                        statusText.text = GetLocalPlayerReady()
                            ? "等待其他玩家..."
                            : "按 SPACE 准备";
                    break;

                case GamePhase.Wave:
                    gameObject.SetActive(false);
                    break;

                case GamePhase.BetweenWaves:
                    gameObject.SetActive(true);
                    RefreshBetweenWavesText();
                    m_BetweenWavesCts = new CancellationTokenSource();
                    TickBetweenWavesAsync(m_BetweenWavesCts.Token).Forget();
                    break;

                case GamePhase.Victory:
                case GamePhase.Defeat:
                    gameObject.SetActive(false);
                    break;
            }
        }

        void StopBetweenWavesCountdown()
        {
            if (m_BetweenWavesCts == null) return;
            m_BetweenWavesCts.Cancel();
            m_BetweenWavesCts.Dispose();
            m_BetweenWavesCts = null;
        }

        async UniTaskVoid TickBetweenWavesAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var state = GameEntry.State;
                if (state == null || state.phase != GamePhase.BetweenWaves) break;

                RefreshBetweenWavesText();
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }

        void RefreshBetweenWavesText()
        {
            if (statusText == null) return;

            var state = GameEntry.State;
            if (state == null) return;

            statusText.text = $"第 {state.currentWave + 1} 波 {Mathf.CeilToInt(state.betweenWavesTimer)} 秒后开始";
        }

        static bool GetLocalPlayerReady()
        {
            var state = GameEntry.State;
            if (state == null) return false;

            foreach (var p in state.Players)
            {
                if (p.isLocalPlayer) return p.isReady;
            }

            return false;
        }
    }
}
