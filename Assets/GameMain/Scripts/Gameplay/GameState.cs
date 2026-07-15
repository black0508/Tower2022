using Mirror;
using UnityEngine;

namespace Tower
{
    /// <summary>
    /// 整局游戏阶段：准备 → 波前倒计时 → 波次 → 胜负。
    /// </summary>
    public enum GamePhase
    {
        Preparing,
        PreWave,
        Wave,
        Victory,
        Defeat,
    }

    /// <summary>
    /// 全局游戏状态（网络对象）：只承载当前局的同步字段和权威变更接口。
    /// 阶段推进由 WaveDirector 负责；玩家列表由 PlayerManagerComponent 负责。
    /// </summary>
    public class GameState : NetworkBehaviour
    {
        [SyncVar(hook = nameof(OnGoldChanged))]
        public int sharedGold = 100;

        [SyncVar(hook = nameof(OnPhaseChanged))]
        public GamePhase phase = GamePhase.Preparing;

        /// <summary>当前波次（1-based）；Preparing 时为 0。</summary>
        [SyncVar(hook = nameof(OnCurrentWaveChanged))]
        public int currentWave;

        /// <summary>PreWave 剩余秒数。</summary>
        [SyncVar] public float preWaveTimer;

        /// <summary>本局已获得的全局 Mutation（只同步 ID）。</summary>
        public readonly SyncList<int> activeMutationIds = new();

        /// <summary>总波次：服务端由 WaveDirector 写入，客户端通过 TargetRpc 或 RpcStartBattle 同步。</summary>
        public int totalWaves;

        public int TotalWaves => totalWaves;

        public override void OnStartServer()
        {
            GameEntry.RegisterState(this);

            // 服务端专属组件运行时挂载（Prefab 双端实例化，不能直接挂）
            var director = gameObject.AddComponent<WaveDirector>();
            director.InitServer(this);
        }

        public override void OnStartClient()
        {
            GameEntry.RegisterState(this);

            if (GameEntry.UI != null && !GameEntry.UI.HasUIForm(UIFormId.GamingForm))
                GameEntry.UI.OpenUIForm(UIFormId.GamingForm);
        }

        void OnDestroy()
        {
            GameEntry.UnregisterState(this);
        }

        // ===== 服务端权威变更接口 =====

        [Server]
        public bool TrySpend(int amount)
        {
            if (sharedGold < amount) return false;
            sharedGold -= amount;
            return true;
        }

        [Server]
        public void AddGold(int amount)
        {
            if (amount <= 0) return;
            sharedGold += amount;
        }

        [Server]
        public void NotifyDefeat()
        {
            if (phase == GamePhase.Defeat || phase == GamePhase.Victory) return;
            phase = GamePhase.Defeat;
            Debug.Log("[Server] === DEFEAT ===");
        }

        [Server]
        public void SendBattleInfoTo(NetworkConnectionToClient conn)
        {
            TargetSyncBattleInfo(conn, totalWaves);
        }

        [Server]
        public void BroadcastStartBattle()
        {
            RpcStartBattle(totalWaves);
        }

        [Server]
        public void AddMutation(MutationId id)
        {
            int v = (int)id;
            for (int i = 0; i < activeMutationIds.Count; i++)
                if (activeMutationIds[i] == v) return;

            activeMutationIds.Add(v);
            RecalculateAllCombatAttributes();
            Debug.Log($"[Server] Mutation added: {id}");
        }

        [Server]
        public void RecalculateAllCombatAttributes()
        {
            var all = FindObjectsOfType<AttributeComponent>();
            for (int i = 0; i < all.Length; i++)
                all[i].Recalculate();
        }

        // ===== SyncVar Hook → Event =====

        void OnGoldChanged(int oldVal, int newVal)
        {
            GameEntry.Event.Fire(this, SharedGoldChangedEventArgs.Create(newVal));
        }

        void OnPhaseChanged(GamePhase oldVal, GamePhase newVal)
        {
            GameEntry.Event.Fire(this, GamePhaseChangedEventArgs.Create(newVal));
        }

        void OnCurrentWaveChanged(int oldVal, int newVal)
        {
            GameEntry.Event.Fire(this, CurrentWaveChangedEventArgs.Create(newVal));
        }

        // ===== RPC =====

        [ClientRpc]
        void RpcStartBattle(int total)
        {
            totalWaves = total;
            GameEntry.Event.Fire(this, CurrentWaveChangedEventArgs.Create(currentWave));
        }

        [TargetRpc]
        void TargetSyncBattleInfo(NetworkConnectionToClient _, int total)
        {
            totalWaves = total;
            GameEntry.Event.Fire(this, CurrentWaveChangedEventArgs.Create(currentWave));
        }
    }
}
