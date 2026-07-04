namespace Tower
{
    /// <summary>
    /// 塔建造配置（UI 展示与逻辑层共用）。
    /// </summary>
    public struct TowerBuildInfo
    {
        public int TowerConfigId;
        public int Cost;

        public TowerBuildInfo(int towerConfigId, int cost)
        {
            TowerConfigId = towerConfigId;
            Cost = cost;
        }
    }
}
