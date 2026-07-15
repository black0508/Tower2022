using Mirror;

namespace Tower
{
    public abstract class BuffBehaviour
    {
        public abstract BuffDefId DefId { get; }
        public virtual int Priority => EffectPriority.PreCalculation;
        public virtual float DefaultDuration => 3f;

        public float startTimeOnServer;
        public float duration;
        public int stackCount = 1;

        protected BuffHolder holder;

        public virtual void CollectModifiers(IStatModifierBuffer buffer) { }

        public virtual void OnApply(BuffHolder holder, bool isServer)
        {
            this.holder = holder;
        }

        public virtual void OnRemove(bool isServer) { }
        public virtual void OnTick(float dt) { }

        public virtual void OnDamageDealt(ref DamageInfo info) { }
        public virtual void OnDamageTaken(ref DamageInfo info) { }
        public virtual void OnKill(ref DamageInfo info) { }
        public virtual void OnBeKilled(ref DamageInfo info) { }

        public virtual void Serialize(NetworkWriter writer) { }
        public virtual void Deserialize(NetworkReader reader) { }
    }
}
