using GameFramework.Event;
using Mirror;
using UnityEngine;
using UnityEngine.Events;

namespace Tower
{
    /// <summary>
    /// 服务端权威的全局游戏状态（共享金币等）。由 GameNetworkManager 在服务器启动时从预制体 Spawn。
    /// </summary>
    public class GameState : NetworkBehaviour
    {
        [SyncVar(hook = nameof(OnGoldChanged))]
        public int sharedGold = 100;

        public UnityEvent<int> onGoldChanged = new();

        bool eventSubscribed;

        public override void OnStartServer()
        {
            GameEntry.RegisterState(this);
            GameEntry.Event.Subscribe(EventCommon.EnemyKilled, OnEnemyKilled);
            eventSubscribed = true;
        }

        public override void OnStartClient()
        {
            GameEntry.RegisterState(this);
        }

        void OnDestroy()
        {
            if (eventSubscribed)
            {
                GameEntry.Event.Unsubscribe(EventCommon.EnemyKilled, OnEnemyKilled);
                eventSubscribed = false;
            }

            GameEntry.UnregisterState(this);
        }

        void OnEnemyKilled(object sender, GameEventArgs e)
        {
            if (!isServer) return;
            if (e is EnemyKilledEventArgs args)
                sharedGold += args.GoldAmount;
        }

        [Server]
        public bool TrySpend(int amount)
        {
            if (sharedGold < amount) return false;
            sharedGold -= amount;
            return true;
        }

        void OnGoldChanged(int oldVal, int newVal)
        {
            onGoldChanged?.Invoke(newVal);
        }
    }
}
