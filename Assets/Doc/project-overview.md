# Tower2022 项目概览

> 最后更新：2026-06-29  
> 用途：快速熟悉项目结构、技术栈与当前进度，便于后续 Day 2–3 及后续开发。

---

## 1. 项目简介

**Tower2022** 是一款基于 Unity 的**联网塔防**游戏项目。采用 **Dedicated Server（独立服务器）+ Client** 架构，通过 **ParrelSync** 在双 Editor 实例中模拟 Server / Client 联调。

当前阶段处于 **Week 1**，Day 1（网络基础搭建）已完成，即将进入 **Day 2–3（核心战斗循环）**。

### Day 2–3 目标

| 阶段 | 目标 |
|------|------|
| Day 2 | 敌人沿 NavMesh 路径行走 + Spawner 定时生成 |
| Day 3 | 塔检测敌人 + 自动开火 + 服务器权威扣血 |

完成后应能在双 Editor 中看到：敌人生成 → 沿路径走向 Base → 塔自动攻击 → 敌人扣血死亡，两端同步。

详细步骤见 [`w1-day2-3-detailed..md`](./w1-day2-3-detailed..md)。

---

## 2. 技术栈

| 类别 | 选型 | 版本 / 说明 |
|------|------|-------------|
| 引擎 | Unity | **2022.3.62f3** LTS |
| 游戏框架 | Unity Game Framework (GF) | 2021.05.31，位于 `Assets/ThirdParty/GF/` |
| 网络 | Mirror | **96.10.2**，位于 `Assets/ThirdParty/Mirror/` |
| 传输层 | KCP Transport | 端口 **7777**，DualMode 开启 |
| 双开调试 | ParrelSync | Git 包 + `Assets/Plugins/ParrelSync/` |
| IDE | Cursor / VS Code | `com.boxqkrtm.ide.cursor`，`.vscode/launch.json` 支持 Attach to Unity |
| 其他 | TextMeshPro、Newtonsoft.Json、Timeline | 见 `Packages/manifest.json` |

### 架构组合说明

```
Unity Game Framework（流程/资源/UI 等基础设施）
        +
Mirror（联网同步、NetworkBehaviour、Spawn/Destroy）
        +
ParrelSync（主 Editor = Server，Clone Editor = Client）
```

GF 与 Mirror **并行存在**：GF 负责框架层组件注册与生命周期；Mirror 负责游戏对象的联网行为。自定义入口 `Tower.GameEntry` 在 GF 初始化之后注册 Mirror 相关组件。

---

## 3. 目录结构

```
Tower2022/
├── Assets/
│   ├── Doc/                          # 项目文档
│   │   ├── project-overview.md       # 本文档
│   │   └── w1-day2-3-detailed..md    # Day 2–3 详细开发指南
│   ├── GameMain/                     # ★ 游戏主资源（业务代码放这里）
│   │   ├── Scenes/
│   │   │   ├── MainScene.unity       # 客户端主场景（含 UI/Sound）
│   │   │   └── ServerScene.unity     # 服务端场景（精简，无 UI/Sound）
│   │   └── Scripts/
│   │       ├── Base/                 # GameEntry 入口（partial class）
│   │       ├── Core/                 # GameContext 等全局工具
│   │       └── Network/              # GameNetworkManager、NetworkDebugUI
│   ├── ThirdParty/
│   │   ├── GF/                       # Unity Game Framework
│   │   └── Mirror/                   # Mirror 网络库 + 示例
│   ├── Plugins/
│   │   └── ParrelSync/               # 双 Editor 克隆配置
│   ├── Resources/                    # （当前为空）
│   └── ScriptTemplates/              # Mirror 脚本模板
├── Packages/manifest.json
└── ProjectSettings/
```

### 脚本路径约定

| 位置 | 用途 |
|------|------|
| `Assets/GameMain/Scripts/` | **项目自有代码**（当前全部在这里） |
| `Assets/ThirdParty/` | 第三方库，**不要修改** |
| Day 2–3 文档中的 `Assets/Scripts/Gameplay/...` | 文档示例路径；**建议实际放在 `Assets/GameMain/Scripts/Gameplay/`** 以保持与现有结构一致 |

Day 2–3 将新增（尚未创建）：

```
Assets/GameMain/Scripts/
├── Gameplay/
│   ├── Enemy/Enemy.cs
│   ├── Spawner/EnemySpawner.cs
│   ├── Tower/Tower.cs
│   └── Projectile/Projectile.cs      # 方案 D：NetworkBehaviour 子弹
└── Prefabs/                            # 建议：Assets/GameMain/Prefabs/ 或 Assets/Prefabs/
    ├── Enemy_Slime.prefab
    ├── Tower_Cannon.prefab
    └── Projectile_Bullet.prefab
```

---

## 4. 当前已实现内容（Day 1）

### 4.1 场景 Hierarchy

两个场景结构基本相同，差异见下表：

```
Scene Root
├── Main Camera
├── Directional Light
└── GF                          ← 挂 Tower.GameEntry
    ├── Main                    ← GF BaseComponent + ResourceComponent 等
    ├── Extensions              ← Web Request, Debugger, File System, Entity,
    │                             Localization, Download, Resource, FSM,
    │                             Reference Pool, Scene, Data Node, UI*, Sound*,
    │                             Object Pool, Event, Config, Procedure, Setting
    └── Customs
        └── NetworkManager      ← KcpTransport + GameNetworkManager + NetworkDebugUI
```

| 组件 | MainScene | ServerScene |
|------|-----------|-------------|
| UI Component | ✅ | ❌ |
| Sound Component | ✅ | ❌ |
| 其余 GF 组件 | ✅ | ✅ |

> NavMesh **尚未烘焙**（`NavMeshData: {fileID: 0}`）。Ground / SpawnPoint / Base 等关卡物体 **尚未搭建**。

### 4.2 已有脚本

| 文件 | 职责 |
|------|------|
| `GameEntry.cs` | 入口：`DontDestroyOnLoad`，启动时初始化 GF + 自定义组件 |
| `GameEntry.Builtin.cs` | 注册 GF 全部内置组件（Base、Resource、Procedure、UI…） |
| `GameEntry.Custom.cs` | 注册自定义组件：`GameEntry.NetWork` → `GameNetworkManager` |
| `GameContext.cs` | 静态网络身份判定：`IsServer` / `IsClient` / `IsHost` / `IsDS` / `IsPureClient` / `IsOffline` |
| `GameNetworkManager.cs` | 继承 `NetworkManager`，覆写连接/断开日志回调 |
| `NetworkDebugUI.cs` | OnGUI 调试面板：Start Server / Client / Host / Stop |

### 4.3 网络配置（NetworkManager 物体）

| 配置项 | 当前值 |
|--------|--------|
| Transport | KCP，port **7777** |
| networkAddress | localhost |
| autoCreatePlayer | **false**（无玩家角色，塔防不需要） |
| playerPrefab | 空 |
| spawnPrefabs | **空**（Day 2 需注册 Enemy 等） |
| sendRate | 60 |

### 4.4 尚未配置 / 缺失项

- `EditorBuildSettings` 中 **Build Scenes 为空**
- **Tag 列表为空**（Day 2 需添加 `Base` Tag）
- **无 Prefab**（`Assets/Prefabs/` 不存在）
- **无 Gameplay 脚本**（Enemy / Tower / Spawner 等）
- `GameNetworkManager` **尚未** 实现 `OnStartServer` 启动 Spawner（Day 2 任务）

---

## 5. 核心代码模式

### 5.1 命名空间

所有项目脚本统一使用 `namespace Tower`。

### 5.2 GameEntry 访问模式

```csharp
// GF 内置组件
GameEntry.Resource.LoadAsset(...);
GameEntry.Procedure.StartProcedure(...);

// 自定义组件
GameEntry.NetWork.StartServer();
```

新增自定义组件时：在 `GameEntry.Custom.cs` 的 `InitCustomComponents()` 中 `FindObjectOfType` 并暴露静态属性。

### 5.3 网络身份判定

优先使用 `GameContext`，而非直接读 Mirror 静态变量：

```csharp
if (GameContext.IsServer) { /* 仅服务器逻辑 */ }
if (GameContext.IsPureClient) { /* 纯客户端 */ }
```

### 5.4 服务器权威原则（Day 2–3 及以后）

| 逻辑 | 运行端 |
|------|--------|
| 敌人生成、NavMesh 寻路、到达 Base | **Server** |
| 塔选目标、开火、伤害计算 | **Server** |
| 敌人位置同步 | Server 驱动 → `NetworkTransformReliable` 同步到 Client |
| 客户端 NavMeshAgent | **禁用**（`OnStartClient` 中 `agent.enabled = false`） |
| 子弹飞行（方案 D） | 双端各自 `MoveTowards`；伤害与 Destroy **仅 Server** |
| 客户端子弹视觉 | 本地预测假命中，等 Server Destroy |

---

## 6. Day 2–3 设计要点（摘要）

完整步骤与代码见 [`w1-day2-3-detailed..md`](./w1-day2-3-detailed..md)。

### Day 2：敌人与生成

1. 搭建场景：Ground (30×30)、SpawnPoint、Base（Tag = `Base`）
2. 烘焙 NavMesh（Unity 2022：`Window → AI → Navigation`）
3. 创建 `Enemy_Slime` Prefab：`NetworkIdentity` + `NavMeshAgent` + `Rigidbody(Kinematic)` + `NetworkTransformReliable` + `Enemy.cs`
4. 创建 `EnemySpawner` 挂 SpawnPoint，服务器 `NetworkServer.Spawn`
5. 扩展 `GameNetworkManager.OnStartServer()` 调用 `StartSpawning()`
6. 注册 Enemy Prefab 到 `spawnPrefabs`

### Day 3：塔与子弹

1. 创建 `Tower_Cannon`：`NetworkIdentity` + `SphereCollider(isTrigger)` + `Tower.cs`
2. 子弹采用 **方案 D**：`Projectile` 为 `NetworkBehaviour`，**不挂 NetworkTransform**，用 SyncVar 同步 `targetNetId / startPos / speed / hasHitVisually`
3. 职责分离：**Tower 只 Spawn**，**Projectile 自治飞行+命中**，**Enemy 负责 TakeDamage**
4. 场景中手动放置一座塔（Day 3 不做玩家建塔）

### 明确不做（Day 2–3 范围外）

- 玩家建塔、UI、金币、多种塔/敌人、Buff、对象池、主菜单、XLua、asmdef 分离

---

## 7. 开发工作流

### 7.1 双 Editor 联调（ParrelSync）

1. 主 Editor 打开 `MainScene` 或 `ServerScene` → Play → **Start Server**
2. ParrelSync 创建 Clone → Clone Editor Play → **Start Client**
3. 通过 `NetworkDebugUI` 左上角面板操作

### 7.2 调试 Attach

`.vscode/launch.json` 已配置 **Attach to Unity**，可在 Cursor 中附加调试器。

### 7.3 常用 Console 日志前缀

| 前缀 | 来源 |
|------|------|
| `[Server]` | 服务器逻辑 |
| `[Client]` | 客户端逻辑 |
| `[NetworkDebugUI]` | 调试 UI 操作 |

---

## 8. 文档与代码差异备忘

开发 Day 2–3 时注意以下几点：

| 项目 | 文档描述 | 项目现状 | 建议 |
|------|----------|----------|------|
| 脚本路径 | `Assets/Scripts/Gameplay/...` | `Assets/GameMain/Scripts/` | 新代码放 `GameMain/Scripts/Gameplay/` |
| Unity 版本 | 文档写 "Unity 6 Navigation (Obsolete)" | 实际 **2022.3** | 使用 `Window → AI → Navigation` 经典窗口 |
| GameNetworkManager 路径 | `Assets/Scripts/Network/` | 已在 `GameMain/Scripts/Network/` | 直接扩展现有文件 |
| 子弹方案 | 文档前半用 NetworkBehaviour Projectile；后半有 ProjectileVisual 章节 | 以 **方案 D（NetworkBehaviour Projectile）** 为准 | Step 7 ProjectileVisual 为旧方案备选，可忽略 |
| 场景 | 未指定用哪个场景 | 有 MainScene + ServerScene | Day 2 可在 MainScene 上直接搭关卡；Server 专用场景可后续再分 |

---

## 9. 后续里程碑（Week 1 预览）

| 天数 | 内容 |
|------|------|
| Day 4 | 客户端建塔 + 共享金币 |
| Day 5 | UI（金币、波次显示） |
| W2+ | 多种敌人/塔、Buff、视觉打磨、对象池 |
| W4 | 关卡选择、主菜单、XLua / 热更 |

---

## 10. 快速检查清单（开始 Day 2 前）

- [ ] 确认 ParrelSync Clone 能正常双开
- [ ] 主 Editor Start Server + Clone Start Client 能连接（Console 有 connected 日志）
- [ ] 决定开发场景：MainScene（推荐，有完整 GF）或 ServerScene
- [ ] 阅读 [`w1-day2-3-detailed..md`](./w1-day2-3-detailed..md) Day 2 Step 1 起
- [ ] 新脚本统一放 `Assets/GameMain/Scripts/`，命名空间 `Tower`

---

## 附录：关键文件索引

| 文件 | 路径 |
|------|------|
| 游戏入口 | `Assets/GameMain/Scripts/Base/GameEntry.cs` |
| 网络管理器 | `Assets/GameMain/Scripts/Network/GameNetworkManager.cs` |
| 网络调试 UI | `Assets/GameMain/Scripts/Network/NetworkDebugUI.cs` |
| 网络身份工具 | `Assets/GameMain/Scripts/Core/GameContext.cs` |
| 客户端场景 | `Assets/GameMain/Scenes/MainScene.unity` |
| 服务端场景 | `Assets/GameMain/Scenes/ServerScene.unity` |
| Day 2–3 指南 | `Assets/Doc/w1-day2-3-detailed..md` |
| 包依赖 | `Packages/manifest.json` |
