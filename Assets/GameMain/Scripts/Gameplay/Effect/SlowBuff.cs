using Mirror;

namespace Tower
{
    /// <summary>
    /// 减速 Buff：按百分比降低 Speed。Frost 塔命中时挂上。
    /// percent 为负值（如 -0.4 表示减速 40%）。
    /// </summary>
    public class SlowBuff : BuffBehaviour
    {
        public override BuffDefId DefId => BuffDefId.Slow;
        public override float DefaultDuration => 3f;

        public float percent = -0.4f;

        public override void CollectModifiers(IStatModifierBuffer buffer) =>
            buffer.AddPercent(AttributeKey.Speed, percent);

        public override void OnApply(BuffHolder holder, bool isServer)
        {
            base.OnApply(holder, isServer);
            if (!isServer)
                holder.GetComponent<Enemy>()?.SetSlowVisual(true);
        }

        public override void OnRemove(bool isServer)
        {
            if (!isServer)
                holder.GetComponent<Enemy>()?.SetSlowVisual(false);
        }

        // percent 参与同步：中途加入的客户端也能拿到正确减速强度
        public override void Serialize(NetworkWriter writer) => writer.WriteFloat(percent);
        public override void Deserialize(NetworkReader reader) => percent = reader.ReadFloat();

        public override void Clear()
        {
            base.Clear();
            percent = -0.4f;
        }
    }
}
