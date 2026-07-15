using System.Collections.Generic;
using Mirror;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Tower
{
    public class AttributeComponent : NetworkBehaviour
    {
        [Title("初始属性 (Base)")]
        [InfoBox("敌：MaxHp / Speed / BaseDamage / GoldReward\n塔：MaxHp / AttackInterval / Damage / ProjectileSpeed\n基地：MaxHp\n注意：Damage(塔攻击) ≠ BaseDamage(敌撞家)", InfoMessageType.None)]
        [TableList(AlwaysExpanded = true, DrawScrollView = false)]
        [ValidateInput(nameof(ValidateInitialAttributes))]
        [SerializeField] AttributeEntry[] initialAttributes;

        public readonly SyncList<AttributeEntry> syncedAttributes = new();

        [SyncVar(hook = nameof(OnSyncedHpHook))]
        float syncedCurrentHp;

        readonly Dictionary<AttributeKey, FloatAttribute> attributes = new();
        readonly StatModifierBuffer modifierBuffer = new();
        readonly ResourceAttribute hp = new();

        BuffHolder buffHolder;

        public ResourceAttribute Hp => hp;
        public bool IsAlive => hp.IsAlive;
        public float CurrentHp => hp.Value;

        bool ValidateInitialAttributes(AttributeEntry[] entries, ref string errorMessage)
        {
            if (entries == null || entries.Length == 0) return true;

            var seen = new HashSet<AttributeKey>();
            for (int i = 0; i < entries.Length; i++)
            {
                var key = entries[i].key;
                if (key == AttributeKey.None)
                {
                    errorMessage = $"第 {i} 项 Key 不能为 None";
                    return false;
                }
                if (!seen.Add(key))
                {
                    errorMessage = $"重复属性 Key：{key}（第 {i} 项）";
                    return false;
                }
            }
            return true;
        }

        void Awake()
        {
            buffHolder = GetComponent<BuffHolder>();

            if (initialAttributes == null) return;
            foreach (var e in initialAttributes)
            {
                var attr = new FloatAttribute(e.key);
                attr.Initialize(e.value);
                attributes[e.key] = attr;
            }
        }

        public override void OnStartServer()
        {
            Recalculate();
        }

        public override void OnStartClient()
        {
            syncedAttributes.OnAdd += OnSyncAdd;
            syncedAttributes.OnSet += OnSyncSet;

            for (int i = 0; i < syncedAttributes.Count; i++)
                ApplySyncedEntry(syncedAttributes[i]);

            hp.Init(syncedCurrentHp);
        }

        public override void OnStopClient()
        {
            syncedAttributes.OnAdd -= OnSyncAdd;
            syncedAttributes.OnSet -= OnSyncSet;
        }

        void OnSyncAdd(int idx)
        {
            if (isServer) return;
            ApplySyncedEntry(syncedAttributes[idx]);
        }

        void OnSyncSet(int idx, AttributeEntry oldEntry)
        {
            if (isServer) return;
            ApplySyncedEntry(syncedAttributes[idx]);
        }

        void ApplySyncedEntry(AttributeEntry entry)
        {
            GetOrCreate(entry.key).Set(entry.value);
        }

        void OnSyncedHpHook(float oldVal, float newVal)
        {
            hp.Set(newVal);
        }

        public FloatAttribute Get(AttributeKey key) =>
            attributes.TryGetValue(key, out var a) ? a : null;

        public float GetBase(AttributeKey key) => Get(key)?.Base ?? 0f;
        public float GetFinal(AttributeKey key) => Get(key)?.Value ?? 0f;

        public float GetMaxHp()
        {
            float max = GetFinal(AttributeKey.MaxHp);
            return max > 0f ? max : 1f;
        }

        FloatAttribute GetOrCreate(AttributeKey key)
        {
            if (attributes.TryGetValue(key, out var a)) return a;
            a = new FloatAttribute(key);
            a.Initialize(0f);
            attributes[key] = a;
            return a;
        }

        [Server]
        public void SetBase(AttributeKey key, float value)
        {
            GetOrCreate(key).SetBase(value);
            Recalculate();
        }

        [Server]
        public void InitHpFull()
        {
            float max = GetMaxHp();
            syncedCurrentHp = max;
            hp.Init(max);
        }

        [Server]
        public void ModHp(float delta)
        {
            float next = Mathf.Clamp(syncedCurrentHp + delta, 0f, GetMaxHp());
            syncedCurrentHp = next;
            hp.Set(next);
        }

        [Server]
        public void ClampHpToMax()
        {
            float max = GetMaxHp();
            if (syncedCurrentHp > max)
            {
                syncedCurrentHp = max;
                hp.Set(max);
            }
        }

        [Server]
        public void Recalculate()
        {
            CollectModifiersOntoBlackboard();
            ApplyBlackboardToFinals();
            ClampHpToMax();
        }

        void CollectModifiersOntoBlackboard()
        {
            modifierBuffer.Clear();

            if (buffHolder != null)
            {
                foreach (var buff in buffHolder.GetSortedServerBuffs())
                    buff.CollectModifiers(modifierBuffer);
            }

            var state = GameEntry.State;
            if (state == null || netIdentity == null) return;

            for (int i = 0; i < state.activeMutationIds.Count; i++)
            {
                var def = MutationRegistry.Get(state.activeMutationIds[i]);
                if (def != null && def.Matches(netIdentity))
                    def.CollectModifiers(modifierBuffer);
            }
        }

        void ApplyBlackboardToFinals()
        {
            var keys = new HashSet<AttributeKey>(attributes.Keys);
            foreach (var k in modifierBuffer.Flat.Keys) keys.Add(k);
            foreach (var k in modifierBuffer.Percent.Keys) keys.Add(k);

            foreach (var key in keys)
            {
                var attr = GetOrCreate(key);
                float final = (attr.Base + modifierBuffer.GetFlat(key)) * (1f + modifierBuffer.GetPercent(key));
                attr.Set(final);
                WriteToSyncList(key, final);
            }
        }

        void WriteToSyncList(AttributeKey key, float value)
        {
            int idx = FindEntryIdx(key);
            var entry = new AttributeEntry { key = key, value = value };
            if (idx >= 0)
                syncedAttributes[idx] = entry;
            else
                syncedAttributes.Add(entry);
        }

        int FindEntryIdx(AttributeKey key)
        {
            for (int i = 0; i < syncedAttributes.Count; i++)
                if (syncedAttributes[i].key == key) return i;
            return -1;
        }
    }
}
