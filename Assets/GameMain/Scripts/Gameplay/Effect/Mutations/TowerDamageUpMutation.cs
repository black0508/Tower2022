namespace Tower
{
    public class TowerDamageUpMutation : MutationDef
    {
        public TowerDamageUpMutation()
        {
            Id = MutationId.TowerDamageUp;
            Name = "火力强化";
            Description = "所有防御塔伤害 +25%";
            Filter = MutationTarget.Towers;
        }

        public override void CollectModifiers(IStatModifierBuffer buffer) =>
            buffer.AddPercent(AttributeKey.Damage, 0.25f);
    }
}
