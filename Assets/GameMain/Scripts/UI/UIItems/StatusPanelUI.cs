using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework.Event;
using UnityEngine;
using UnityEngine.UI;

namespace Tower
{
    /// <summary>
    /// 中央阶段提示：Preparing 就绪文案、波前倒计时。
    /// Init/Clear 由 GamingForm 统一驱动；自行订阅关心的 GF 事件。
    /// </summary>
    public class StatusPanelUI : MonoBehaviour
    {
        [SerializeField] Text statusText;

        CancellationTokenSource m_PreWaveCts;

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
            StopPreWaveCountdown();
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
            StopPreWaveCountdown();

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
                case GamePhase.Voting:
                    gameObject.SetActive(false);
                    break;

                case GamePhase.PreWave:
                    gameObject.SetActive(true);
                    RefreshPreWaveText();
                    m_PreWaveCts = new CancellationTokenSource();
                    TickPreWaveAsync(m_PreWaveCts.Token).Forget();
                    break;

                case GamePhase.Victory:
                case GamePhase.Defeat:
                    gameObject.SetActive(false);
                    break;
            }
        }

        void StopPreWaveCountdown()
        {
            if (m_PreWaveCts == null) return;
            m_PreWaveCts.Cancel();
            m_PreWaveCts.Dispose();
            m_PreWaveCts = null;
        }

        async UniTaskVoid TickPreWaveAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var state = GameEntry.State;
                if (state == null || state.phase != GamePhase.PreWave) break;

                RefreshPreWaveText();
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }

        void RefreshPreWaveText()
        {
            if (statusText == null) return;

            var state = GameEntry.State;
            if (state == null) return;

            statusText.text = $"第 {state.currentWave} 波 {Mathf.CeilToInt(state.preWaveTimer)} 秒后开始";
        }

        static bool GetLocalPlayerReady()
        {
            var local = GameEntry.PlayerManager?.GetLocalPlayer();
            return local != null && local.isReady;
        }
    }
}
