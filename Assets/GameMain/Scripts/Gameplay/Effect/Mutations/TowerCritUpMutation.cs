namespace Tower
{
    public class TowerCritUpMutation : MutationDef
    {
        public TowerCritUpMutation()
        {
            Id = MutationId.TowerCritUp;
            Name = "精准打击";
            Description = "防御塔暴击率 +10%";
            Filter = MutationTarget.Towers;
        }

        public override void OnDamageDealt(ref DamageInfo info) =>
            info.critChance += 0.10f;
    }
}
