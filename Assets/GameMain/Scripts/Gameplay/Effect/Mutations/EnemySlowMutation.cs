namespace Tower
{
    public class EnemySlowMutation : MutationDef
    {
        public EnemySlowMutation()
        {
            Id = MutationId.EnemySlow;
            Name = "迟缓领域";
            Description = "所有敌人移速 -20%";
            Filter = MutationTarget.Enemies;
        }

        public override void CollectModifiers(IStatModifierBuffer buffer) =>
            buffer.AddPercent(AttributeKey.Speed, -0.20f);
    }
}
