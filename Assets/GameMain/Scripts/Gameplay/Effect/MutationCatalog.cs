namespace Tower
{
    /// <summary>Mutation 定义的双端预设注册表引导 + 随机池来源。</summary>
    public static class MutationCatalog
    {
        static bool s_Registered;

        /// <summary>本作全部可投 Mutation（随机挑选池）。</summary>
        public static readonly MutationId[] All =
        {
            MutationId.TowerDamageUp,
            MutationId.EnemySlow,
            MutationId.TowerCritUp,
        };

        /// <summary>双端各调一次；把 MutationDef 实例填进 MutationRegistry。</summary>
        public static void EnsureRegistered()
        {
            if (s_Registered) return;
            s_Registered = true;

            MutationRegistry.Register(new TowerDamageUpMutation());
            MutationRegistry.Register(new EnemySlowMutation());
            MutationRegistry.Register(new TowerCritUpMutation());
        }
    }
}
