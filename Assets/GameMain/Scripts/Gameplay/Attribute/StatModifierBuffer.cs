using System.Collections.Generic;

namespace Tower
{
    public interface IStatModifierBuffer
    {
        void AddFlat(AttributeKey key, float value);
        void AddPercent(AttributeKey key, float value);
    }

    /// <summary>Recalculate 用的黑板；不是常驻组件。</summary>
    public sealed class StatModifierBuffer : IStatModifierBuffer
    {
        public readonly Dictionary<AttributeKey, float> Flat = new();
        public readonly Dictionary<AttributeKey, float> Percent = new();

        public void Clear()
        {
            Flat.Clear();
            Percent.Clear();
        }

        public void AddFlat(AttributeKey key, float value)
        {
            Flat.TryGetValue(key, out float cur);
            Flat[key] = cur + value;
        }

        public void AddPercent(AttributeKey key, float value)
        {
            Percent.TryGetValue(key, out float cur);
            Percent[key] = cur + value;
        }

        public float GetFlat(AttributeKey key) =>
            Flat.TryGetValue(key, out float v) ? v : 0f;

        public float GetPercent(AttributeKey key) =>
            Percent.TryGetValue(key, out float v) ? v : 0f;
    }
}
