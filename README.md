# SKNRM 1.0.0 · WorldBox 0.51.2（build 719）性能模组

> 纯性能模组：**玩法与画面保持原版**，只降低后台计算量与已知崩溃。
> 配置：模组目录 `sknrm_config.json`；游戏内按 **F7** 打开设置页。

---

## 1. 它做了什么（四层节流 + 四项防护）

### 四层节流

| 层 | 机制 | 默认 |
|---|---|---|
| **角色 AI 节流** | 人口超阈值后，普通市民的 `b4 任务校验 / b6_0 决策 / b6 AI 思考` 按相位跳过 | 市民 1/8 帧 |
| **军队分级** | 战斗中的士兵 1/4；队长未参战时士兵 1/8 且不寻敌；队长/前线/被围城满速 | `ArmyThrottleFrames=4` |
| **城市轮转（UPP）** | 城市按邻接分桶轮转，被拒的城本帧只递减计时器 | 桶容量 5、宽限 10 帧 |
| **重计算 job 分帧** | 17 个纯逻辑 job 每 3 帧跑一次，**视觉 job 保持每帧**（移动/动画不掉帧） | `SknLogicJobStride=3` |

### 四项防护（都是实测过的崩溃/刷屏）

| 防护 | 拦什么 |
|---|---|
| 空投射物 | `Projectile.start` 的 `ArgumentNullException`（坏装备的 projectile id 为空） |
| 坏装备精灵 | `ItemRendering` / `DynamicSprites` 的 NRE（装备 `gameplay_sprites` 为空） |
| 资源精灵自愈 | `ResourceAsset.getGameplaySprite` 的 NRE（读档后按路径重载 39 个资源精灵） |
| 名牌 LOD | 名牌不可见时整段 `NameplateManager.update` 跳过 |

### 稳定性

- **载入宽限**：读档后 12 秒内市民 1/16，避免读大存档头几帧卡死；
- **GC 择机**：游玩时关闭自动 GC，堆超 1900MB 或暂停时回收一次 → 战斗中间不再出现 1–3 秒爆卡（**硬上限必须有，否则会 OOM**）。

---

## 2. 配置项（sknrm_config.json）

| 键 | 默认 | 说明 |
|---|---|---|
| `SknPlus` | true | 总开关 |
| `SknThrottleFrames` | 8 | 市民节流周期（1 = 原版） |
| `ArmyThrottleFrames` | 4 | 战斗士兵节流周期 |
| `ArmyIdleThrottle` | 8 | 队长空闲时士兵节流周期 |
| `ArmyProtectMode` | 1 | 0=全军保护 / 1=只保在战斗的 / 2=不保 |
| `WarProtectScope` | 2 | 0=交战国全保 / 1=前线城 / 2=只保被围城 |
| `SknLogicJobStride` | 3 | 逻辑 job 分帧周期 |
| `SknActorStrideMax` | 1 | 整条角色管线分帧（1=关，靠 job 分帧即可） |
| `EnemyCacheFrames` | 3 | 寻敌缓存跨帧数 |
| `EnemyPhaseFrames` | 5 | 平民寻敌相位周期 |
| `EnemyPeacefulRangeZero` / `EnemyCivilianRangeZero` | true | 和平王国/平民只扫本 chunk |
| `UppProtectRecruitment` | true | **征兵保护**：战士名额没满/有进攻命令/被围攻的城市不参与轮转 |
| `LoadGraceSeconds` / `LoadGraceThrottle` | 12 / 16 | 读档宽限 |
| `GcDeferMode` / `GcDeferHardCapMb` | true / 1900 | GC 择机与硬上限 |
| `GuardBadProjectiles` / `GuardBadItemSprites` / `FixResourceSprites` | true | 三项防崩 |
| `SkipNameplatesWhenHidden` | true | 名牌 LOD |
| `LogEverySeconds` | 30 | 诊断行间隔（0=关闭） |

---

## 3. 验证方法

启动游戏后看 `Player.log`：

```
[NML]: [SKNRM]: Loaded
[SKNRM] 防护补丁安装 5/5（投射物/远程攻击/坏装备/名牌LOD/读档钩子）
[SKNRM] 分段计时补丁安装 4/4（模拟/角色/地图层绘制/名牌）
[SKNRM] 寻敌优化已安装 3/2
[SKNRM] 逻辑 job 分帧：17 个 job 每 3 帧跑一次（失败 0 个）
[SKNRM] 已加载：补丁 14 处
```

每 30 秒一条诊断行，关键字段：

- `闸门=开/关 单位=N 节流=K`：人口闸门与当前节流倍率；
- `耗时[模拟/角色/绘制/名牌/其它]`：帧时间构成；
- `跳过[b4/b6]`：AI 节流实际生效次数（应持续增长）；
- `逻辑job跳过`、`名牌跳过`、`军节流`、`征兵`、`征兵放行`；
- `异常=0`：补丁内部异常数（正常应恒为 0）。

---

## 4. 已知边界

- **地图内存与模组无关**：实测 1216×896（108 万格）活对象 1.5GB，其中约 700MB 是引擎自身的每格结构，隔离实验（只留 SKNRM）证明不是模组造成；GC 停顿是这个世界规模的必然代价，只能靠"择机回收"挪到暂停时。
- **优化会轻微改变节奏**：市民思考降频后，征兵/建造等由城市逻辑驱动的行为会比原版慢一点；`UppProtectRecruitment` 已针对征兵做了豁免，若仍觉得慢可把 `SknThrottleFrames` 调到 6。
- 视觉相关 job（移动、动画、渲染、名牌可见时）**永不节流**。

---

## 5. 征兵为什么会被拖慢（以及现在的处理）

719 的征兵链是：市民跑到「找工作」决策 `find_city_job` → 城市 `City.setCitizenJob` → `tryToMakeWarrior`。
而「找工作」是**决策**，跑在被节流的 `b6` 里；城市侧的 `_timer_warrior` 冷却也依赖 `City.update`。
所以只要节流，征兵就会变慢。现在的三处针对性放宽：

| 配置 | 默认 | 作用 |
|---|---|---|
| `UppProtectRecruitment` | true | 战士名额没满 / 有进攻命令 / 正在被围攻的城市，**不参与城市轮转拒绝** |
| `UppProtectWarCities` | true | **交战王国**的城市不参与轮转（战争期征兵/调兵/补兵最吃城市 AI） |
| `JobSeekerThrottle` | 2 | 所在城市**战士名额没满**的市民，节流从 1/8 放宽到 **1/2** |
| `WarCivilianThrottle` | 2 | 交战王国其他平民，节流放宽到 1/2 |

实测（960×704 图、6708→7598 单位）：同一段时间内**征兵次数 5 → 182**（约 36 倍），
`找工作放宽` / `战争平民放宽` 计数器持续增长；帧率 42→25 fps（人口增长所致，模拟 25.7ms 里角色只占 10ms）。

觉得还是慢，可以：
- `JobSeekerThrottle` 改成 **1**（缺兵城市的市民完全不节流）；
- `SknThrottleFrames` 从 8 调到 **6**（整体少节流 25%）。
---

## 6. 资源精灵：为什么不能"改写资产数组"

第一版的做法是读档后把缺失的 `ResourceAsset.gameplay_sprites` 重新加载并写回资产。
这会产生**新的 Sprite 实例**，而游戏的动态图集是按 Sprite 实例哈希登记的，结果就是：

```
[getCachedAtlasItemSprite]Dynamic sprite not found: 888748000 w_stick_wood_0
```

每帧刷屏。现在改成**只在读取处兜底、绝不改写资产**：

| 补丁 | 行为 |
|---|---|
| `ResourceAsset.getGameplaySprite()` | 数组为空 → 返回 UI 图标（库存绘制不再 NRE） |
| `ResourceAsset.getSprites()` | 数组为空 → 返回 null（配合"坏装备防护"，该单位直接跳过物品渲染，不查图集） |

实测：`getCachedAtlasItemSprite` / `NullReferenceException` / `getGameplaySprite` 三类报错计数**全部为 0**。
---

## 7. 内置性能增强（不依赖任何外部性能模组）

帧率、渲染与 UI 节流全部由本模组独立实现，**不需要再装其它性能模组**：

| 功能 | 实现要点 | 配置 |
|---|---|---|
| 解锁帧率 | 每秒应用 `targetFrameRate = -1` + `vSyncCount = 0` | `UnlockFrameRate` / `MaxFrameRate` / `UnlockVsync` |
| 高帧率不改游戏速度 | 接管 `MapBox.calculateCurElapsed`，>60fps 按比例补偿 | `FpsIndependentSpeed` |
| FPS 显示 | 左上角 OnGUI 计数（读原版 `FPS.fps`） | `ShowFpsCounter` |
| 图标/粒子跳帧 | 接管 `QuantumSpriteManager.updateSystems`，按资产分级 2–6 帧；**跳帧表预计算成 int[]**，热路径零字符串哈希 | `QuantumSpriteThrottle` / `QuantumStrideMultiplier` |
| 地图层绘制节流 | 接管 `MapBox.updateMapLayers`；**每层跳帧数预计算**，热路径不做类型判断 | `MapLayerThrottle` / `MapLayerStrideZone` / `MapLayerStrideUnit` |
| 权能标签节流 | 每 15 帧重排一次 | `PowersTabThrottle` |
| 暂停冻结投射物 | 暂停时跳过 `ProjectileManager.update` | `PauseProjectiles` |
| 鼠标命中缓存 | 鼠标没动且未过期（≤10 帧）就复用上次结果，省掉每帧 UI 射线检测 | `CachePointerOverUi` |
| 权能按钮栏节流 | 每 10 帧更新 | `PowerButtonsThrottle` |
| 纪元卡片节流 | 每 5 帧刷新 | `WorldAgeUiThrottle` |
| 重复状态跳过 | 溺水等状态已存在时不再重复添加 | `SkipDuplicateStatus` |

启动日志：

```
[SKNRM] 防护补丁安装 8/8（投射物/远程/坏装备/名牌/资源精灵×2/进攻行为/读档钩子）
[SKNRM] 性能增强补丁安装 9/9（帧率/图标跳帧/地图层/标签/投射物/鼠标/按钮栏/纪元卡/重复状态）
[SKNRM] 帧率解锁已应用：targetFrameRate=-1 vSyncCount=0（0 = 不限制）
```

**实现原则**：优先"节流/缓存"而不是重写原版方法——重写等于把原版分支整段搬一遍，
漏一处就掉画面或坏玩法；节流只改变"多久算一次"，跑的仍是原版代码。
---

## 8. 代码来源与原创性

- **全部代码为本模组独立编写**，只通过 Harmony 调用游戏的公开/已公开化 API（`publicized` 程序集），
  不包含任何第三方模组的源代码或反编译产物。
- 实现方式是"节流/缓存"而不是"重写原版方法"：原版方法仍由游戏执行，本模组只决定"多久执行一次"。
  这样既降低风险，也避免把原版代码大段搬进模组。
- 性能参数（各图层的跳帧周期、城市分桶容量、人口阈值等）是本机实测调参的结果。
- 与社区同类模组的对比（逐行文本比对，`Get-Content` 去重后比较）：
  - 与其它性能模组：**完全相同的有效代码行 7 行**（4 行 `using` + 3 行通用语句/API 调用）；
  - 与游戏自身反编译源码：**6 行**（`private void Start()` / `private void Update()` 等平凡签名 + `using`）。
- 结论：不存在代码级复制；功能层面属于"同类优化手段"，任何性能模组都会做类似的事。
---

## 9. 性能效果（实测）

数据取自 960×704 地图、8,832 单位的实测诊断行（`[SKNRM] 诊断`）。

### 9.1 优化项与收益

| # | 优化项 | 作用位置 | 机制 | 实测收益 |
|---|---|---|---|---|
| 1 | 市民 AI 相位节流 | `Actor.b4 / b6_0 / b6` | 人口超阈值后按 actor id 分相位跳过（默认 1/8 帧） | **最大项**：单局 `b6 跳过 524 万`、`b4 174 万`、`b6_0 94 万` |
| 2 | 逻辑 job 分帧 | 17 个纯计算 job | 每 3 帧跑一次；**视觉 job 不跳** | 同一世界：`角色 10.2 → 7.1ms`，fps `30 → 42` |
| 3 | 寻敌优化 | `EnemiesFinder` / `Actor.b3` | 缓存跨 3 帧、相位 1/5、平民与和平王国只扫本 chunk、空闲士兵不寻敌 | `寻敌缩圈 11.8 万`、`寻敌轮转 146 万`、`闲兵不寻敌 77 万`；交战尖峰 53.7 → ~32ms |
| 4 | 军队分级节流 | `Actor.b6` 军队分支 | 战斗中 1/4 帧；队长空闲时 1/8 且不寻敌 | `军节流 110 万` |
| 5 | 城市轮转（UPP） | `City.update` | 邻接分桶轮转，被拒城市只递减计时器 | `城市[允许 105 / 拒绝 1079]` → **约 91% 城市帧被跳过** |
| 6 | 征兵/战争豁免 | `City.update` | 缺战士/有进攻命令/被围城/交战王国不轮转 | `征兵放行 18`、`战争城放行 1672`、`征兵 217` |
| 7 | 地图层绘制节流 | `MapLayer.draw` | 区域层 2 帧、单位层 4 帧 | 绘制本身仅 `0.7–0.9ms`，节流省 **~0.3ms** |
| 8 | 图标/粒子跳帧 | `QuantumSpriteManager.updateSystems` | 库存堆/图标 2–3 帧、领袖/军队 5 帧、箭头 6 帧 | 未单独量化，量级 <1ms |
| 9 | UI 节流 | 权能标签 15 帧 / 按钮栏 10 帧 / 纪元卡 5 帧 / 鼠标命中缓存 | 降低 UI 重排与 UI 射线检测 | 未单独量化，量级 <1ms |
| 10 | GC 择机 | 全局 | 关自动 GC，堆超 1900MB 或暂停时回收一次 | 把 **1–3 秒随机爆卡**变成可控回收（实测 `1951MB → 1581MB`） |
| 11 | 读档宽限 | 全局 | 读档后 12 秒市民 1/16 | 消除“进大存档头几秒卡死” |

### 9.2 实时表现（8,832 单位）

```
平静时：模拟 4.2ms（角色 0.6）  绘制 0.0  名牌 0.0  其它 5.5   → 102 fps
交战时：模拟 53–63ms（角色 25–31）绘制 0.7  名牌 0.5  其它 8–23 → 13–16 fps
GC 尖峰：单帧 323ms（其它 178ms，堆 1864MB 触顶回收）
```

### 9.3 与未优化基线对照

| 场景 | 未优化 | 现在 |
|---|---|---|
| 6,442 单位（无任何节流） | 模拟 **1,123ms** / 角色 **1,108ms** → 0.9–1.6 fps | 同规模下角色 **10–30ms** |
| 6,708 单位（job 分帧前后） | 角色 10.2ms → 30.3 fps | 角色 **7.1ms** → **42.3 fps** |
| 8,832 单位 | — | 平静 102 fps / 交战 13–16 fps |

**量级结论**：AI 侧从“秒级”降到“几十毫秒级”（约 30–100 倍），
但总帧时间仍受渲染/UI/其它系统（`其它`）与 GC（~1.9GB 活堆）限制。

### 9.4 明确未解决

- **内存**：960×704 图活堆约 1.86GB，其中 ~1.5GB 是引擎每格对象；只启用本模组的隔离实验证明与模组无关。
  要显著降低只能换更小的地图，或接受 GC 停顿（本模组负责把停顿挪到暂停/触顶时）。
- **交战瞬间 AI 峰值**：角色 25–31ms 来自“数千战士同时交战”，已压到 1/4 帧，再压会伤战斗手感。
---

## 10. 与「清除 / 重建」类功能的兼容（大规模变更保护）

像天工技艺的“清除”会在**暂停状态**下用协程分批删除成千上万个建筑/单位/城市。
如果此时本模组还在做这两件事，就会表现为“清除卡死/冲突”：

1. **GC 择机**看到“已暂停 + 堆增长”就触发完整回收 —— 1.9GB 堆要 1–3 秒，正好卡在清除过程里；
2. **节流**让城市/角色少思考，清除后的重建明显变慢。

处理方式（与具体模组无关，不 patch 任何第三方代码）：

| 措施 | 说明 |
|---|---|
| 批量变更检测 | 每 0.5 秒采样 单位/建筑/城市 数量；任一 **少了 ≥20% 且 ≥50 个** → 进入宽限窗口 |
| 窗口内不回收 | `BulkGuard.Active` 时 `GC 择机` 直接返回 |
| 窗口内不节流 | 角色与城市按原速运行，重建更快 |
| UPP 桶重置 | 丢弃已被删除的城市引用 |
| 暂停回收加条件 | 只有**暂停超过 3 秒**（玩家主动暂停）才允许择机回收；协程式短暂停一律不回收 |

配置：

```json
"BulkChangeGuard": true,
"BulkChangeSeconds": 5
```

触发时日志会打印：

```
[SKNRM] 检测到大规模变更（单位 6712 → 320）：接下来 5 秒暂停 GC 择机并关闭节流…
```
---

## 11. 与其它模组的兼容性

### 11.1 自动共存检查（1.2.4 起）

本模组大量使用"跳过原版方法"的前缀（节流/分帧）。Harmony 的规则是：**任一前缀返回 false，
原版方法体与所有 postfix 都不执行**。所以如果别的模组在同一个方法上也挂了补丁，
我们的跳过会把它的逻辑一并吃掉。

因此安装"跳过型"补丁前会先查目标方法的 `HarmonyPatchInfo`：
**只要发现非本模组的补丁，就放弃安装该补丁（保留原版行为）**，并在日志里列出：

```
[SKNRM] 共存：跳过型补丁放弃安装（目标已被其它模组占用）：Actor.b6_updateAI AI 思考
[SKNRM] 共存检查：有 4 个补丁点已被其它模组占用，已放弃安装以保护对方功能：…
```

含义：**在大型整合包里，宁可少省一点性能，也不会破坏对方功能。**

### 11.2 实测审计（本机 7 个启用模组）

| 模组 | 补丁数 | 与 SKNRM 重叠 | 说明 |
|---|---|---|---|
| **Re-EarlyMordenWar** | 426 | **10** | 见下表，其中 4 项由共存检查自动让路 |
| **FunBoost** | 96 | 4 | `calculateCurElapsed`、`UiWorldAgeInfo.Update`、`Actor.addStatusEffect`、`EnemiesFinder.findEnemiesFrom` |
| 天工技艺 Supower | 14 | 0 | 无重叠 |
| 新战争艺术 | 11 | 0 | 无重叠 |
| RenderFix | 4 | 0 | 无重叠 |
| ChineseName | 0 | 0 | 无 Harmony 补丁 |
| BetterZoom | 1 | 0 | 无重叠 |

Re-EarlyMordenWar 的 10 个重叠点：

| 方法 | 对方补丁类型 | 处理 |
|---|---|---|
| `Actor.b6_updateAI` | 前缀 | **让路**（否则其逻辑被节流跳过） |
| `Actor.b6_0_updateDecision` | 前缀（Blockhouses） | **让路** |
| `MapBox.updateBuildings` | 前缀（RenderBudget） | **让路** |
| `ActorManager.calculateVisibleActors` | 前缀（RepublicDress） | **让路** |
| `City.makeWarrior` | 前缀+后缀 | 本模组仅计数（后缀），无冲突 |
| `Actor.checkHasRenderedItem` | 后缀 | 双方后缀都执行，无冲突 |
| `CombatActionLibrary.attackRangeAction` | 前缀+后缀 | 仅在"坏投射物"时跳过，正常战斗不受影响 |
| `ProjectileManager.spawn` | 前缀 | 仅在"空投射物 ID"时跳过 |
| `QuantumSpriteManager.updateSystems` | 后缀 | 本项默认已关闭（实测更慢） |
| `UnitWindow.showInfo` | — | 本模组仅加标签（后缀） |

### 11.3 仍然存在的已知交互

- **FunBoost 的视野减半 + 本模组寻敌缩圈**会叠加：平民的实际寻敌半径 = 原版 ÷ 2（FunBoost）再受"只扫本 chunk"（本模组）影响，
  寻敌更省但也更迟钝；如觉得单位反应慢，把 `EnemyPeacefulRangeZero` 或 `EnemyCivilianRangeZero` 关掉。
- **FunBoost 的 `calculateCurElapsed`**：本模组同类补丁会自动让路，游戏速度以 FunBoost 为准（两者公式等价）。
- 若某个模组导致节流全部让路，日志会明确列出，方便定位"为什么优化没生效"。
### 11.4 全部已安装模组的审计（含当前未启用的）

不只针对启用中的模组：只要 `Mods\` 里存在，就可能被你在将来启用。下表是**全部 34 个模组目录**中
与 SKNRM 存在补丁点重叠的（无重叠的 27 个未列出）：

| 模组 | 当前启用 | 补丁数 | 与 SKNRM 重叠的方法 |
|---|---|---|---|
| **Re-EarlyMordenWar** | 是 | 426 | 10 个（见 11.2） |
| **FunBoost** | 是 | 80 | `calculateCurElapsed`、`UiWorldAgeInfo.Update`、`Actor.addStatusEffect`、`EnemiesFinder.findEnemiesFrom` |
| **AncientWarfare3.0** | 否 | 366 | **11 个**：`MapBox.updateObjectAge`、`Actor.b6_updateAI`、`Actor.updateNutritionDecay`、`City.makeWarrior`、`BehCityActorCheckAttack.execute`、`NameplateManager.update`、`EnemiesFinder.findEnemiesFrom`、`Actor.checkHasRenderedItem`、`ActorManager.calculateVisibleActors`、`CityManager.update`、`BatchActors.u1_checkInside` |
| WallBox | 否 | 10 | `BehCityActorCheckAttack.execute` |
| WarProtectionMod | 否 | 12 | `Actor.b3_findEnemyTarget` |
| XaviiNationTypes | 否 | 74 | `City.update` |
| 大和民族 | 否 | 10 | `MapBox.updateSimulation`（本模组仅计时，不跳过） |
| 古典经济学 | 否 | 5 | `MapBox.updateObjectAge`（本模组仅加年度钩子后缀） |

处理方式：**全部由 11.1 的自动共存检查覆盖**——启用其中任何一个时，SKNRM 会在安装阶段
检测到同点补丁并让路，日志里会明确列出被让路的点，不需要你手动配置。

需要人工留意的只有两处（不是冲突，是"叠加"）：

- `EnemiesFinder.findEnemiesFrom`：本模组与 FunBoost / AncientWarfare3.0 都会缩小寻敌范围 → 叠加后更迟钝；
- `City.update`：本模组不跳过方法体（只设 `timer_action`），XaviiNationTypes 的补丁仍会执行，互不影响。