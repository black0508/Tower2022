using GameFramework.Event;
using Mirror;
using UnityEngine;

namespace Tower
{
    /// <summary>
    /// 服务端权威的全局游戏状态（共享金币等）。由 GameNetworkManager 在服务器启动时从预制体 Spawn。
    /// </summary>
    public class GameState : NetworkBehaviour
    {
        [SyncVar(hook = nameof(OnGoldChanged))]
        public int sharedGold = 100;

        public override void OnStartServer()
        {
            GameEntry.RegisterState(this);
            GameEntry.Event.Subscribe(EnemyKilledEventArgs.EventId, OnEnemyKilled);
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
            GameEntry.Event.Unsubscribe(EnemyKilledEventArgs.EventId, OnEnemyKilled); 
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
            GameEntry.Event.Fire(this, SharedGoldChangedEventArgs.Create(newVal));
        }
    }
}
