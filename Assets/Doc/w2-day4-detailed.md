# W2-Day4 — Roguelike 投票系统 + Mutation 管线激活

> 日期：2026-07-16
> 目标工程：`F:\Download\Tower2022-main (2)\Tower2022-main`（Day1/Day2/Day3 均已合入，含 Day3 Phase2 池化）
> 前置：Effect Pipeline（`AttributeComponent` / `DamageService` / `MutationDef` / `MutationRegistry` / `GameState.activeMutationIds`）已就位
> **本文是 Day4 唯一依据**：打开即可按文件抄；抄完对照 Checklist 验收
> **只写文档、不改工程**：本文是实现规范，代码由你按文档落地

---

## 现状：Mutation 管线已接好线，但空转

Day2 已经把「Mutation 影响属性 / 影响伤害」两条路都接进了 Pipeline，只差「内容 + 触发」：

- 属性路：`AttributeComponent.CollectModifiersOntoBlackboard` 已遍历 `activeMutationIds` → `MutationRegistry.Get` → `Matches(netIdentity)` → `CollectModifiers`（`AttributeComponent.cs:198-203`）
- 伤害路：`DamageService.Apply` 已遍历 `activeMutationIds`，对每个 Mutation **无差别**调 `OnDamageDealt`、并在 `Matches(info.target)` 后调 `OnDamageTaken`（`DamageService.cs:23-31`）。**Day4 修正**：`OnDamageDealt` 也改为 `Matches(info.source)` 门控后再调（与 `OnDamageTaken` 对称，见 §3.3）
- 落地入口：`GameState.AddMutation(MutationId)` 加 ID + `RecalculateAllCombatAttributes()`（`GameState.cs:104-114`）

**空转原因**：`MutationId` 只有 `None`（`EffectEnums.cs:11-14`）、`MutationRegistry.Map` 从未 `Register`、`AddMutation` 从未被调用。Day4 就是把这三处补上，再加一个玩家投票阶段来驱动 `AddMutation`。

> **实现路线说明**：早期 `active.md` 曾设想「Mutation = 挂 GameState 的通用 BuffHolder」，但**实际代码走的是 `MutationDef + MutationRegistry + activeMutationIds`**。本文以现有代码为准，不引入 BuffHolder 版 Mutation。

---

## Day4 目标 / 完成标志 / 不做

**目标**

- 新增独立阶段 `GamePhase.Voting`，插在「某波清空 → 下一波 PreWave」之间。
- **投票触发不写死「每 3 波」**，改为在 `WaveConfig` 每波加一个开关 `voteAfterWave`，由策划配置哪些波后投票。
- 玩家 3 选 1 投票（30s 倒计时 / 全员投完提前结束 / 超时取最高 / 平票取最小下标）。
- 计票胜出 → `GameState.AddMutation(winner)` → 全场塔/敌人即时重算。
- 实装 **3 个正式 Mutation**，覆盖 Pipeline 两种机制（属性式 ×2 + 伤害钩子式 ×1）。

**完成标志**

- 配置了 `voteAfterWave` 的波清空后进入投票 UI，3 个选项 + 倒计时 + 实时票数。
- 任一玩家投票即时刷新票数；全员投完立刻结算；30s 到自动结算。
- 胜出 Mutation 立刻生效：塔伤害/敌人速度肉眼可见变化；后续新刷的敌人也带上 Mutation。
- 双端一致（Host / 纯客户端）；投票中途加入的客户端看到选项、当前票数、剩余时间正确。
- 与 Day3 `SlowBuff` 叠加正确（如「敌人减速」Mutation + Frost 减速百分比相加）。

**明确不做**

- Mutation 图标/品质/稀有度、投票动画特效。
- 塔合成、每局随机解锁塔池（属于内容填充，后续）。
- Mutation 持久化存档（跨局，W3+）。
- `AttributeKey.Range` 类「改检测半径」的 Mutation（见下方「边界与坑」#6，Range 属性当前未被 Trigger 消费）。

---

## 设计决策速览（已敲定）

| 项 | 决策 |
|---|---|
| 投票阶段形态 | 新增独立 `GamePhase.Voting`（语义干净） |
| 触发时机 | `WaveConfig.WaveEntry.voteAfterWave` 开关，逐波配置（**非**写死每 3 波） |
| 计票规则 | 30s 倒计时；全员投完提前结束；超时取当前最高；平票/零票取**最小选项下标** |
| Day4 范围 | 一次到位：投票机制 + 3 个正式 Mutation |
| 杂项（清理/炮塔转向） | 见附录 A / B |

---

## 数据流总览

```text
[Server] WaveDirector.TickWave: 本波清空
   │ IsLastWave? → Victory
   │ 否，且 该波 voteAfterWave==true 且 有可选 Mutation
   ▼
[Server] 进入 Voting：
   voteOptions(SyncList)= 随机挑 3 个未拥有的 MutationId
   playerVotes(SyncDict).Clear()；voteTimer(SyncVar)=30
   gameState.phase = Voting
   │
   ├─[Client] GamePhaseChanged=Voting → 打开 VotingPanel
   │           读 voteOptions 建 3 个按钮（名字查 MutationRegistry.Get(id).Name）
   │           倒计时读 voteTimer；票数订阅 playerVotes.OnChange
   │
   └─[Client] 点选项 → localPlayer.CmdVote(index)
              [Server] playerVotes[playerId]=index（覆盖=改票）
   ▼
[Server] TickVoting: voteTimer≤0 或 全员已投 → Tally()
   winner = 最高票（平票/零票取最小下标）
   gameState.AddMutation(voteOptions[winner])   ← 激活管线
       └ activeMutationIds.Add + RecalculateAllCombatAttributes()
          └ 每个 AttributeComponent.Recalculate 收集 Mutation 修正 → 写 SyncList<AttributeEntry>
   voteOptions.Clear()；playerVotes.Clear()
   AdvanceToNextPreWave()（currentWave++ → PreWave）
```

---

## 文件改动清单

**新建**

```text
Assets/GameMain/Scripts/Gameplay/Effect/
  Mutations/TowerDamageUpMutation.cs
  Mutations/EnemySlowMutation.cs
  Mutations/TowerCritUpMutation.cs
  MutationCatalog.cs                 — 注册表引导 + 全体 ID 列表
Assets/GameMain/Scripts/UI/UIItems/
  VotingPanelUI.cs                   — 投票面板（仿 StatusPanelUI 模式）
```

**修改**

```text
EffectEnums.cs        — MutationId 加 3 个真实值
MutationDef.cs        — 加 Description 字段（UI 展示用）
DamageService.cs      — OnDamageDealt 改为 Matches(info.source) 后再调（与 OnDamageTaken 对称，见 §3.3）
WaveConfig.cs         — WaveEntry 加 voteAfterWave 开关
GameState.cs          — 枚举加 Voting；加 voteOptions/playerVotes/voteTimer
GamePlayer.cs         — 加 CmdVote
WaveDirector.cs       — 插入 Voting 阶段 + 计票 + 推进重构
GameEntry.Custom.cs   — 启动时 MutationCatalog.EnsureRegistered()（双端）
StatusPanelUI.cs      — 加 Voting 分支（隐藏中央提示，交给投票面板）
GamingForm.cs         — 管理 VotingPanelUI 的 Init/Clear（仿 StatusPanelUI）
EnumUIForm.cs         — （若走独立 UIForm 方案才需要，见 §8）
```

---

## 1. `EffectEnums.cs` — MutationId 加真实值

```csharp
public enum MutationId
{
    None = 0,
    TowerDamageUp = 2001,  // 属性式：塔伤害 +25%
    EnemySlow     = 2002,  // 属性式：敌人移速 -20%
    TowerCritUp   = 2003,  // 伤害钩子式：暴击率 +10%
}
```

---

## 2. `MutationDef.cs` — 加 Description（供 UI）

在现有 `MutationDef`（`Id` / `Name` / `Filter` / `CollectModifiers` / `OnDamageDealt` / `OnDamageTaken` / `Matches`）基础上加一个字段：

```csharp
public string Description;   // 投票面板展示文案
```

> `Matches(NetworkIdentity)` 已实现 `Towers`/`Enemies`/`All` 过滤（`MutationDef.cs:16-26`），子类只填数据。

---

## 3. 三个正式 Mutation

### 3.1 属性式 · 塔伤害 +25%（`TowerDamageUpMutation.cs`）

```csharp
namespace Tower
{
    public class TowerDamageUpMutation : MutationDef
    {
        public TowerDamageUpMutation()
        {
            Id = MutationId.TowerDamageUp;
            Name = "火力强化";
            Description = "所有防御塔伤害 +25%";
            Filter = MutationTarget.Towers;   // 只对塔的 AttributeComponent 生效
        }

        public override void CollectModifiers(IStatModifierBuffer buffer)
            => buffer.AddPercent(AttributeKey.Damage, 0.25f);
    }
}
```

工作原理：`AttributeComponent.CollectModifiersOntoBlackboard` 只在 `def.Matches(netIdentity)` 时调 `CollectModifiers`（`AttributeComponent.cs:201`），`Filter=Towers` 保证只有挂 `TowerUnit` 的对象吃到；`Damage.Final = (Base+Flat)*(1+Percent)` 天然叠加其它加成。

### 3.2 属性式 · 敌人移速 -20%（`EnemySlowMutation.cs`）

```csharp
namespace Tower
{
    public class EnemySlowMutation : MutationDef
    {
        public EnemySlowMutation()
        {
            Id = MutationId.EnemySlow;
            Name = "迟缓领域";
            Description = "所有敌人移速 -20%";
            Filter = MutationTarget.Enemies;
        }

        public override void CollectModifiers(IStatModifierBuffer buffer)
            => buffer.AddPercent(AttributeKey.Speed, -0.20f);
    }
}
```

> 与 Day3 `SlowBuff` 叠加天然正确：两者都往 `Speed` 黑板 `AddPercent`，百分比相加（`-0.2 + -0.4 = -0.6`）。

### 3.3 伤害钩子式 · 暴击率 +10%（`TowerCritUpMutation.cs`）

```csharp
namespace Tower
{
    public class TowerCritUpMutation : MutationDef
    {
        public TowerCritUpMutation()
        {
            Id = MutationId.TowerCritUp;
            Name = "精准打击";
            Description = "防御塔暴击率 +10%";
            Filter = MutationTarget.Towers;   // 真过滤：只有“施加者是塔”的伤害才 +暴击（见下）
        }

        public override void OnDamageDealt(ref DamageInfo info)
            => info.critChance += 0.10f;
    }
}
```

关键顺序：`DamageService.Apply` 先跑 Mutation 的 `OnDamageDealt`，**之后**才 roll 暴击（`DamageService.cs:55-59`）。所以这里 `info.critChance += 0.1` 会真正提高暴击概率。

### 3.3.1 Day4 修正 · `OnDamageDealt` 按 `info.source` 门控（对称化）

现状 `DamageService.Apply` 对 `OnDamageDealt` **无差别调用**（`DamageService.cs:28` 不看 `Matches`），只有 `OnDamageTaken` 用 `Matches(info.target)` 门控。这不对称、也不利于扩展。Day4 把两者对称起来：

```csharp
for (int i = 0; i < ids.Count; i++)
{
    var mut = MutationRegistry.Get(ids[i]);
    if (mut == null) continue;

    // 施加方（source）匹配 → 才跑“打出伤害时”的钩子
    if (mut.Matches(info.source)) mut.OnDamageDealt(ref info);
    // 受击方（target）匹配 → 才跑“承受伤害时”的钩子
    if (mut.Matches(info.target)) mut.OnDamageTaken(ref info);
}
```

**为什么是 `source` 不是 `target`**：`OnDamageTaken` 描述「谁挨打」，故看 `info.target`；`OnDamageDealt` 描述「谁出手」，故必须看 `info.source`。二者对称、各看伤害的一端，`MutationTarget.Towers/Enemies` 才能对「攻方 / 受方」分别精确生效。

**对本作 3 个 Mutation 的影响**：只有 `TowerCritUp` 用 `OnDamageDealt`。命中链路里 `info.source` 恒为发射塔的 `NetworkIdentity`（`HomingProjectile.cs:22` / `FrostProjectile.cs:28` 都设了 `source = sourceNetIdentity`，源头是 `TowerUnit.Fire` 传入的 `netIdentity`），`Matches(Towers)` 对它返回 `true` → 暴击照常生效，**无回归**。区别是 `Filter=Towers` 从「摆设」变成了真过滤，为「敌人反伤 / 敌人 DoT / 环境伤害」等未来来源提前隔离好。

> **边界（`info.source == null`）**：撞家伤害、`Enemy.TakeDamage(int)`（已是死代码）等无源伤害，`Matches(null)` 直接返回 `false`（`MutationDef.cs:18`），因此**任何** `OnDamageDealt` 都不会触发——包括 `Filter=All` 的。这符合「没有施加者就谈不上施加者匹配」的直觉；若将来确实需要「无源也算 All」的全局 `OnDamageDealt`，再在门控处对 `Filter==All && source==null` 特判即可，本次不做。

---

## 4. `MutationCatalog.cs` — 注册引导 + 全体 ID 列表

```csharp
using System.Collections.Generic;

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
```

调用时机：在 `GameEntry.Custom.cs` 的自定义组件初始化里调 `MutationCatalog.EnsureRegistered()`。`GameEntry` 每进程只跑一次、且早于联网，天然满足「双端预设一致」。

> **为什么客户端也要注册**：服务端用注册表跑属性/伤害逻辑；**客户端用它把 `MutationId` 翻成中文名/描述**显示在投票面板（`MutationRegistry.Get(id).Name/Description`）。漏注册客户端 → 投票按钮显示不出名字。

---

## 5. `WaveConfig.cs` — 每波加投票开关

在 `WaveEntry` 加一个字段：

```csharp
[LabelText("清空后触发投票")]
public bool voteAfterWave;
```

策划在 `WaveConfig` 资产里勾选：哪几波清完后弹投票（例如第 3、6、9 波）。**最后一波（Boss）即使勾了也不投**（由 Director 的 `IsLastWave` 先行拦截）。

---

## 6. `GameState.cs` — Voting 阶段数据

### 6.1 枚举加 Voting

```csharp
public enum GamePhase
{
    Preparing,
    PreWave,
    Wave,
    Voting,     // 新增：波间投票
    Victory,
    Defeat,
}
```

### 6.2 同步字段

```csharp
/// <summary>本轮投票的 3 个候选 MutationId（服务端随机挑，双端展示）。</summary>
public readonly SyncList<int> voteOptions = new();

/// <summary>playerId → 所投选项下标（0..voteOptions.Count-1）；覆盖即改票。</summary>
public readonly SyncDictionary<int, int> playerVotes = new();

/// <summary>投票剩余秒数（服务端递减，UI 直接读，仿 preWaveTimer）。</summary>
[SyncVar] public float voteTimer;
```

> 计票与选项生成逻辑放在 `WaveDirector`（服务端）里，`GameState` 只承载同步数据。`AddMutation` / `RecalculateAllCombatAttributes` 已在 `GameState`，直接复用。

---

## 7. `GamePlayer.cs` — CmdVote

仿现有 `CmdSetReady`（`GamePlayer.cs:41-47`）写一个投票命令：

```csharp
[Command]
public void CmdVote(int optionIndex)
{
    var state = GameEntry.State;
    if (state == null || state.phase != GamePhase.Voting) return;
    if (optionIndex < 0 || optionIndex >= state.voteOptions.Count) return;

    state.playerVotes[playerId] = optionIndex;   // 覆盖=改票，SyncDict 自动同步
}
```

`playerId` 已是 `[SyncVar]`（`GamePlayer.cs:11`，服务端 = netId）。服务端 `GamePlayer` 写 `GameState.playerVotes` 属于同进程服务端调用，合法。

---

## 8. `WaveDirector.cs` — 插入 Voting + 计票 + 推进重构

现有 `TickWave` 在本波清空后直接 `currentWave++` 进下一波 PreWave（`WaveDirector.cs:98-114`）。改成：清空后先判是否投票，投票结算完再推进。

```csharp
const float VoteDuration = 30f;

void TickPhase(float dt)
{
    switch (gameState.phase)
    {
        case GamePhase.Preparing: TickPreparing(); break;
        case GamePhase.PreWave:   TickPreWave(dt);  break;
        case GamePhase.Wave:      TickWave();       break;
        case GamePhase.Voting:    TickVoting(dt);   break;   // 新增
    }
}

void TickWave()
{
    if (m_WaveManager == null) return;

    m_WaveManager.ServerTick();
    if (!m_WaveManager.IsCurrentWaveCleared) return;

    if (m_WaveManager.IsLastWave)
    {
        TransitionTo(GamePhase.Victory);
        return;
    }

    // 该波清空：先看要不要投票（最后一波已在上面拦掉）
    if (ShouldVoteAfter(gameState.currentWave) && TryStartVote())
    {
        TransitionTo(GamePhase.Voting);
        return;
    }

    AdvanceToNextPreWave();
}

bool ShouldVoteAfter(int waveNumber)   // waveNumber 1-based
{
    var config = m_WaveManager?.config;
    int idx = waveNumber - 1;
    return config != null && idx >= 0 && idx < config.waves.Length
        && config.waves[idx].voteAfterWave;
}

bool TryStartVote()
{
    // 从未拥有的 Mutation 里随机挑最多 3 个
    var pool = new List<int>();
    foreach (var id in MutationCatalog.All)
    {
        int v = (int)id;
        if (!gameState.activeMutationIds.Contains(v))
            pool.Add(v);
    }
    if (pool.Count == 0) return false;   // 全拿光了，跳过投票

    gameState.voteOptions.Clear();
    int take = Mathf.Min(3, pool.Count);
    for (int i = 0; i < take; i++)
    {
        int pick = Random.Range(0, pool.Count);
        gameState.voteOptions.Add(pool[pick]);
        pool.RemoveAt(pick);
    }

    gameState.playerVotes.Clear();
    gameState.voteTimer = VoteDuration;
    return true;
}

void TickVoting(float dt)
{
    gameState.voteTimer -= dt;

    bool timeUp   = gameState.voteTimer <= 0f;
    bool allVoted = AllActivePlayersVoted();
    if (!timeUp && !allVoted) return;

    int winnerOption = Tally();
    if (winnerOption >= 0 && winnerOption < gameState.voteOptions.Count)
        gameState.AddMutation((MutationId)gameState.voteOptions[winnerOption]);

    gameState.voteOptions.Clear();
    gameState.playerVotes.Clear();
    AdvanceToNextPreWave();
}

bool AllActivePlayersVoted()
{
    var pm = GameEntry.PlayerManager;
    int players = pm?.Players.Count ?? 0;
    return players > 0 && gameState.playerVotes.Count >= players;
}

int Tally()
{
    int n = gameState.voteOptions.Count;
    if (n == 0) return -1;

    var counts = new int[n];
    foreach (var kv in gameState.playerVotes)
        if (kv.Value >= 0 && kv.Value < n) counts[kv.Value]++;

    int best = 0;                       // 平票/零票 → 最小下标
    for (int i = 1; i < n; i++)
        if (counts[i] > counts[best]) best = i;
    return best;
}

void AdvanceToNextPreWave()
{
    gameState.currentWave++;
    gameState.preWaveTimer = GetDelayBeforeWave(gameState.currentWave - 1);
    TransitionTo(GamePhase.PreWave);
}
```

> 关键：`currentWave++` 从 `TickWave` 里挪到 `AdvanceToNextPreWave`，保证「投票期间 `currentWave` 仍是刚清空那波」，UI/逻辑不错位。投票结束后才推进。

---

## 9. UI：`VotingPanelUI.cs`（仿 StatusPanelUI 模式）

沿用 `StatusPanelUI` 的「由 `GamingForm` 驱动 Init/Clear、自订阅 GF 事件」模式（`StatusPanelUI.cs:19-36`）。放在 `GamingForm` 里当子面板，最省事。

职责：
- 订阅 `GamePhaseChangedEventArgs`：进入 `Voting` 显示面板并建按钮；离开则隐藏。
- 建按钮：读 `GameEntry.State.voteOptions`，每项用 `MutationRegistry.Get(id)` 取 `Name`/`Description`；按钮 `onClick` → `GameEntry.PlayerManager.GetLocalPlayer().CmdVote(i)`。
- 倒计时：`UniTask` 每帧读 `state.voteTimer`（仿 `TickPreWaveAsync`，`StatusPanelUI.cs:91-101`）。
- 票数：订阅 `state.playerVotes.OnChange`（SyncDictionary 回调）刷新每个选项的票数标签。

```csharp
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework.Event;
using Mirror;                 // OnVotesChanged 用到 SyncDictionary<,>.Operation，缺这行编译不过
using UnityEngine;
using UnityEngine.UI;

namespace Tower
{
    /// <summary>波间 3 选 1 投票面板。由 GamingForm 驱动 Init/Clear。</summary>
    public class VotingPanelUI : MonoBehaviour
    {
        [SerializeField] GameObject root;               // 整个面板根
        [SerializeField] Text timerText;
        [SerializeField] VoteOptionButton[] optionButtons; // 3 个：Text 名/描述/票数 + Button

        CancellationTokenSource m_Cts;

        public void Init()
        {
            GameEntry.Event.Subscribe(GamePhaseChangedEventArgs.EventId, OnPhaseChanged);
            Apply(GameEntry.State != null ? GameEntry.State.phase : GamePhase.Preparing);
        }

        public void Clear()
        {
            GameEntry.Event.Unsubscribe(GamePhaseChangedEventArgs.EventId, OnPhaseChanged);
            Unbind();
            Stop();
        }

        void OnPhaseChanged(object sender, GameEventArgs e)
        {
            if (e is GamePhaseChangedEventArgs args) Apply(args.Phase);
        }

        void Apply(GamePhase phase)
        {
            if (phase == GamePhase.Voting) Open();
            else Close();
        }

        void Open()
        {
            var state = GameEntry.State;
            if (state == null) return;

            root.SetActive(true);

            // 建选项
            for (int i = 0; i < optionButtons.Length; i++)
            {
                bool active = i < state.voteOptions.Count;
                optionButtons[i].gameObject.SetActive(active);
                if (!active) continue;

                var def = MutationRegistry.Get(state.voteOptions[i]);
                int idx = i; // 闭包捕获
                optionButtons[i].Bind(
                    def?.Name ?? "?",
                    def?.Description ?? "",
                    () => GameEntry.PlayerManager?.GetLocalPlayer()?.CmdVote(idx));
            }

            RefreshCounts();
            state.playerVotes.OnChange += OnVotesChanged;

            Stop();
            m_Cts = new CancellationTokenSource();
            TickAsync(m_Cts.Token).Forget();
        }

        void Close()
        {
            Unbind();
            Stop();
            if (root != null) root.SetActive(false);
        }

        void Unbind()
        {
            var state = GameEntry.State;
            if (state != null) state.playerVotes.OnChange -= OnVotesChanged;
        }

        // SyncDictionary.OnChange 签名依 Mirror 版本；此处只需「有变化就刷新」
        void OnVotesChanged(SyncDictionary<int, int>.Operation op, int key, int value)
            => RefreshCounts();

        void RefreshCounts()
        {
            var state = GameEntry.State;
            if (state == null) return;

            int n = state.voteOptions.Count;
            var counts = new int[n];
            foreach (var kv in state.playerVotes)
                if (kv.Value >= 0 && kv.Value < n) counts[kv.Value]++;

            for (int i = 0; i < n; i++)
                optionButtons[i].SetCount(counts[i]);
        }

        async UniTaskVoid TickAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var state = GameEntry.State;
                if (state == null || state.phase != GamePhase.Voting) break;

                if (timerText != null)
                    timerText.text = $"{Mathf.CeilToInt(state.voteTimer)}";

                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }

        void Stop()
        {
            if (m_Cts == null) return;
            m_Cts.Cancel();
            m_Cts.Dispose();
            m_Cts = null;
        }
    }
}
```

配套一个极简 `VoteOptionButton`（挂在每个选项按钮上）：

```csharp
using System;
using UnityEngine;
using UnityEngine.UI;

namespace Tower
{
    public class VoteOptionButton : MonoBehaviour
    {
        [SerializeField] Text nameText;
        [SerializeField] Text descText;
        [SerializeField] Text countText;
        [SerializeField] Button button;

        public void Bind(string name, string desc, Action onClick)
        {
            if (nameText != null) nameText.text = name;
            if (descText != null) descText.text = desc;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => onClick?.Invoke());
        }

        public void SetCount(int c)
        {
            if (countText != null) countText.text = $"{c} 票";
        }
    }
}
```

### 配套小改

- **`GamingForm.cs`**：像管理 `StatusPanelUI` 一样，持有 `VotingPanelUI` 引用并在 `OnOpen/OnClose` 调其 `Init()/Clear()`。
- **`StatusPanelUI.cs`**：`UpdateStatusUI` 的 `switch` 加一个 `case GamePhase.Voting: gameObject.SetActive(false); break;`（中央提示让位给投票面板）。
- **`GamingForm.RefreshWaveForPhase`**：`Voting` 阶段可显示「投票中」或沿用刚清空那波的波次文案（`currentWave` 未变，直接显示即可）。

> **备选（独立 UIForm 方案）**：若想让投票是独立弹窗而非 GamingForm 子面板，则在 `EnumUIForm.cs` 加 `VotingForm = 101`，由 `GameState.OnPhaseChanged`/`GamingForm` 在进入 `Voting` 时 `OpenUIForm`、离开时 `CloseUIForm`。子面板方案更轻，推荐先用子面板。

---

## 表现 / 同步链路说明（无需额外写逻辑）

```text
选项生成（服务端）：
  voteOptions(SyncList) 写入 → 各客户端 replay → 建按钮

投票（客户端 → 服务端）：
  点按钮 → CmdVote(i) → playerVotes[playerId]=i(SyncDict)
  → 各客户端 OnChange → 刷新票数

倒计时：
  voteTimer(SyncVar) 服务端每帧递减 → 客户端直接读（同 preWaveTimer）

结算（服务端）：
  Tally → AddMutation → activeMutationIds(SyncList).Add
  → RecalculateAllCombatAttributes() → 每个 AttributeComponent 重算
  → 属性式 Mutation 通过 SyncList<AttributeEntry> 下发 Final（客户端塔/敌人数值即时变）
  → 伤害式 Mutation（暴击）在下一次 DamageService.Apply 时生效（仅服务端逻辑）

中途加入投票中：
  voteOptions / playerVotes / voteTimer / phase 全走 SyncList/SyncDict/SyncVar replay
  → 新客户端看到选项、当前票数、剩余时间，可正常投票
```

---

## 边界与坑

1. **`currentWave` 时序**：投票期间不 `++`，结算后才推进（§8 重构点），否则 UI 波次错位。
2. **`AddMutation` 幂等**：`GameState.AddMutation` 已去重（`GameState.cs:108-109`），但投票池已排除已拥有项，正常不会重复。
3. **可选项耗尽**：`TryStartVote` 返回 false（Mutation 全拿光）→ 跳过投票直接下一波，不卡流程。
4. **全员投完提前结束**：`AllActivePlayersVoted` 用 `PlayerManager.Players.Count`。注意掉线玩家会减少分母；若担心「投票中掉线导致提前/延后」，可接受（塔防节奏容忍）。
5. **Host / 纯客户端**：Host 的 server 面写数据、client 面照常订阅刷新；纯客户端只读同步数据。均无需特判。
6. **不要做 Range 类 Mutation**：`AttributeKey.Range` 存在但**塔的检测范围用的是 Trigger Collider 半径，未读 Range 属性**（`TowerUnit` 无消费点）。加「射程 +15%」不会真的扩圈，除非同时改 `TowerUnit` 在 `Recalculate` 后调整 `SphereCollider.radius`——Day4 不做，选了 Damage/AttackInterval/暴击/敌速这些**已被消费**的键。
7. **属性 Mutation 对「已在场对象」与「新刷对象」都生效**：`AddMutation` 立刻 `RecalculateAllCombatAttributes()` 覆盖在场对象；之后新刷的敌人 `OnStartServer→Recalculate` 会自动带上（`AttributeComponent.cs:65-68,198-203`）。
8. **`OnDamageDealt` 改为按 `info.source` 门控**（Day4 修正，见 §3.3.1）：与 `OnDamageTaken` 的 `info.target` 门控对称。注意无源伤害（`source==null`）不会触发任何 `OnDamageDealt`。
9. **SyncDictionary.OnChange 签名**：不同 Mirror 版本回调签名略有差异（本工程 Mirror 96.x），以工程内 `SyncDictionary` 实际委托为准，逻辑只需「变了就 `RefreshCounts`」。

---

## Checklist

**内容 / 注册**

- [ ] `MutationId` 加 3 个值
- [ ] 3 个 `MutationDef` 子类 + `Description`
- [ ] `MutationCatalog` + `GameEntry.Custom` 里 `EnsureRegistered()`（双端）
- [ ] `DamageService.Apply`：`OnDamageDealt` 加 `Matches(info.source)` 门控（§3.3.1）

**阶段 / 投票机制**

- [ ] `GamePhase.Voting`
- [ ] `WaveConfig.WaveEntry.voteAfterWave`
- [ ] `GameState`：`voteOptions` / `playerVotes` / `voteTimer`
- [ ] `GamePlayer.CmdVote`
- [ ] `WaveDirector`：`TickVoting` / `TryStartVote` / `Tally` / `AdvanceToNextPreWave`，`currentWave++` 后移

**UI**

- [ ] `VotingPanelUI` + `VoteOptionButton`
- [ ] `GamingForm` 驱动其 Init/Clear
- [ ] `StatusPanelUI` 加 `Voting` 隐藏分支

**验收**

- [ ] 配 `voteAfterWave` 的波清空 → 进投票，3 选项 + 倒计时 + 票数
- [ ] 投票即时刷票数；全员投完立即结算；30s 到自动结算
- [ ] 胜出 Mutation 立即生效（塔伤/敌速肉眼可见）；后续新怪也带上
- [ ] `TowerCritUp` 生效后塔伤更常暴击；`OnDamageDealt` 门控改后无回归（塔伤害仍走暴击加成）
- [ ] `EnemySlow` Mutation 与 Frost `SlowBuff` 叠加更慢（percent 相加）
- [ ] Host / 纯客户端 / 投票中途加入 三种情况表现正确
- [ ] 最后一波（Boss）不触发投票

---

## 关键提醒

1. **管线已就位，Day4 主要填内容 + 加触发**：`AttributeComponent` 的 Mutation 遍历别动；`DamageService` 仅做一处对称化小改——`OnDamageDealt` 加 `Matches(info.source)` 门控（§3.3.1），不要重写整个遍历。
2. **`MutationCatalog.EnsureRegistered` 双端都要跑**（客户端要名字显示）。
3. **投票期间 `currentWave` 不动**，结算后才 `AdvanceToNextPreWave`。
4. **触发用配置开关 `voteAfterWave`**，不要写死「每 3 波」。
5. **平票/零票取最小下标**（确定性，避免不同端结果分歧——虽然计票只在服务端）。
6. **不做 Range 类 Mutation**（属性未被 Trigger 消费）。
7. **伤害钩子式 Mutation 顺序 + 门控**：`OnDamageDealt` 在暴击 roll 之前（改 `critChance` 有效），且按 `Matches(info.source)` 门控（§3.3.1）。

---

# 附录 A — 进入 Day4 前的代码审查 & 清理清单

> 2026-07-16 对工程 (2) 做的对照审查结论摘要。**仅记录，不在本次改代码**；清理动作留到 Day4 落地时顺手做。

**对照计划已良好落地**：阶段机（PreWave 重构）、Day2 Effect Pipeline、Day3 Phase1（SlowBuff）+ **Phase2（GF 对象池/引用池）均已实现**（`NetworkObjectPool` / `Enemy.ResetRuntimeState` / `AttributeComponent.ResetForSpawn` / `BuffHolder.ClearAll` / `ReferencePool.Acquire<SlowBuff>`）。

**可删的小死代码**（Day4 顺手清）：

| 位置 | 说明 |
|---|---|
| ~~`Core/GameContext.cs`~~ | **（对工程 (2) 已失效，勿再列为清理项）** 实测工程 (2) **不存在** `Assets/GameMain/Scripts/Core/GameContext.cs`，`Assets/Doc/project-overview.md` 也无「优先用 GameContext」条目（(2) 的 §5.3 是「服务器权威原则」）。该行是照旧工程/(1) 抄来的，(2) 已清理过，跳过。 |
| `Enemy.TakeDamage(int)` 重载 | 零调用（全库唯一 `TakeDamage(int)` 调用在 Mirror 示例 `TankProjectile`，与本作 `Enemy` 无关）；本作实际全走 `TakeDamage(ref DamageInfo)` |
| `WaveSpawnEnemyEventArgs.WaveIndex/SpawnIndex` | 写入但订阅端从不读 |
| `GamingForm.RefreshWave` 内 `phase==Preparing` 与 `waveNumber<=0` 两段 if | 上层已拦截，死分支 + `currentWave==0` 哨兵残留写法 |
| `BuildSlot.OnOccupiedChanged` | 空 hook 体（如无需响应，改普通字段） |

**保留（预留但暂无消费者，别删）**：属性 `Flat` 通道（`AddFlat/GetFlat`）、`EffectPriority` 非默认档位、`DamageTag.Periodic/Reflect/Heal` 与 `DamageInfo.IsHeal` 分支、`AttributeComponent.GetBase/SetBase`。这些是 Day5+/护盾/治疗/强化的预留位。

> 注：上表其余各项已对工程 (2) 逐一核实属实（`WaveSpawnEnemyEventArgs.WaveIndex/SpawnIndex` 仅写不读、`BuildSlot.OnOccupiedChanged` 为空 hook、`GamingForm.RefreshWave` 内 `phase==Preparing` 与 `waveNumber<=0` 两段 if 因唯一调用方 `RefreshWaveForPhase` 已在上层拦截而成死分支），可放心顺手清。仅 `GameContext` 一项对 (2) 不适用（见上）。

---

# 附录 B — 炮塔 Mesh 转向优化（视觉打磨）

> 目标：塔开火时炮管跟随目标水平转向，观感更自然。低带宽：只同步「当前目标 netId」，双端各自本地转向（沿用方案 D「初值+同逻辑=自然一致」的思路）。

**Inspector 约定**：把可旋转的炮管/炮台设为 `turretPivot`；`firePoint` 作为它的子物体（这样枪口随转向移动）。`turretPivot` 留空则旋转整座塔。

**`TowerUnit.cs` 改动要点**：

- 加字段：

```csharp
[Header("炮塔转向")]
[SerializeField] Transform turretPivot;      // 旋转部件，留空转整座塔
[SerializeField] float turretTurnSpeed = 720f; // 度/秒
[SyncVar] uint targetNetId;                   // 当前目标，双端据此本地转向
```

- `Update` 拆成「服务端逻辑」+「双端转向」：

```csharp
void Update()
{
    if (isServer) ServerTick();
    AimTick();   // 双端各自转向目标
}

[Server]
void ServerTick()
{
    enemiesInRange.RemoveAll(e => e == null || !e.IsAlive);

    if (currentTarget == null || !currentTarget.IsAlive || !enemiesInRange.Contains(currentTarget))
        currentTarget = enemiesInRange.Count > 0 ? enemiesInRange[0] : null;

    uint newId = currentTarget != null ? currentTarget.netId : 0;
    if (newId != targetNetId) targetNetId = newId;

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

void AimTick()
{
    Transform pivot = turretPivot != null ? turretPivot : transform;
    Transform tgt = ResolveTargetTransform();
    if (tgt == null) return;

    Vector3 dir = tgt.position - pivot.position;
    dir.y = 0f;                          // 只做水平偏航
    if (dir.sqrMagnitude < 0.0001f) return;

    Quaternion want = Quaternion.LookRotation(dir);
    pivot.rotation = Quaternion.RotateTowards(
        pivot.rotation, want, turretTurnSpeed * Time.deltaTime);
}

Transform ResolveTargetTransform()
{
    if (isServer)
        return currentTarget != null ? currentTarget.transform : null;
    if (targetNetId != 0 && NetworkClient.spawned.TryGetValue(targetNetId, out var id))
        return id.transform;
    return null;
}
```

**要点**：
- 客户端靠 `targetNetId` 从 `NetworkClient.spawned` 找目标 Transform（敌人位置本就 `NetworkTransform` 同步，转向自然跟手）。
- 只转 yaw（`dir.y=0`），塔不会前后仰。
- `RotateTowards` 平滑限速，避免瞬时抖动。
- 可选增强：`ServerTick` 里「大致对准目标后才开火」——但 720°/s 已足够快，MVP 不 gate。

---

# 附录 C — 文档核对修订 & 代码优化点（基于工程 (2) 实测，2026-07-16）

> 落地前对照 `Tower2022-main (2)` 实际代码逐条核对了本文引用。结论：**主线设计（Voting 阶段 + 3 个 Mutation + WaveDirector 重构 + 子面板 UI）与现有管线高度吻合，行号引用基本准确，可直接按文抄。** 以下是核对中发现的需修正项、真实风险与可选优化，均**只更新文档、不在本次改工程**；实现时参考。

## C.1 已在正文修正的两处

1. **§9 `VotingPanelUI` 缺 `using Mirror;`（真实编译错误）**：`OnVotesChanged` 形参用了 `SyncDictionary<int, int>.Operation`，原 using 块无 `Mirror` 命名空间会编译不过。已在正文 §9 补上。
2. **附录 A 的 `GameContext` 清理项对 (2) 失效**：工程 (2) 无 `Core/GameContext.cs`，`project-overview.md` 也无相关规范（(2) §5.3 是「服务器权威原则」）。已在附录 A 标注跳过。

## C.2 核对通过的关键引用（放心抄）

| 文档断言 | 实测 | 结论 |
|---|---|---|
| 属性路 `AttributeComponent.cs:198-203` 遍历 `activeMutationIds`→`Matches`→`CollectModifiers` | 完全一致 | ✅ |
| 伤害路 `DamageService.cs:23-31`；暴击 roll 在其后 | 一致（暴击 roll 实为 `L55-59`，文档写 54-59，偏一行） | ✅（微调行号即可） |
| `GameState.AddMutation` 去重 + `RecalculateAllCombatAttributes`（`105-114`，去重 `108-109`） | 一致 | ✅ |
| `WaveDirector.cs:98-114` 清空后直接 `currentWave++` 进 PreWave | 一致；`GetDelayBeforeWave` 已存在可复用 | ✅ |
| `GamePlayer.CmdSetReady`（`41-47`）、`playerId` 为 SyncVar（`L11`，服务端=netId） | 一致 | ✅ |
| `StatusPanelUI` Init/Clear（`19-36`）+ `TickPreWaveAsync`（`91-101`）模式 | 一致 | ✅ |
| `PlayerManager.Players` / `GetLocalPlayer()` / `AreAllPlayersReady()` | 全部存在，签名匹配 | ✅ |
| §6「Range 未被消费」 | `TowerUnit` 只读 `Damage`(L71)/`AttackInterval`(L59)/`ProjectileSpeed`(L72)，`AttributeKey.Range=23` 全库无消费点 | ✅ 结论成立 |
| §3.3「伤害全走 `DamageService.Apply`、暴击钩子有效」 | 常规弹 `HomingProjectile` 命中 `critChance=0.05` 后 `TakeDamage(ref)`→`DamageService.Apply`；`TowerCritUp +0.10`→实际 0.15 | ✅ |
| §边界#9 `SyncDictionary.OnChange` 签名 | 确认为 `Action<Operation, TKey, TValue>`（`Mirror/Core/SyncDictionary.cs:35`），即本作 `(Operation op, int key, int value)`——**正文 §9 写法正确**，可去掉「以工程为准」的不确定语气 | ✅ 已确证 |

## C.3 落地提醒（正文正确，但抄时容易踩）

1. **`WaveDirector` §8 的 `Random` 命名空间**：该文件当前 `using UnityEngine;` 且无 `using System;`，故 `Random.Range/Random.value` 解析为 `UnityEngine.Random`（正确）。抄入 §8 时**不要顺手加 `using System;`**，否则 `Random` 产生歧义、编译失败。另需确保有 `using System.Collections.Generic;`（`List<int>`）。
2. **现有 `WaveConfig` 资产需重勾**：`WaveEntry` 是 `[Serializable]`、`WaveConfig` 是 Odin `SerializedScriptableObject`；新增 `voteAfterWave` 对**已存在的资产**默认 `false`。若不去资产里勾选某几波，全程不会触发投票——验收「配 `voteAfterWave` 的波清空→进投票」前，先确认资产里**至少勾了一波**（且别只勾最后一波，那波被 `IsLastWave` 拦成 Victory）。
3. **`GamingForm` 需新增 `[SerializeField] VotingPanelUI votingPanel;` 并在 `OnOpen/OnClose` 调 `Init()/Clear()`**（仿 `statusPanel`）。Checklist 已含，勿漏挂 Inspector 引用。
4. **`EnumUIForm`（备选独立窗口方案）** 若采用需同时在 `UIConfig` 注册 `VotingForm=101` 的资源与 prefab；正文推荐的**子面板方案更省事**，无需动 `EnumUIForm`。

## C.4 表述澄清（避免验收误判）

1. **`EnemySlow` Mutation 不会让敌人变蓝**：变蓝是 Day3 `SlowBuff.OnApply(isServer:false)` 里 `SetSlowVisual(true)` 的专属表现；`EnemySlow` 走的是属性式 `Speed AddPercent`，只让 `NavMeshAgent.speed` 下降→敌人**移动**变慢。完成标志里的「敌速肉眼可见」指移动速度、不是颜色，别按颜色验收。
2. **本作只有 3 个 Mutation，投票池会递减**：首次投票必是这 3 个；每胜出 1 个可投池 -1（3→2→1）；第 4 次投票 `TryStartVote` 因池空返回 `false`、直接跳过（§边界#3）。测试时别期望第二次投票还有 3 个「新」选项——这正是 Day5「扩池」要补的量。

## C.5 代码优化点（本次不改，供 Day4/后续择机）

1. **计时器改「结束时间戳」少同步一份每帧流量**：`voteTimer` 与既有 `preWaveTimer` 都是 `float` SyncVar 每帧递减 → 每帧 dirty、持续占用带宽。更省的做法是同步一个 `double voteEndTime = NetworkTime.time + VoteDuration`（只写 1 次），客户端本地 `Mathf.CeilToInt(voteEndTime - NetworkTime.time)` 显示剩余秒。这样 §9 的 `TickAsync` 逻辑不变，但网络只发 1 个值。可顺带把 `preWaveTimer` 一并这样优化（收益一致）。MVP 阶段保持现状（与 `preWaveTimer` 一致）亦可。
2. **`VotingPanelUI` 增订 `voteOptions.OnChange` 提升稳健性**：目前按钮只在 `OnPhaseChanged→Voting` 的 `Open()` 里按当时的 `voteOptions` 建一次。Mirror 反序列化顺序是「SyncObject 先于 SyncVar hook」，正常单帧过渡时选项已就位；但为防御中途加入/顺序边界，建议在 `Open()` 里同时 `state.voteOptions.OnChange += ...` 触发重建，`Close()` 退订（注意 `voteOptions.Clear()` 也会回调，需判 `phase==Voting` 再重建）。
3. **`Tally()`/`AllActivePlayersVoted` 可按当前在场玩家过滤残票**：玩家投票后掉线，其票仍留在 `playerVotes`（key 不随 `Players` 移除而删）。当前 `>=` 判定与计票会把残票算进去，塔防节奏可容忍（§边界#4 已认可）。若要更严谨，结算时用 `PlayerManager.Players` 的 `playerId` 集合过滤 `playerVotes`。MVP 不做。
4. **`GamePhase` 新增值的位置**：正文把 `Voting` 插在 `Wave` 与 `Victory` 之间（语义清晰、推荐）。注意这会改变 `Victory/Defeat` 的底层 int；本作 `phase` 仅运行期 SyncVar、无持久化，全部按枚举名比较，**无实际影响**。仅在未来加「按 int 存档 phase」时才需改为追加到枚举末尾。
5. **附录 B `AimTick` 每帧 `Quaternion.LookRotation`**：可缓存目标方向、目标未变时跳过重算；转向本身开销极小，属锦上添花。

---

## Day5 预告

3 个 Mutation 的**数值打磨 + 扩池**（更多 MutationId、按品质/主题分组），以及可能的「每局随机解锁塔池」。投票机制本文已完成，Day5 主要是内容量。
