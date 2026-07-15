# 波次 / 游戏阶段状态机重构设计

> 日期：2026-07-15  
> 工程：`F:\Download\Tower2022-main (1)\Tower2022-main`（Day1 已基本合入）  
> 状态：设计定案，待实现  
> 问题：开战塞进 `BetweenWaves` + 用 `currentWave==0` 当开战哨兵，语义错误；第一波倒计时靠「假波间」硬凑

---

## 1. 目标与非目标

### 目标

玩家感知流程：

```text
进入游戏 → 全员 Ready → 第 1 波倒计时 UI → 进入第 1 波刷怪
→ 清完 → 第 N 波倒计时 → 第 N 波 → … → Victory / Defeat
```

代码语义：

- 开战 = 离开 `Preparing`，进入 `PreWave`，且 **`currentWave = 1`**
- 波前倒计时是正规阶段，不是「currentWave 仍为 0 的 BetweenWaves」
- 倒计时 UI 直接显示 `currentWave`，不再 `currentWave + 1` 凑数
- 波前延迟只由 Director 管一份，去掉 WaveManager 内重复延迟

### 非目标

- Effect Pipeline / Buff / Mutation（见 `w2-day2-detailed.md`）
- 投票阶段 Voting（W2 Day4，以后在 PreWave 或独立 Phase 插入）
- 改 WaveConfig 刷怪时间线数据结构

---

## 2. 阶段枚举（方案 B）

```csharp
public enum GamePhase
{
    Preparing,  // 等人 Ready（Space）
    PreWave,    // 波前倒计时（含开局第 1 波）
    Wave,       // 本波刷怪 / 清怪进行中
    Victory,
    Defeat,
}
```

- **删除** `BetweenWaves`（全工程替换为 `PreWave`）
- 同步字段建议：`betweenWavesTimer` → **`preWaveTimer`**（可先留别名一帧，但推荐直接改名并改 UI）

### 状态流

```text
Preparing
  │ 全员 Ready
  ▼
PreWave (currentWave=1, preWaveTimer=waves[0].delayBeforeWave)
  │ timer → 0
  ▼
Wave (StartWave(0))
  │ 本波清空
  ├─ 最后一波 → Victory
  └─ 否则 currentWave++, preWaveTimer=waves[currentWave-1].delay → PreWave → Wave …
```

Defeat 仍可由 HomeBase 等随时 `NotifyDefeat()` 切入。

---

## 3. 职责拆分

| 组件 | 职责 | 不负责 |
|------|------|--------|
| **GameState** | SyncVar：`phase` / `currentWave` / `preWaveTimer` / `sharedGold`；权威接口 TrySpend/AddGold/NotifyDefeat；开战 RPC | 不 Tick 阶段、不刷怪 |
| **WaveDirector**（Server only） | 阶段状态机；Ready→开战；PreWave 倒计时；波清空后切下一波/胜利；击杀加金币 | 不解析刷怪时间线 |
| **WaveManager** | 单波：按 config 排队刷怪、统计 spawned/killed、`IsCurrentWaveCleared` / `IsLastWave` | **不再**持有波前 delay；`StartWave` 不再需要 `skipDelayBeforeWave` |

### 开战 `BeginBattle()`（Director，从 Preparing 触发一次）

```text
1. GameEntry.Build?.ClearAllOccupancy()
2. gameState.BroadcastStartBattle()
3. gameState.currentWave = 1
4. gameState.preWaveTimer = DelayForWaveIndex(0)  // config.waves[0].delayBeforeWave
5. gameState.phase = PreWave
```

**禁止**再用 `if (currentWave <= 0)` 判断「是不是第一次开战」。

### PreWave Tick

```text
preWaveTimer -= dt
timer > 0 → return
StartWave(currentWave - 1)   // 配置 0-based
phase = Wave
```

### Wave 清空后

```text
if IsLastWave → Victory
else:
  currentWave++
  preWaveTimer = DelayForWaveIndex(currentWave - 1)
  phase = PreWave
```

### WaveManager.StartWave(index)

- 删除 `skipDelayBeforeWave` 参数与内部 `delayBeforeWaveTimer`
- `ServerTick` 只推进刷怪时间线，不再先扣波前等待

`WaveEntry.delayBeforeWave` **保留在配置里**，由 Director 读取。

---

## 4. 字段与「是否开战」语义

| 字段 | 含义 |
|------|------|
| `phase == Preparing` | 未开战，等人 Ready |
| `phase == PreWave` && `currentWave == 1` | 已开战，第 1 波倒计时 |
| `currentWave` | **1-based 当前（或即将开始的）波次**；Preparing 时为 **0** |
| `preWaveTimer` | PreWave 剩余秒数 |

UI / 逻辑判断：

- 「战斗未开始」→ `phase == Preparing`（**不要**再用 `currentWave == 0` 作为唯一依据；Preparing 时 wave 恰好为 0 是结果不是原因）
- 「第 N 波倒计时」→ `phase == PreWave`，文案用 `currentWave`
- 「波次 HUD」→ Wave / PreWave 都可显示 `currentWave/totalWaves`；Preparing 显示未开始

可选（非必须）：`bool BattleStarted => phase != Preparing && phase != Defeat` 等，避免散落魔法判断。

---

## 5. UI 改动要点

### `StatusPanelUI`

- `GamePhase.BetweenWaves` → `PreWave`
- 文案：`第 {state.currentWave} 波 {Ceil(preWaveTimer)} 秒后开始`  
  （去掉 `currentWave + 1`）
- 计时字段改读 `preWaveTimer`

### `GamingForm.RefreshWave`

- Preparing → 「战斗未开始」
- 其他 → `波次: {currentWave}/{total}`（PreWave 时显示即将开始的波也合理）

### Ready

- 仍仅 `phase == Preparing` 可 Ready（`GamePlayer` / `PlayerBuildModeComponent` 不变语义）

---

## 6. 文件改动清单

| 文件 | 改动 |
|------|------|
| `GameState.cs` | 枚举改名；`betweenWavesTimer` → `preWaveTimer`；注释修正 |
| `WaveDirector.cs` | Preparing→`BeginBattle`→PreWave；TickPreWave；去掉 `currentWave<=0` 开战旁路 |
| `WaveManager.cs` | 删除波前 delay 与 `skipDelayBeforeWave` |
| `StatusPanelUI.cs` | PreWave + 文案 + timer 字段名 |
| `GamingForm.cs` | 用 phase 判断未开战 |
| 其他 `BetweenWaves` 引用 | 全库 grep 替换 |

配置资产 `WaveConfig`：**不改结构**，继续用每波 `delayBeforeWave`。

---

## 7. 验收

- [ ] 全员 Ready → 进入 PreWave，`currentWave==1`，UI 显示「第 1 波 X 秒后开始」
- [ ] 倒计时结束 → Wave，开始按时间线刷怪
- [ ] 第 1 波清完 → PreWave，`currentWave==2`，再倒计时 → 第 2 波
- [ ] 最后一波清完 → Victory
- [ ] Preparing 时 HUD「战斗未开始」；任意时刻不再依赖 `currentWave<=0` 触发 `BroadcastStartBattle`
- [ ] 中途加入客户端：phase / currentWave / preWaveTimer / totalWaves 显示正确

---

## 8. 与后续 W2 的衔接

- Day4 **Voting**：建议插在「一波清空后、进入下一波 PreWave 前」，或把 PreWave 延长并叠投票 UI；本文不展开，只要阶段语义正确，投票是加态或挂接，而不是再滥用 `currentWave==0`。
- Day2 Effect：与本重构正交，可并行。

---

## 9. 修订记录

| 日期 | 内容 |
|------|------|
| 2026-07-15 | 方案 B 定案：PreWave + 开战即 currentWave=1；延迟只在 Director |
