# W1 交付说明

## 如何运行

1. Unity 打开项目，主 Editor Play → **Start Server**
2. ParrelSync 副 Editor Play → **Start Client**（IP: localhost）
3. 双端 Preparing 阶段各自按 **Space** Ready
4. 全员 Ready → 游戏开始

## 玩法

| 操作 | 说明 |
|------|------|
| **Space** | Preparing 阶段切换 Ready |
| **B** | 切换建造模式 |
| **点击底部塔卡片** | 选中塔（金币不足自动置灰） |
| **鼠标左键** | 在建造点放置塔 |
| **ESC / 右键** | 退出建造模式 |

- 守住基地，HP 归零 = 失败；打通全部波次 = 胜利
- **Host** 可在结算面板点 **Restart** 重开（Client 按钮置灰）

## 塔类型

| 塔 | 造价 | 效果 |
|----|------|------|
| Cannon | 50 | 追踪弹，25 伤害 |
| Frost | 100 | AOE 30 伤害 + 40% 减速 3 秒 |

塔配置在 `Assets/GameMain/Config/TowerConfig.asset`，加塔只改配置 + UI 卡片 `towerConfigId`。

## 已知问题（W2+ 处理）

- 无对象池
- 无死亡特效 / 音效
- 减速视觉简陋（材质变蓝）
- 材质 Instance 未统一回收

## 关键文件

| 模块 | 路径 |
|------|------|
| 游戏状态机 | `Scripts/Gameplay/GameState.cs` |
| 波次管理 | `Scripts/Gameplay/Wave/WaveManager.cs` |
| 波次配置 | `Scripts/Gameplay/Wave/WaveConfig.cs` |
| 基地 HP | `Scripts/Gameplay/HomeBase/HomeBase.cs` |
| 子弹基类 | `Scripts/Gameplay/Missile/ProjectileBase.cs` |
| 追踪 / 冰霜弹 | `HomingProjectile.cs` / `FrostProjectile.cs` |
| 战斗 UI | `Scripts/UI/UIForms/GamingForm.cs` |
| 塔配置 | `Config/TowerConfig.asset` |
| 开发文档 | `Assets/Doc/w1-day5-7-detailed(1)..md` |
