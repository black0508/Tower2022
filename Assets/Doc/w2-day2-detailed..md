# W2-Day2 — Effect Pipeline 完整可抄实现

> 日期：2026-07-14  
> 目标工程：`F:\Download\Tower2022-main\Tower2022-main`（**当前 Day1 尚未合入**）  
> 架构：方案 B — Stats / Resource / Unit Buff / Mutation + DamageService  
> Mirror：工程内 `Assets/ThirdParty/Mirror`，SyncList 支持 `OnAdd` / `OnSet` / `OnRemove`  
> **本文是 Day2 唯一依据**：打开即可按文件抄；抄完对照 Checklist 验收

### 与当前仓库命名对齐（重要）

Day1 未做，本文**保持现有类名**：

| 现状 | Day1 计划改名（若你先做 Day1，全文替换即可） |
|------|-----------------------------------------------|
| `TowerBase` | `TowerUnit` |
| `EnemyKilledEventArgs` | `EnemyRemovedEventArgs` |

---

## Day2 目标 / 完成标志 / 不做

**目标**

- 落地 Attribute + Health + BuffHolder + MutationList 空壳 + DamageService
- Enemy / TowerBase / HomeBase / Projectile 迁到新系统
- 删掉旧减速字段；Frost 暂无减速（Day3 SlowBuff 恢复）

**完成标志**

- 一局能跑通；塔打敌扣血；击杀加金币；撞基地扣 HomeBase
- TestBuff 改 Speed，双端能看到变慢
- （可选）Debug Mutation 全敌减速 / 全塔加伤生效

**明确不做**

- SlowBuff 正式版、投票 UI、具体玩法 Mutation
- GamePlayer 挂 Buff/Attr
- Buff 图标 UI

---

# 架构速查（抄代码前读一遍）

```text
Final(stat) = (Base + Flat) * (1 + Percent)
  Flat/Percent ← 本单位 Buff.CollectModifiers + 匹配的 Mutation.CollectModifiers
                （经 StatModifierBuffer 黑板收集）

CurrentHp：挂在 AttributeComponent 上的 Resource（SyncVar），ModHp 立即改，不进 Recalculate
  —— 不是单独 NetworkBehaviour，Prefab 不用再挂 Health 组件

伤害：DamageService.Apply(ref DamageInfo)
  Mutations OnDamageDealt → source Buffs OnDamageDealt → target Buffs OnDamageTaken
  → attrs.ModHp → 死亡回调

同步：Stats Final → SyncList<AttributeEntry>（同一 struct 也用于 Inspector 配 Base）
      CurrentHp → AttributeComponent 上的 SyncVar
      Buff → SyncList<BuffSnapshot>
      Mutation → SyncList<int> on GameState

属性对象：FloatAttribute（Stats）+ ResourceAttribute（CurrentHp 逻辑封装）
  AttributeComponent = 容器 + Recalculate + 同步宿主（SyncVar 必须挂 NetworkBehaviour）
```

### 概念澄清（易混）

**`EffectPriority`**：不是第二套优先级。只是给 `BuffBehaviour.Priority`（一个 int）起的**档位常量名**（如 Absorption=200）。排序仍看每个 Buff 自己的 `Priority` 数值。

**`StatModifierBuffer`**：就是你说的**黑板（Blackboard）**——`Recalculate()` 当帧的临时共享写板。Buff/Mutation 往黑板上写 Flat/Percent 贡献，算完 Final 后清空。不挂网络、不持久、不是与 Buff 平行的系统。

**`AttributeEntry` 只有一份**：Inspector 配初始 Base、SyncList 同步 Final，**共用同一 struct**（都是 key+value）。以前拆成 BaseAttributeEntry / AttributeEntry 是多余的。

**`FloatAttribute`**：单个 Stats。`OnValueChanged` 挂在属性上。

**`ResourceAttribute`**：当前生命等消耗型资源的逻辑封装（Current + 事件）。**同步宿主仍是 AttributeComponent 上的 SyncVar**（Mirror 限制），因此**不再单独挂 HealthResource 组件**。

---

# 文件地图

### 新建

```text
Assets/GameMain/Scripts/Gameplay/Effect/
  DamageType.cs
  DamageTag.cs
  DamageInfo.cs
  EffectPriority.cs
  BuffDefId.cs
  BuffSnapshot.cs
  BuffBehaviour.cs
  BuffBehaviourRegistry.cs
  BuffHolder.cs
  DamageService.cs
  MutationId.cs
  MutationTarget.cs
  MutationDef.cs
  MutationRegistry.cs
  ICombatEntity.cs

Assets/GameMain/Scripts/Gameplay/Attribute/
  AttributeKey.cs
  AttributeEntry.cs
  FloatAttribute.cs
  ResourceAttribute.cs
  IStatModifierBuffer.cs
  StatModifierBuffer.cs
  AttributeComponent.cs
  MirrorEffectSerializers.cs

Assets/GameMain/Scripts/Debug/   （测完可删）
  BuffPipelineDebugTest.cs
```

### 修改

```text
Enemy.cs          — 全量替换为下文
TowerBase.cs      — 全量替换为下文
HomeBase.cs       — 全量替换为下文
ProjectileBase.cs — ServerLaunch 加 source
HomingProjectile.cs / FrostProjectile.cs — 命中走 DamageInfo
GamingForm.cs     — RefreshHpFromScene 读 AttributeComponent.Hp
GameState.cs      — 加 activeMutationIds + AddMutation
```

### Prefab

Enemy / 各塔 Prefab / HomeBase：加 `AttributeComponent` + `BuffHolder`（HomeBase 的 BuffHolder 可选）  
**不必**再挂 Health 组件。Inspector 只配 `AttributeComponent.initialAttributes`（见文末表）。

---

# 新建文件（完整代码）

## 1. `DamageType.cs`

```csharp
namespace Tower
{
    public enum DamageType
    {
        Physical = 0,
    }
}
```

## 2. `DamageTag.cs`

```csharp
using System;

namespace Tower
{
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
```

## 3. `EffectPriority.cs`

> **说明**：档位常量，不是独立系统。`BuffBehaviour.Priority` 返回这些 int 之一（或中间值）。  
> `BuffHolder.GetSortedServerBuffs()` / 伤害回调遍历都按这个 int 升序。  
> 例：破甲类用 `DamageModification`，护盾用 `Absorption`，反伤用 `PostCalculation`。

```csharp
namespace Tower
{
    public static class EffectPriority
    {
        public const int PreCalculation = 0;       // 属性加成、多数 Mutation
        public const int DamageModification = 100; // 破甲、易伤、暴击改写
        public const int Absorption = 200;         // 护盾吸收
        public const int PostCalculation = 300;    // 吸血、反伤
    }
}
```

## 4. `BuffDefId.cs` / `MutationId.cs` / `MutationTarget.cs`

```csharp
namespace Tower
{
    public enum BuffDefId
    {
        None = 0,
        Slow = 1001, // Day3
    }
}
```

```csharp
namespace Tower
{
    public enum MutationId
    {
        None = 0,
        DebugEnemySlow = 9001,   // Day2 自测用，正式 Mutation 用 Day5 id
        DebugTowerDamage = 9002,
    }
}
```

```csharp
namespace Tower
{
    public enum MutationTarget
    {
        All = 0,
        Towers = 1,
        Enemies = 2,
    }
}
```

## 5. `AttributeKey.cs`（无 CurrentHp）

```csharp
namespace Tower
{
    public enum AttributeKey
    {
        None = 0,
        MaxHp = 2,
        Speed = 10,
        BaseDamage = 11,
        GoldReward = 12,
        AttackInterval = 20,
        Damage = 21,
        ProjectileSpeed = 22,
        Range = 23,
    }
}
```

## 6. `AttributeEntry.cs` / `BuffSnapshot.cs`

> **一份 Entry 两用**：Prefab Inspector 配初始 Base；SyncList 同步 Final。结构都是 `key + value`，不必再拆 BaseAttributeEntry。

```csharp
using System;

namespace Tower
{
    [Serializable]
    public struct AttributeEntry
    {
        public AttributeKey key;
        public float value;
    }
}
```

```csharp
using System;

namespace Tower
{
    [Serializable]
    public struct BuffSnapshot
    {
        public int defId;
        public float startTimeOnServer;
        public float duration;
        public int stackCount;
        public byte[] payload;
    }
}
```

## 7. `MirrorEffectSerializers.cs`（SyncList 自定义结构必须有）

```csharp
using Mirror;

namespace Tower
{
    public static class MirrorEffectSerializers
    {
        public static void WriteAttributeEntry(this NetworkWriter writer, AttributeEntry value)
        {
            writer.WriteInt((int)value.key);
            writer.WriteFloat(value.value);
        }

        public static AttributeEntry ReadAttributeEntry(this NetworkReader reader)
        {
            return new AttributeEntry
            {
                key = (AttributeKey)reader.ReadInt(),
                value = reader.ReadFloat(),
            };
        }

        public static void WriteBuffSnapshot(this NetworkWriter writer, BuffSnapshot value)
        {
            writer.WriteInt(value.defId);
            writer.WriteFloat(value.startTimeOnServer);
            writer.WriteFloat(value.duration);
            writer.WriteInt(value.stackCount);
            writer.WriteBytesAndSize(value.payload);
        }

        public static BuffSnapshot ReadBuffSnapshot(this NetworkReader reader)
        {
            return new BuffSnapshot
            {
                defId = reader.ReadInt(),
                startTimeOnServer = reader.ReadFloat(),
                duration = reader.ReadFloat(),
                stackCount = reader.ReadInt(),
                payload = reader.ReadBytesAndSize(),
            };
        }
    }
}
```

## 8. `DamageInfo.cs`

```csharp
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
        public bool cancelled;
        public bool killed;

        public bool IsHeal => (tags & DamageTag.Heal) != 0;
    }
}
```

## 9. `IStatModifierBuffer.cs` / `StatModifierBuffer.cs`

> **黑板（Blackboard）**：Recalculate 当帧的临时共享写板。Buff/Mutation 只往黑板写贡献，不直接改 Final。算完即 Clear。

```csharp
namespace Tower
{
    public interface IStatModifierBuffer
    {
        void AddFlat(AttributeKey key, float value);
        void AddPercent(AttributeKey key, float value);
    }
}
```

```csharp
using System.Collections.Generic;

namespace Tower
{
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
```

## 9b. `FloatAttribute.cs`

> 单个 Stats。事件挂在属性自身。不要把 CurrentHp 做成 FloatAttribute。

```csharp
using System;

namespace Tower
{
    public class FloatAttribute
    {
        readonly AttributeKey key;
        float baseValue;
        float finalValue;

        public AttributeKey Key => key;
        public float Base => baseValue;
        public float Final => finalValue;

        public event Action<float> OnValueChanged;

        public FloatAttribute(AttributeKey key)
        {
            this.key = key;
        }

        public void InitBase(float value)
        {
            baseValue = value;
            finalValue = value;
        }

        public void SetBase(float value) => baseValue = value;

        public void SetFinal(float value)
        {
            if (Math.Abs(finalValue - value) < 0.0001f) return;
            finalValue = value;
            OnValueChanged?.Invoke(finalValue);
        }
    }
}
```

## 9c. `ResourceAttribute.cs`

> 消耗型资源的**逻辑封装**（非 NetworkBehaviour）。  
> Mirror 的 SyncVar 必须挂在 NetworkBehaviour 上，所以真正的同步字段在 `AttributeComponent.syncedCurrentHp`，hook 里调用 `Hp.SetCurrent`。

```csharp
using System;

namespace Tower
{
    public class ResourceAttribute
    {
        float current;

        public float Current => current;
        public bool IsAlive => current > 0f;

        public event Action<float> OnValueChanged;

        public void SetCurrent(float value)
        {
            if (Math.Abs(current - value) < 0.0001f) return;
            current = value;
            OnValueChanged?.Invoke(current);
        }

        public void Init(float value) => current = value;
    }
}
```

## 10. `BuffBehaviour.cs`

```csharp
using Mirror;

namespace Tower
{
    public abstract class BuffBehaviour
    {
        public abstract BuffDefId DefId { get; }
        public virtual int Priority => EffectPriority.PreCalculation;
        public virtual float DefaultDuration => 3f;

        public float startTimeOnServer;
        public float duration;
        public int stackCount = 1;

        protected BuffHolder holder;

        public virtual void CollectModifiers(IStatModifierBuffer buffer) { }

        public virtual void OnApply(BuffHolder holder, bool isServer)
        {
            this.holder = holder;
        }

        public virtual void OnRemove(bool isServer) { }
        public virtual void OnTick(float dt) { }

        public virtual void OnDamageDealt(ref DamageInfo info) { }
        public virtual void OnDamageTaken(ref DamageInfo info) { }
        public virtual void OnKill(ref DamageInfo info) { }
        public virtual void OnBeKilled(ref DamageInfo info) { }

        public virtual void Serialize(NetworkWriter writer) { }
        public virtual void Deserialize(NetworkReader reader) { }
    }
}
```

## 11. `BuffBehaviourRegistry.cs`

```csharp
using System;
using System.Collections.Generic;

namespace Tower
{
    public static class BuffBehaviourRegistry
    {
        static readonly Dictionary<BuffDefId, Func<BuffBehaviour>> Factories = new()
        {
            // Day3: { BuffDefId.Slow, () => new SlowBuff() },
            // Day2 Debug 见 BuffPipelineDebugTest 里临时注册
        };

        public static void Register(BuffDefId id, Func<BuffBehaviour> factory) =>
            Factories[id] = factory;

        public static BuffBehaviour Create(BuffDefId id) =>
            Factories.TryGetValue(id, out var f) ? f() : null;

        public static BuffBehaviour Create(int id) => Create((BuffDefId)id);
    }
}
```

## 12. `MutationDef.cs` / `MutationRegistry.cs`

```csharp
using Mirror;
using UnityEngine;

namespace Tower
{
    public class MutationDef
    {
        public MutationId Id;
        public string Name;
        public MutationTarget Filter = MutationTarget.All;

        public virtual void CollectModifiers(IStatModifierBuffer buffer) { }
        public virtual void OnDamageDealt(ref DamageInfo info) { }
        public virtual void OnDamageTaken(ref DamageInfo info) { }

        public bool Matches(NetworkIdentity unit)
        {
            if (unit == null) return false;
            return Filter switch
            {
                MutationTarget.All => true,
                MutationTarget.Towers => unit.GetComponent<TowerBase>() != null,
                MutationTarget.Enemies => unit.GetComponent<Enemy>() != null,
                _ => false,
            };
        }
    }

    /// <summary>Day2 自测：全敌移速 -20%</summary>
    public sealed class DebugEnemySlowMutation : MutationDef
    {
        public DebugEnemySlowMutation()
        {
            Id = MutationId.DebugEnemySlow;
            Name = "Debug Enemy Slow";
            Filter = MutationTarget.Enemies;
        }

        public override void CollectModifiers(IStatModifierBuffer buffer) =>
            buffer.AddPercent(AttributeKey.Speed, -0.2f);
    }

    /// <summary>Day2 自测：全塔伤害 +20%</summary>
    public sealed class DebugTowerDamageMutation : MutationDef
    {
        public DebugTowerDamageMutation()
        {
            Id = MutationId.DebugTowerDamage;
            Name = "Debug Tower Damage";
            Filter = MutationTarget.Towers;
        }

        public override void CollectModifiers(IStatModifierBuffer buffer) =>
            buffer.AddPercent(AttributeKey.Damage, 0.2f);
    }
}
```

```csharp
using System.Collections.Generic;

namespace Tower
{
    public static class MutationRegistry
    {
        static readonly Dictionary<MutationId, MutationDef> Map = new()
        {
            { MutationId.DebugEnemySlow, new DebugEnemySlowMutation() },
            { MutationId.DebugTowerDamage, new DebugTowerDamageMutation() },
        };

        public static MutationDef Get(MutationId id) =>
            Map.TryGetValue(id, out var d) ? d : null;

        public static MutationDef Get(int id) => Get((MutationId)id);

        public static void Register(MutationDef def) => Map[def.Id] = def;
    }
}
```

## 13. `ICombatEntity.cs`

```csharp
namespace Tower
{
    /// <summary>可受伤单位：死亡时由 DamageService 回调。</summary>
    public interface ICombatEntity
    {
        void OnFatalHit(ref DamageInfo info);
    }
}
```

## 14. ~~`HealthResource.cs`~~（已取消）

不再单独挂组件。生命并入 `AttributeComponent`（见下一节 `ResourceAttribute` + `syncedCurrentHp`）。

## 15. `AttributeComponent.cs`

> 容器 + Recalculate + Stats SyncList + **CurrentHp SyncVar**。  
> Prefab 只挂这一个属性组件；订阅 Stats 用 `Get(key).OnValueChanged`，订阅血量用 `Hp.OnValueChanged`。

```csharp
using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace Tower
{
    public class AttributeComponent : NetworkBehaviour
    {
        [Tooltip("Prefab 初始 Stats（Base）。与 SyncList 共用 AttributeEntry 结构。")]
        [SerializeField] AttributeEntry[] initialAttributes;

        public readonly SyncList<AttributeEntry> syncedAttributes = new();

        [SyncVar(hook = nameof(OnSyncedHpHook))]
        float syncedCurrentHp;

        readonly Dictionary<AttributeKey, FloatAttribute> attributes = new();
        readonly StatModifierBuffer modifierBuffer = new(); // 黑板
        readonly ResourceAttribute hp = new();

        BuffHolder buffHolder;

        public ResourceAttribute Hp => hp;
        public bool IsAlive => hp.IsAlive;
        public float CurrentHp => hp.Current;

        void Awake()
        {
            buffHolder = GetComponent<BuffHolder>();

            foreach (var e in initialAttributes)
            {
                var attr = new FloatAttribute(e.key);
                attr.InitBase(e.value);
                attributes[e.key] = attr;
            }
        }

        public override void OnStartServer()
        {
            Recalculate();
            foreach (var kv in attributes)
                WriteToSyncList(kv.Key, kv.Value.Final);
        }

        public override void OnStartClient()
        {
            syncedAttributes.OnAdd += OnSyncAdd;
            syncedAttributes.OnSet += OnSyncSet;

            for (int i = 0; i < syncedAttributes.Count; i++)
                ApplySyncedEntry(syncedAttributes[i]);

            // 中途加入：用当前 SyncVar 初始化本地 Hp 逻辑对象
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
            GetOrCreate(entry.key).SetFinal(entry.value);
        }

        void OnSyncedHpHook(float oldVal, float newVal)
        {
            hp.SetCurrent(newVal);
        }

        public FloatAttribute Get(AttributeKey key) =>
            attributes.TryGetValue(key, out var a) ? a : null;

        public float GetBase(AttributeKey key) => Get(key)?.Base ?? 0f;
        public float GetFinal(AttributeKey key) => Get(key)?.Final ?? 0f;

        public float GetMaxHp()
        {
            float max = GetFinal(AttributeKey.MaxHp);
            return max > 0f ? max : 1f;
        }

        FloatAttribute GetOrCreate(AttributeKey key)
        {
            if (attributes.TryGetValue(key, out var a)) return a;
            a = new FloatAttribute(key);
            a.InitBase(0f);
            attributes[key] = a;
            return a;
        }

        [Server]
        public void SetBase(AttributeKey key, float value)
        {
            GetOrCreate(key).SetBase(value);
            Recalculate();
        }

        /// <summary>开战/生成时：当前生命 = MaxHp.Final</summary>
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
            hp.SetCurrent(next);
        }

        [Server]
        public void ClampHpToMax()
        {
            float max = GetMaxHp();
            if (syncedCurrentHp > max)
            {
                syncedCurrentHp = max;
                hp.SetCurrent(max);
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
                attr.SetFinal(final);
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
```

## 16. `BuffHolder.cs`

```csharp
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace Tower
{
    public class BuffHolder : NetworkBehaviour
    {
        public readonly SyncList<BuffSnapshot> buffSnapshots = new();

        readonly List<BuffBehaviour> serverBuffs = new();
        readonly List<BuffBehaviour> clientBuffs = new();

        AttributeComponent attrs;

        void Awake()
        {
            attrs = GetComponent<AttributeComponent>();
        }

        [Server]
        public void AddBuff(BuffBehaviour buff, float? duration = null)
        {
            if (buff == null) return;

            for (int i = 0; i < serverBuffs.Count; i++)
            {
                if (serverBuffs[i].DefId != buff.DefId) continue;

                // MVP：同 DefId 刷新时长
                var existing = serverBuffs[i];
                existing.startTimeOnServer = (float)NetworkTime.time;
                existing.duration = duration ?? buff.DefaultDuration;
                existing.stackCount = buff.stackCount;
                SyncSnapshotAt(i, existing);
                attrs?.Recalculate();
                return;
            }

            buff.startTimeOnServer = (float)NetworkTime.time;
            buff.duration = duration ?? buff.DefaultDuration;
            buff.OnApply(this, isServer: true);
            serverBuffs.Add(buff);

            var writer = NetworkWriterPool.Get();
            buff.Serialize(writer);
            byte[] payload = writer.ToArray();
            NetworkWriterPool.Return(writer);

            buffSnapshots.Add(new BuffSnapshot
            {
                defId = (int)buff.DefId,
                startTimeOnServer = buff.startTimeOnServer,
                duration = buff.duration,
                stackCount = buff.stackCount,
                payload = payload,
            });

            attrs?.Recalculate();
        }

        [Server]
        public void RemoveBuff(BuffBehaviour buff)
        {
            int idx = serverBuffs.IndexOf(buff);
            if (idx < 0) return;

            buff.OnRemove(isServer: true);
            serverBuffs.RemoveAt(idx);
            if (idx < buffSnapshots.Count)
                buffSnapshots.RemoveAt(idx);
            attrs?.Recalculate();
        }

        [Server]
        void SyncSnapshotAt(int idx, BuffBehaviour buff)
        {
            var writer = NetworkWriterPool.Get();
            buff.Serialize(writer);
            byte[] payload = writer.ToArray();
            NetworkWriterPool.Return(writer);

            buffSnapshots[idx] = new BuffSnapshot
            {
                defId = (int)buff.DefId,
                startTimeOnServer = buff.startTimeOnServer,
                duration = buff.duration,
                stackCount = buff.stackCount,
                payload = payload,
            };
        }

        void Update()
        {
            if (!isServer) return;

            float dt = Time.deltaTime;
            for (int i = serverBuffs.Count - 1; i >= 0; i--)
            {
                var buff = serverBuffs[i];
                buff.OnTick(dt);
                if (NetworkTime.time >= buff.startTimeOnServer + buff.duration)
                    RemoveBuff(buff);
            }
        }

        public IEnumerable<BuffBehaviour> GetSortedServerBuffs()
        {
            serverBuffs.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            return serverBuffs;
        }

        public override void OnStartClient()
        {
            buffSnapshots.OnAdd += OnClientBuffAdd;
            buffSnapshots.OnRemove += OnClientBuffRemove;
            buffSnapshots.OnSet += OnClientBuffSet;

            for (int i = 0; i < buffSnapshots.Count; i++)
                OnClientBuffAdd(i);
        }

        public override void OnStopClient()
        {
            buffSnapshots.OnAdd -= OnClientBuffAdd;
            buffSnapshots.OnRemove -= OnClientBuffRemove;
            buffSnapshots.OnSet -= OnClientBuffSet;

            foreach (var b in clientBuffs)
                b?.OnRemove(isServer: false);
            clientBuffs.Clear();
        }

        void OnClientBuffAdd(int idx)
        {
            var snap = buffSnapshots[idx];
            var buff = BuffBehaviourRegistry.Create(snap.defId);
            if (buff == null)
            {
                Debug.LogWarning($"[BuffHolder] Unknown BuffDefId={snap.defId}");
                clientBuffs.Add(null);
                return;
            }

            buff.startTimeOnServer = snap.startTimeOnServer;
            buff.duration = snap.duration;
            buff.stackCount = snap.stackCount;
            if (snap.payload != null && snap.payload.Length > 0)
            {
                var reader = NetworkReaderPool.Get(snap.payload);
                buff.Deserialize(reader);
                NetworkReaderPool.Return(reader);
            }

            buff.OnApply(this, isServer: false);
            clientBuffs.Add(buff);
        }

        void OnClientBuffRemove(int idx, BuffSnapshot oldSnap)
        {
            if (idx < 0 || idx >= clientBuffs.Count) return;
            clientBuffs[idx]?.OnRemove(isServer: false);
            clientBuffs.RemoveAt(idx);
        }

        void OnClientBuffSet(int idx, BuffSnapshot oldSnap)
        {
            if (idx < 0 || idx >= clientBuffs.Count) return;
            var buff = clientBuffs[idx];
            if (buff == null) return;
            var snap = buffSnapshots[idx];
            buff.startTimeOnServer = snap.startTimeOnServer;
            buff.duration = snap.duration;
            buff.stackCount = snap.stackCount;
        }
    }
}
```

## 17. `DamageService.cs`

```csharp
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace Tower
{
    public static class DamageService
    {
        [Server]
        public static void Apply(ref DamageInfo info)
        {
            if (info.target == null) return;

            var targetAttrs = info.target.GetComponent<AttributeComponent>();
            if (targetAttrs == null || !targetAttrs.IsAlive) return;

            // 1) Mutations
            var state = GameEntry.State;
            if (state != null)
            {
                var dealt = CollectMutationDefs(state);
                for (int i = 0; i < dealt.Count; i++)
                    dealt[i].OnDamageDealt(ref info);

                if (info.cancelled) return;
            }

            // 2) Source buffs
            if (info.source != null)
            {
                var srcHolder = info.source.GetComponent<BuffHolder>();
                if (srcHolder != null)
                {
                    foreach (var buff in srcHolder.GetSortedServerBuffs())
                        buff.OnDamageDealt(ref info);
                }
            }

            if (info.cancelled) return;

            // 3) Target buffs
            var dstHolder = info.target.GetComponent<BuffHolder>();
            if (dstHolder != null)
            {
                foreach (var buff in dstHolder.GetSortedServerBuffs())
                    buff.OnDamageTaken(ref info);
            }

            if (info.cancelled) return;

            // 4) 预告击杀
            bool wouldKill = !info.IsHeal && info.amount >= targetAttrs.CurrentHp;
            if (wouldKill)
            {
                if (info.source != null)
                {
                    var srcHolder = info.source.GetComponent<BuffHolder>();
                    if (srcHolder != null)
                    {
                        foreach (var buff in srcHolder.GetSortedServerBuffs())
                            buff.OnKill(ref info);
                    }
                }

                if (dstHolder != null)
                {
                    foreach (var buff in dstHolder.GetSortedServerBuffs())
                        buff.OnBeKilled(ref info);
                }
            }

            if (info.cancelled) return;

            // 5) 结算 HP
            float delta = info.IsHeal ? Mathf.Abs(info.amount) : -Mathf.Abs(info.amount);
            if (!info.IsHeal && info.critChance > 0f && Random.value <= info.critChance)
                delta *= 1.8f;

            targetAttrs.ModHp(delta);

            if (targetAttrs.CurrentHp <= 0f)
            {
                info.killed = true;
                var combat = info.target.GetComponent<ICombatEntity>();
                combat?.OnFatalHit(ref info);
            }
        }

        static List<MutationDef> CollectMutationDefs(GameState state)
        {
            var list = new List<MutationDef>();
            for (int i = 0; i < state.activeMutationIds.Count; i++)
            {
                var def = MutationRegistry.Get(state.activeMutationIds[i]);
                if (def != null) list.Add(def);
            }

            list.Sort((a, b) => 0); // Mutation 暂无 Priority 字段；需要时再加
            return list;
        }
    }
}
```

---

# 修改现有文件（完整替换稿）

## 18. `Enemy.cs`（全量替换）

```csharp
using Mirror;
using UnityEngine;
using UnityEngine.AI;

namespace Tower
{
    [RequireComponent(typeof(BuffHolder))]
    [RequireComponent(typeof(AttributeComponent))]
    [RequireComponent(typeof(NavMeshAgent))]
    public class Enemy : NetworkBehaviour, ICombatEntity
    {
        [Header("Visual")]
        [SerializeField] MeshRenderer meshRenderer;
        Color originalColor;

        [Header("Path")]
        [SerializeField] string pathEndTag = "Tower.Path.End";
        [SerializeField] float reachBaseDistance = 1f;

        BuffHolder buffHolder;
        AttributeComponent attribute;
        NavMeshAgent agent;
        Transform pathEnd;
        bool reachedBase;
        bool dead;

        public bool IsAlive => attribute != null && attribute.IsAlive && !dead;

        void Awake()
        {
            buffHolder = GetComponent<BuffHolder>();
            attribute = GetComponent<AttributeComponent>();
            agent = GetComponent<NavMeshAgent>();
            CacheOriginalColor();
        }

        public override void OnStartServer()
        {
            BindSpeedAttribute();
            InitCombatStats();
            SetupPathfinding();
        }

        public override void OnStopServer()
        {
            UnbindSpeedAttribute();
        }

        public override void OnStartClient()
        {
            if (!isServer)
            {
                agent.enabled = false;
                BindSpeedAttribute();
            }
            CacheOriginalColor();
        }

        public override void OnStopClient()
        {
            if (!isServer)
                UnbindSpeedAttribute();
        }

        void Update()
        {
            if (!isServer || dead) return;
            if (HasReachedBase())
                ReachBase();
        }

        void BindSpeedAttribute()
        {
            var speed = attribute.Get(AttributeKey.Speed);
            if (speed == null) return;
            speed.OnValueChanged -= OnSpeedChanged;
            speed.OnValueChanged += OnSpeedChanged;
            ApplySpeed(speed.Final);
        }

        void UnbindSpeedAttribute()
        {
            var speed = attribute?.Get(AttributeKey.Speed);
            if (speed != null)
                speed.OnValueChanged -= OnSpeedChanged;
        }

        void InitCombatStats()
        {
            attribute.Recalculate();
            attribute.InitHpFull();
            ApplySpeed(attribute.GetFinal(AttributeKey.Speed));
        }

        void SetupPathfinding()
        {
            var pathEndGo = GameObject.FindWithTag(pathEndTag);
            if (pathEndGo == null)
            {
                Debug.LogError($"[Server] Path end not found! Tag '{pathEndTag}'.");
                return;
            }

            pathEnd = pathEndGo.transform;
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                agent.Warp(hit.position);
            agent.destination = pathEnd.position;
        }

        bool HasReachedBase() =>
            !reachedBase && pathEnd != null &&
            Vector3.Distance(transform.position, pathEnd.position) < reachBaseDistance;

        void OnSpeedChanged(float value) => ApplySpeed(value);

        void ApplySpeed(float value)
        {
            if (agent != null)
                agent.speed = value;
        }

        void CacheOriginalColor()
        {
            if (meshRenderer != null)
                originalColor = meshRenderer.material.color;
        }

        [Server]
        void ReachBase()
        {
            reachedBase = true;
            dead = true;
            int baseDmg = Mathf.RoundToInt(attribute.GetFinal(AttributeKey.BaseDamage));
            GameEntry.Event.Fire(this, EnemyKilledEventArgs.Create(
                EnemyRemoveReason.ReachedBase,
                gold: 0,
                baseDmg: baseDmg,
                pos: transform.position,
                netId: netId));
            NetworkServer.Destroy(gameObject);
        }

        [Server]
        public void TakeDamage(int dmg)
        {
            var info = new DamageInfo
            {
                source = null,
                target = netIdentity,
                amount = dmg,
                type = DamageType.Physical,
                tags = DamageTag.Direct,
            };
            DamageService.Apply(ref info);
        }

        [Server]
        public void TakeDamage(ref DamageInfo info)
        {
            info.target = netIdentity;
            DamageService.Apply(ref info);
        }

        [Server]
        public void OnFatalHit(ref DamageInfo info)
        {
            if (dead) return;
            dead = true;
            int gold = Mathf.RoundToInt(attribute.GetFinal(AttributeKey.GoldReward));
            GameEntry.Event.Fire(this, EnemyKilledEventArgs.Create(
                EnemyRemoveReason.KilledByPlayer,
                gold: gold,
                baseDmg: 0,
                pos: transform.position,
                netId: netId));
            NetworkServer.Destroy(gameObject);
        }

        public void SetSlowVisual(bool slowed)
        {
            if (meshRenderer == null) return;
            meshRenderer.material.color = slowed ? Color.blue : originalColor;
        }
    }
}
```

## 19. `TowerBase.cs`（全量替换）

```csharp
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace Tower
{
    [RequireComponent(typeof(BuffHolder))]
    [RequireComponent(typeof(AttributeComponent))]
    public class TowerBase : NetworkBehaviour
    {
        [Header("同步变量")]
        [SyncVar] public int ownerPlayerId = -1;
        [SyncVar] public int level = 1;

        [Header("引用")]
        public Transform firePoint;
        public GameObject projectilePrefab;

        [SerializeField] List<Enemy> enemiesInRange = new();
        [SerializeField] Enemy currentTarget;
        [SerializeField] float attackTimer;

        AttributeComponent attribute;

        void Awake() => attribute = GetComponent<AttributeComponent>();

        public override void OnStartServer()
        {
            attribute.Recalculate();
            attribute.InitHpFull();
        }

        void OnTriggerEnter(Collider c)
        {
            if (!isServer) return;
            if (c.TryGetComponent<Enemy>(out var e) && !enemiesInRange.Contains(e))
                enemiesInRange.Add(e);
        }

        void OnTriggerExit(Collider c)
        {
            if (!isServer) return;
            if (c.TryGetComponent<Enemy>(out var e))
                enemiesInRange.Remove(e);
        }

        void Update()
        {
            if (!isServer) return;

            enemiesInRange.RemoveAll(e => e == null || !e.IsAlive);

            if (currentTarget == null || !currentTarget.IsAlive || !enemiesInRange.Contains(currentTarget))
                currentTarget = enemiesInRange.Count > 0 ? enemiesInRange[0] : null;

            if (currentTarget == null) return;

            attackTimer += Time.deltaTime;
            float interval = attribute.GetFinal(AttributeKey.AttackInterval);
            if (interval <= 0f) interval = 1f;
            if (attackTimer >= interval)
            {
                attackTimer = 0f;
                Fire(currentTarget);
            }
        }

        [Server]
        void Fire(Enemy target)
        {
            var go = Instantiate(projectilePrefab, firePoint.position, Quaternion.identity);
            var proj = go.GetComponent<ProjectileBase>();
            if (proj == null)
            {
                Debug.LogError($"[Server] {projectilePrefab.name} missing ProjectileBase.");
                Destroy(go);
                return;
            }

            int damage = Mathf.RoundToInt(attribute.GetFinal(AttributeKey.Damage));
            float projSpeed = attribute.GetFinal(AttributeKey.ProjectileSpeed);
            proj.ServerLaunch(target, damage, projSpeed, firePoint.position, netIdentity);
            NetworkServer.Spawn(go);
        }
    }
}
```

## 20. `HomeBase.cs`（全量替换）

```csharp
using GameFramework.Event;
using Mirror;
using UnityEngine;

namespace Tower
{
    [RequireComponent(typeof(AttributeComponent))]
    public class HomeBase : NetworkBehaviour, ICombatEntity
    {
        [SerializeField] int damagePerEnemy = 1;

        AttributeComponent attribute;
        bool defeated;

        void Awake() => attribute = GetComponent<AttributeComponent>();

        public override void OnStartServer()
        {
            attribute.Recalculate();
            attribute.InitHpFull();
            attribute.Hp.OnValueChanged += OnHpChanged;
            GameEntry.Event.Subscribe(EnemyKilledEventArgs.EventId, OnEnemyKilled);
            FireHpEvent(attribute.CurrentHp);
        }

        public override void OnStopServer()
        {
            if (attribute != null)
                attribute.Hp.OnValueChanged -= OnHpChanged;
            GameEntry.Event.Unsubscribe(EnemyKilledEventArgs.EventId, OnEnemyKilled);
        }

        public override void OnStartClient()
        {
            attribute.Hp.OnValueChanged += OnHpChanged;
            FireHpEvent(attribute.CurrentHp);
        }

        public override void OnStopClient()
        {
            if (attribute != null)
                attribute.Hp.OnValueChanged -= OnHpChanged;
        }

        void OnHpChanged(float current) => FireHpEvent(current);

        void FireHpEvent(float current)
        {
            int cur = Mathf.RoundToInt(current);
            int max = Mathf.RoundToInt(attribute.GetMaxHp());
            GameEntry.Event.Fire(this, HomeBaseHpChangedEventArgs.Create(cur, max));
        }

        void OnEnemyKilled(object sender, GameEventArgs e)
        {
            if (!isServer) return;
            if (e is EnemyKilledEventArgs args && args.Reason == EnemyRemoveReason.ReachedBase)
            {
                int dmg = args.BaseDamage > 0 ? args.BaseDamage : damagePerEnemy;
                var info = new DamageInfo
                {
                    source = null,
                    target = netIdentity,
                    amount = dmg,
                    type = DamageType.Physical,
                    tags = DamageTag.Direct,
                };
                DamageService.Apply(ref info);
            }
        }

        [Server]
        public void OnFatalHit(ref DamageInfo info)
        {
            if (defeated) return;
            defeated = true;
            GameEntry.State?.NotifyDefeat();
        }
    }
}
```

## 21. `ProjectileBase.cs`（改 ServerLaunch）

在类里增加字段，并改 `ServerLaunch` 签名（**所有调用点已在 TowerBase 更新**）：

```csharp
// 新增字段
[HideInInspector] public NetworkIdentity sourceNetIdentity;

[Server]
public void ServerLaunch(Enemy target, int launchDamage, float launchSpeed, Vector3 launchPos, NetworkIdentity source)
{
    damage = launchDamage;
    speed = launchSpeed;
    startPos = launchPos;
    sourceNetIdentity = source;
    OnServerLaunch(target);
}
```

其余 `ProjectileBase` 代码保持不动。

## 22. `HomingProjectile.CheckServerHit` + `TryGetTargetPos` 里 `hp` 判断

把所有 `serverTarget.hp > 0` 改成 `serverTarget.IsAlive`，命中改为：

```csharp
protected override bool CheckServerHit()
{
    if (serverTarget == null) return false;
    if (Vector3.Distance(transform.position, serverTarget.transform.position) >= HitDistance)
        return false;

    if (serverTarget.IsAlive)
    {
        var info = new DamageInfo
        {
            source = sourceNetIdentity,
            target = serverTarget.netIdentity,
            amount = damage,
            type = DamageType.Physical,
            tags = DamageTag.Direct,
            critChance = 0.05f,
        };
        serverTarget.TakeDamage(ref info);
    }
    return true;
}
```

`TryGetTargetPos` 服务端分支：

```csharp
if (serverTarget != null && serverTarget.IsAlive)
```

## 23. `FrostProjectile.CheckServerHit`（删 ApplySlow）

```csharp
protected override bool CheckServerHit()
{
    if (serverTarget == null) return false;
    if (Vector3.Distance(transform.position, serverTarget.transform.position) >= HitDistance)
        return false;

    var hits = Physics.OverlapSphere(transform.position, aoeRadius, ~0);
    foreach (var h in hits)
    {
        if (!h.TryGetComponent<Enemy>(out var enemy) || !enemy.IsAlive) continue;

        var info = new DamageInfo
        {
            source = sourceNetIdentity,
            target = enemy.netIdentity,
            amount = damage,
            type = DamageType.Physical,
            tags = DamageTag.Direct,
            critChance = 0.05f,
        };
        enemy.TakeDamage(ref info);
        // Day2：不调用 ApplySlow。Day3：AddBuff(new SlowBuff())
    }
    return true;
}
```

同步把 `TryGetTargetPos` 里 `hp > 0` 改成 `IsAlive`。  
`slowMultiplier` / `slowDuration` 字段可先留着给 Day3 用。

## 24. `GamingForm.RefreshHpFromScene`

```csharp
void RefreshHpFromScene()
{
    var homeBase = FindObjectOfType<HomeBase>();
    if (homeBase == null) return;

    var attrs = homeBase.GetComponent<AttributeComponent>();
    if (attrs == null) return;

    RefreshHp(
        Mathf.RoundToInt(attrs.CurrentHp),
        Mathf.RoundToInt(attrs.GetMaxHp()));
}
```

## 25. `GameState.cs` 增量

在字段区增加：

```csharp
/// <summary>本局已获得的全局 Mutation（只同步 ID）。</summary>
public readonly SyncList<int> activeMutationIds = new();
```

在类末尾增加：

```csharp
[Server]
public void AddMutation(MutationId id)
{
    int v = (int)id;
    for (int i = 0; i < activeMutationIds.Count; i++)
        if (activeMutationIds[i] == v) return;

    activeMutationIds.Add(v);
    RecalculateAllCombatAttributes();
    Debug.Log($"[Server] Mutation added: {id}");
}

[Server]
public void RecalculateAllCombatAttributes()
{
    var all = FindObjectsOfType<AttributeComponent>();
    for (int i = 0; i < all.Length; i++)
        all[i].Recalculate();
}
```

---

# Debug（测完删除）

## 26. `BuffPipelineDebugTest.cs`

挂到场景里任意常驻物体（或 GameNetworkManager 同物体）上。

```csharp
using Mirror;
using UnityEngine;

namespace Tower
{
    public class BuffPipelineDebugTest : NetworkBehaviour
    {
        [SerializeField] KeyCode buffKey = KeyCode.F1;
        [SerializeField] KeyCode printKey = KeyCode.F2;
        [SerializeField] KeyCode mutSlowKey = KeyCode.F3;
        [SerializeField] KeyCode mutDmgKey = KeyCode.F4;

        void Awake()
        {
            BuffBehaviourRegistry.Register(BuffDefId.None, () => new TestSpeedBuff());
        }

        void Update()
        {
            if (!isServer) return;

            if (Input.GetKeyDown(buffKey))
            {
                var enemy = FindObjectOfType<Enemy>();
                if (enemy == null) { Debug.Log("[Test] no enemy"); return; }
                enemy.GetComponent<BuffHolder>().AddBuff(new TestSpeedBuff(), 5f);
                Debug.Log($"[Test] Add TestSpeedBuff -> {enemy.netId}");
            }

            if (Input.GetKeyDown(printKey))
            {
                var enemy = FindObjectOfType<Enemy>();
                if (enemy == null) return;
                var a = enemy.GetComponent<AttributeComponent>();
                Debug.Log($"[Test] Speed base={a.Get(AttributeKey.Speed)?.Base} final={a.Get(AttributeKey.Speed)?.Final} hp={a.CurrentHp}/{a.GetMaxHp()}");
            }

            if (Input.GetKeyDown(mutSlowKey))
                GameEntry.State?.AddMutation(MutationId.DebugEnemySlow);

            if (Input.GetKeyDown(mutDmgKey))
                GameEntry.State?.AddMutation(MutationId.DebugTowerDamage);
        }
    }

    public class TestSpeedBuff : BuffBehaviour
    {
        public override BuffDefId DefId => BuffDefId.None;
        public float percent = -0.5f;

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

        public override void Serialize(NetworkWriter writer) => writer.WriteFloat(percent);
        public override void Deserialize(NetworkReader reader) => percent = reader.ReadFloat();
    }
}
```

---

# Prefab 配置

| 对象 | 组件 | initialAttributes |
|------|------|-------------------|
| Enemy | Attribute + BuffHolder | MaxHp=100, Speed=3, BaseDamage=1, GoldReward=10 |
| Homing 塔 Prefab | Attribute + BuffHolder | MaxHp=100, AttackInterval=1, Damage=25, ProjectileSpeed=20 |
| Frost 塔 Prefab | Attribute + BuffHolder | 同上（可按需改 Damage） |
| HomeBase | Attribute（BuffHolder 可选） | MaxHp=10 |
| GamePlayer | **不挂** | — |
| GameState | 代码字段即可，**不挂** BuffHolder | — |

操作：打开 Prefab → Add Component → 填 `AttributeComponent.initialAttributes` → Apply。  
**不要**再挂 HealthResource。CurrentHp 由 `InitHpFull()` 写入 AttributeComponent 的 SyncVar。

**NetworkIdentity**：Enemy/塔/HomeBase 原本就有；新加的 NetworkBehaviour 挂在同一物体上即可。

---

# Day3 SlowBuff 预览（Day2 不实现）

```csharp
public class SlowBuff : BuffBehaviour
{
    public override BuffDefId DefId => BuffDefId.Slow;
    public float percent = -0.4f;

    public override void CollectModifiers(IStatModifierBuffer buffer) =>
        buffer.AddPercent(AttributeKey.Speed, percent);

    public override void OnApply(BuffHolder holder, bool isServer)
    {
        base.OnApply(holder, isServer);
        if (!isServer) holder.GetComponent<Enemy>()?.SetSlowVisual(true);
    }

    public override void OnRemove(bool isServer)
    {
        if (!isServer) holder.GetComponent<Enemy>()?.SetSlowVisual(false);
    }

    public override void Serialize(NetworkWriter w) => w.WriteFloat(percent);
    public override void Deserialize(NetworkReader r) => percent = r.ReadFloat();
}
```

Frost 命中：`enemy.GetComponent<BuffHolder>().AddBuff(new SlowBuff { percent = slowMultiplier - 1f }, slowDuration);`  
（若 `slowMultiplier=0.6`，则 percent=`-0.4`）

---

# Checklist

### 新建

- [ ] Effect 目录下全部文件 + Attribute 目录下全部文件（含 **FloatAttribute.cs**）
- [ ] MirrorEffectSerializers（漏了 SyncList 会序列化失败）
- [ ] DamageService / MutationRegistry（含两个 Debug Mutation）
- [ ] 订阅示例：Enemy 用 `Get(Speed).OnValueChanged`，无组件级 OnValueChanged

### 改造

- [ ] Enemy / TowerBase / HomeBase 全量替换
- [ ] ProjectileBase 签名 + Homing/Frost 命中
- [ ] GamingForm.RefreshHpFromScene
- [ ] GameState.activeMutationIds + AddMutation

### Prefab

- [ ] Enemy / 塔 / HomeBase 挂组件并配 initialAttributes
- [ ] 场景挂 BuffPipelineDebugTest（可选）

### 验收

- [ ] Host 开局 → Wave → 敌人行走速度正确
- [ ] Homing 命中扣血、死亡加金币
- [ ] 撞基地扣 HomeBase，UI 更新，扣光 Defeat
- [ ] F1 TestBuff 减速可见，5 秒恢复
- [ ] F3/F4 Mutation 全局减速/加伤
- [ ] Frost 只有 AoE 伤、无减速（预期）
- [ ] 测完删 Debug；commit

---

# 关键提醒

1. **先 Day1 或先 Day2**：本文按**当前仓库名**写。若先合 Day1，把 `TowerBase`→`TowerUnit`、`EnemyKilled*`→`EnemyRemoved*` 再抄。
2. **HP 在 AttributeComponent 上（SyncVar + ResourceAttribute）**，不是单独组件；不要把 CurrentHp 配进 initialAttributes。
3. **AttributeEntry 只有一份**：Inspector Base 与 SyncList Final 共用。
4. **StatModifierBuffer = 黑板**；Buff 只往黑板写贡献。
5. **订阅**：Stats 用 `Get(key).OnValueChanged`；血量用 `Hp.OnValueChanged`。
6. **EffectPriority** = Buff.Priority 的档位名。
7. **Host**：Attribute SyncList 客户端回调 `if (isServer) return`；Buff 表现走 clientBuffs。
8. **Weaver**：加完 `MirrorEffectSerializers` 后编译一次。
9. **RequireComponent** 加完后 Apply Prefab。
10. **DamageTag.Reflect**：做反伤时必打标。

---

## 相关文档

- `w2-overview.md`：周计划（Mutation 以本文 MutationList 为准）
- `active.md`：会话备忘
