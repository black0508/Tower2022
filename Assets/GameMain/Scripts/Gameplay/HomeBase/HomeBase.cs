using GameFramework.Event;
using Mirror;
using UnityEngine;

namespace Tower
{
    [RequireComponent(typeof(AttributeComponent))]
    public class HomeBase : NetworkBehaviour, ICombatEntity
    {
        [SerializeField] int damagePerEnemy = 1;

        AttributeComponent attribute;
        bool defeated;

        void Awake() => attribute = GetComponent<AttributeComponent>();

        public override void OnStartServer()
        {
            GameEntry.RegisterHomeBase(this);
            attribute.Recalculate();
            attribute.InitHpFull();
            attribute.Hp.OnValueChanged += OnHpChanged;
            GameEntry.Event.Subscribe(EnemyRemovedEventArgs.EventId, OnEnemyRemoved);
            FireHpEvent(attribute.CurrentHp);
        }

        public override void OnStopServer()
        {
            if (attribute != null)
                attribute.Hp.OnValueChanged -= OnHpChanged;
            GameEntry.Event.Unsubscribe(EnemyRemovedEventArgs.EventId, OnEnemyRemoved);
            GameEntry.UnregisterHomeBase(this);
        }

        public override void OnStartClient()
        {
            GameEntry.RegisterHomeBase(this);
            attribute.Hp.OnValueChanged += OnHpChanged;
            FireHpEvent(attribute.CurrentHp);
        }

        public override void OnStopClient()
        {
            if (attribute != null)
                attribute.Hp.OnValueChanged -= OnHpChanged;
            GameEntry.UnregisterHomeBase(this);
        }

        void OnHpChanged(float current) => FireHpEvent(current);

        void FireHpEvent(float current)
        {
            int cur = Mathf.RoundToInt(current);
            int max = Mathf.RoundToInt(attribute.GetMaxHp());
            GameEntry.Event.Fire(this, HomeBaseHpChangedEventArgs.Create(cur, max));
        }

        void OnEnemyRemoved(object sender, GameEventArgs e)
        {
            if (!isServer) return;
            if (e is EnemyRemovedEventArgs args && args.Reason == EnemyRemoveReason.ReachedBase)
            {
                int dmg = args.BaseDamage > 0 ? args.BaseDamage : damagePerEnemy;
                var info = new DamageInfo
                {
                    source = null,
                    target = netIdentity,
                    amount = dmg,
                    type = DamageType.Physical,
                    tags = DamageTag.Direct,
                };
                DamageService.Apply(ref info);
            }
        }

        [Server]
        public void OnFatalHit(ref DamageInfo info)
        {
            if (defeated) return;
            defeated = true;
            GameEntry.State?.NotifyDefeat();
        }
    }
}
