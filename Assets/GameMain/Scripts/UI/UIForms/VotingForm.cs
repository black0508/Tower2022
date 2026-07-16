using System.Threading;
using Cysharp.Threading.Tasks;
using Mirror;
using UnityEngine;
using UnityEngine.UI;

namespace Tower
{
    /// <summary>波间 3 选 1 投票界面（独立 UIForm，由 GamingForm 在 Voting 阶段打开/关闭）。</summary>
    public class VotingForm : UGUIForm
    {
        [SerializeField] Text timerText;
        [SerializeField] VoteOptionButton[] optionButtons;

        CancellationTokenSource m_Cts;

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);
            BindOptions();
            RefreshCounts();

            var state = GameEntry.State;
            if (state != null)
                state.playerVotes.OnChange += OnVotesChanged;

            StopTick();
            m_Cts = new CancellationTokenSource();
            TickAsync(m_Cts.Token).Forget();
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            var state = GameEntry.State;
            if (state != null)
                state.playerVotes.OnChange -= OnVotesChanged;

            StopTick();
            base.OnClose(isShutdown, userData);
        }

        void BindOptions()
        {
            var state = GameEntry.State;
            if (state == null || optionButtons == null) return;

            for (int i = 0; i < optionButtons.Length; i++)
            {
                if (optionButtons[i] == null) continue;
                bool active = i < state.voteOptions.Count;
                optionButtons[i].gameObject.SetActive(active);
                if (!active) continue;

                var def = MutationRegistry.Get(state.voteOptions[i]);
                int idx = i;
                optionButtons[i].Bind(
                    def?.Name ?? "?",
                    def?.Description ?? "",
                    () => GameEntry.PlayerManager?.GetLocalPlayer()?.CmdVote(idx));
            }
        }

        void OnVotesChanged(SyncDictionary<int, int>.Operation op, int key, int value) =>
            RefreshCounts();

        void RefreshCounts()
        {
            var state = GameEntry.State;
            if (state == null || optionButtons == null) return;

            int n = state.voteOptions.Count;
            var counts = new int[n];
            foreach (var kv in state.playerVotes)
                if (kv.Value >= 0 && kv.Value < n) counts[kv.Value]++;

            int total = GameEntry.PlayerManager?.Players.Count ?? 1;
            for (int i = 0; i < n && i < optionButtons.Length; i++)
            {
                if (optionButtons[i] == null) continue;
                optionButtons[i].SetCount(counts[i], total);
            }
        }

        async UniTaskVoid TickAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var state = GameEntry.State;
                if (state == null || state.phase != GamePhase.Voting) break;

                if (timerText != null)
                    timerText.text = $"{Mathf.CeilToInt(state.voteTimer)}";

                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }

        void StopTick()
        {
            if (m_Cts == null) return;
            m_Cts.Cancel();
            m_Cts.Dispose();
            m_Cts = null;
        }
    }
}
