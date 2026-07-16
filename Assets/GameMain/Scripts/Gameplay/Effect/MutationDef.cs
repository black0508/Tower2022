using Mirror;

namespace Tower
{
    //全局buff
    public class MutationDef
    {
        public MutationId Id;
        public string Name;
        public string Description;
        public MutationTarget Filter = MutationTarget.All;

        public virtual void CollectModifiers(IStatModifierBuffer buffer) { }
        public virtual void OnDamageDealt(ref DamageInfo info) { }
        public virtual void OnDamageTaken(ref DamageInfo info) { }

        public bool Matches(NetworkIdentity unit)
        {
            if (unit == null) return false;
            return Filter switch
            {
                MutationTarget.All => true,
                MutationTarget.Towers => unit.GetComponent<TowerUnit>() != null,
                MutationTarget.Enemies => unit.GetComponent<Enemy>() != null,
                _ => false,
            };
        }
    }
}
