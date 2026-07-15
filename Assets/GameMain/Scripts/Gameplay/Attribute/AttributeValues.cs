using System;

namespace Tower
{
    /// <summary>可通知变更的属性基类。对外只暴露 Value。</summary>
    public abstract class AttributeBase<T>
    {
        protected T value;

        public T Value => value;

        public event Action<T> OnValueChanged;

        public void Init(T newValue) => value = newValue;

        public void Set(T newValue)
        {
            if (ValuesEqual(value, newValue)) return;
            value = newValue;
            OnValueChanged?.Invoke(value);
        }

        protected virtual bool ValuesEqual(T a, T b) => Equals(a, b);
    }

    /// <summary>
    /// Stats：Base 为配置底值；Value 为结算后的 Final = (Base+Flat)*(1+Percent)。
    /// </summary>
    public class FloatAttribute : AttributeBase<float>
    {
        public AttributeKey Key { get; }
        public float Base { get; private set; }

        public FloatAttribute(AttributeKey key) => Key = key;

        /// <summary>写入 Base，并把 Value 初始化为同一值。</summary>
        public void Initialize(float baseValue)
        {
            Base = baseValue;
            Init(baseValue);
        }

        public void SetBase(float baseValue) => Base = baseValue;

        protected override bool ValuesEqual(float a, float b) => Math.Abs(a - b) < 0.0001f;
    }

    /// <summary>资源（如 HP）：Value 即当前值。同步仍由 AttributeComponent SyncVar 承载。</summary>
    public class ResourceAttribute : AttributeBase<float>
    {
        public bool IsAlive => Value > 0f;

        protected override bool ValuesEqual(float a, float b) => Math.Abs(a - b) < 0.0001f;
    }
}
