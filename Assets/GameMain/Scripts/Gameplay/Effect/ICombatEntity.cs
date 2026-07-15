namespace Tower
{
    /// <summary>可受伤单位：死亡时由 DamageService 回调。</summary>
    public interface ICombatEntity
    {
        void OnFatalHit(ref DamageInfo info);
    }
}
