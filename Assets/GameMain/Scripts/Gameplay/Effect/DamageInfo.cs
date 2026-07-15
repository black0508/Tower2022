using Mirror;

namespace Tower
{
    public struct DamageInfo
    {
        public NetworkIdentity source;
        public NetworkIdentity target;
        public float amount;
        public DamageType type;
        public DamageTag tags;
        public float critChance;
        public bool isCritical;
        public bool cancelled;
        public bool killed;

        public bool IsHeal => (tags & DamageTag.Heal) != 0;
    }
}
