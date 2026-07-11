using GameFramework.Event;
using Mirror;
using UnityEngine;

namespace Tower
{
    /// <summary>
    /// 玩家主基地 HP（场景静态 NetworkBehaviour）。
    /// </summary>
    public class HomeBase : NetworkBehaviour
    {
        [SyncVar(hook = nameof(OnHpChanged))]
        public int hp = 10;

        public int maxHp = 10;

        [SerializeField] int damagePerEnemy = 1;

        public override void OnStartServer()
        {
            GameEntry.Event.Subscribe(EnemyKilledEventArgs.EventId, OnEnemyKilled);
        }

        public override void OnStopServer()
        {
            GameEntry.Event.Unsubscribe(EnemyKilledEventArgs.EventId, OnEnemyKilled);
        }

        void OnEnemyKilled(object sender, GameEventArgs e)
        {
            if (!isServer) return;
            if (e is EnemyKilledEventArgs args && args.Reason == EnemyRemoveReason.ReachedBase)
                TakeDamage(args.BaseDamage > 0 ? args.BaseDamage : damagePerEnemy);
        }

        [Server]
        void TakeDamage(int dmg)
        {
            hp = Mathf.Max(0, hp - dmg);
            Debug.Log($"[Server] HomeBase took {dmg} damage, hp={hp}");

            if (hp <= 0)
            {
                var state = GameEntry.State;
                if (state != null)
                    state.NotifyDefeat();
            }
        }

        void OnHpChanged(int oldVal, int newVal)
        {
            GameEntry.Event.Fire(this, HomeBaseHpChangedEventArgs.Create(newVal, maxHp));
        }
    }
}
