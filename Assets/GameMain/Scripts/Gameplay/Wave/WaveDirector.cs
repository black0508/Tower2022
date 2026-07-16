using System.Collections.Generic;
using GameFramework.Event;
using Mirror;
using UnityEngine;

namespace Tower
{
    /// <summary>
    /// 服务端专属：阶段状态机 + 波次推进 + 击杀奖励监听。
    /// 由 GameState.OnStartServer 动态 AddComponent 添加（不挂 Prefab）。
    /// </summary>
    public class WaveDirector : MonoBehaviour
    {
        const float VoteDuration = 30f;

        GameState gameState;
        WaveManager m_WaveManager;

        /// <summary>由 GameState.OnStartServer 调用，注入依赖并初始化。</summary>
        public void InitServer(GameState state)
        {
            gameState = state;

            EnsureWaveManager();
            if (m_WaveManager == null)
            {
                Debug.LogError("[Server] WaveManager not found in scene.");
                enabled = false;
                return;
            }

            m_WaveManager.InitServer();
            gameState.totalWaves = m_WaveManager.config != null ? m_WaveManager.config.waves.Length : 0;

            GameEntry.Event.Subscribe(EnemyRemovedEventArgs.EventId, OnEnemyRemoved);
        }

        void OnDestroy()
        {
            GameEntry.Event.Unsubscribe(EnemyRemovedEventArgs.EventId, OnEnemyRemoved);
        }

        void EnsureWaveManager()
        {
            if (m_WaveManager == null)
                m_WaveManager = FindObjectOfType<WaveManager>();
        }

        void Update()
        {
            if (gameState == null) return;
            if (!NetworkServer.active) return;

            TickPhase(Time.deltaTime);
        }

        void TickPhase(float deltaTime)
        {
            switch (gameState.phase)
            {
                case GamePhase.Preparing:
                    TickPreparing();
                    break;
                case GamePhase.PreWave:
                    TickPreWave(deltaTime);
                    break;
                case GamePhase.Wave:
                    TickWave();
                    break;
                case GamePhase.Voting:
                    TickVoting(deltaTime);
                    break;
            }
        }

        void TickPreparing()
        {
            var pm = GameEntry.PlayerManager;
            if (pm != null && pm.AreAllPlayersReady())
                BeginBattle();
        }

        /// <summary>全员 Ready 后开战：清槽位、同步、进入第 1 波 PreWave。</summary>
        void BeginBattle()
        {
            Debug.Log($"[Server] Phase: {gameState.phase} -> {GamePhase.PreWave} (BeginBattle)");

            GameEntry.Build?.ClearAllOccupancy();
            gameState.BroadcastStartBattle();
            gameState.currentWave = 1;
            gameState.preWaveTimer = GetDelayBeforeWave(0);
            gameState.phase = GamePhase.PreWave;
        }

        void TickPreWave(float deltaTime)
        {
            gameState.preWaveTimer -= deltaTime;
            if (gameState.preWaveTimer > 0) return;

            m_WaveManager?.StartWave(gameState.currentWave - 1);
            TransitionTo(GamePhase.Wave);
        }

        void TickWave()
        {
            if (m_WaveManager == null) return;

            m_WaveManager.ServerTick();
            if (!m_WaveManager.IsCurrentWaveCleared) return;

            if (m_WaveManager.IsLastWave)
            {
                TransitionTo(GamePhase.Victory);
                return;
            }

            if (ShouldVoteAfter(gameState.currentWave) && TryStartVote())
            {
                TransitionTo(GamePhase.Voting);
                return;
            }

            AdvanceToNextPreWave();
        }

        bool ShouldVoteAfter(int waveNumber)
        {
            var config = m_WaveManager?.config;
            int idx = waveNumber - 1;
            return config != null && idx >= 0 && idx < config.waves.Length
                && config.waves[idx].voteAfterWave;
        }

        bool TryStartVote()
        {
            var pool = new List<int>();
            foreach (var id in MutationCatalog.All)
            {
                int v = (int)id;
                if (!gameState.activeMutationIds.Contains(v))
                    pool.Add(v);
            }
            if (pool.Count == 0) return false;

            gameState.voteOptions.Clear();
            int take = Mathf.Min(3, pool.Count);
            for (int i = 0; i < take; i++)
            {
                int pick = Random.Range(0, pool.Count);
                gameState.voteOptions.Add(pool[pick]);
                pool.RemoveAt(pick);
            }

            gameState.playerVotes.Clear();
            gameState.voteTimer = VoteDuration;
            return true;
        }

        void TickVoting(float dt)
        {
            gameState.voteTimer -= dt;

            bool timeUp = gameState.voteTimer <= 0f;
            bool allVoted = AllActivePlayersVoted();
            if (!timeUp && !allVoted) return;

            int winnerOption = Tally();
            if (winnerOption >= 0 && winnerOption < gameState.voteOptions.Count)
                gameState.AddMutation((MutationId)gameState.voteOptions[winnerOption]);

            gameState.voteOptions.Clear();
            gameState.playerVotes.Clear();
            AdvanceToNextPreWave();
        }

        bool AllActivePlayersVoted()
        {
            var pm = GameEntry.PlayerManager;
            int players = pm?.Players.Count ?? 0;
            return players > 0 && gameState.playerVotes.Count >= players;
        }

        int Tally()
        {
            int n = gameState.voteOptions.Count;
            if (n == 0) return -1;

            var counts = new int[n];
            foreach (var kv in gameState.playerVotes)
                if (kv.Value >= 0 && kv.Value < n) counts[kv.Value]++;

            int best = 0;
            for (int i = 1; i < n; i++)
                if (counts[i] > counts[best]) best = i;
            return best;
        }

        void AdvanceToNextPreWave()
        {
            gameState.currentWave++;
            gameState.preWaveTimer = GetDelayBeforeWave(gameState.currentWave - 1);
            TransitionTo(GamePhase.PreWave);
        }

        void TransitionTo(GamePhase newPhase)
        {
            Debug.Log($"[Server] Phase: {gameState.phase} -> {newPhase}");
            gameState.phase = newPhase;

            switch (newPhase)
            {
                case GamePhase.Victory:
                    Debug.Log("[Server] === VICTORY ===");
                    break;
                case GamePhase.Defeat:
                    Debug.Log("[Server] === DEFEAT ===");
                    break;
            }
        }

        float GetDelayBeforeWave(int waveIndex)
        {
            var config = m_WaveManager?.config;
            if (config == null || waveIndex < 0 || waveIndex >= config.waves.Length)
                return 0;
            return config.waves[waveIndex].delayBeforeWave;
        }

        void OnEnemyRemoved(object sender, GameEventArgs e)
        {
            if (!NetworkServer.active) return;
            if (e is EnemyRemovedEventArgs args && args.Reason == EnemyRemoveReason.KilledByPlayer)
                gameState.AddGold(args.GoldAmount);
        }
    }
}
