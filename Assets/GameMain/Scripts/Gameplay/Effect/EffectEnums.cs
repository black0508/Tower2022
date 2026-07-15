using System;

namespace Tower
{
    public enum BuffDefId
    {
        None = 0,
        Slow = 1001, // Day3
    }

    public enum MutationId
    {
        None = 0,
    }

    public enum MutationTarget
    {
        All = 0,
        Towers = 1,
        Enemies = 2,
    }

    /// <summary>BuffBehaviour.Priority 的档位常量（按 int 升序执行）。</summary>
    public static class EffectPriority
    {
        public const int PreCalculation = 0;
        public const int DamageModification = 100;
        public const int Absorption = 200;
        public const int PostCalculation = 300;
    }

    public enum DamageType
    {
        Physical = 0,
    }

    [Flags]
    public enum DamageTag
    {
        None = 0,
        Direct = 1 << 0,
        Periodic = 1 << 1,
        Reflect = 1 << 2,
        Heal = 1 << 3,
    }
}
