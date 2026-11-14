using System;
using System.IO;
using Newtonsoft.Json;

namespace SKNRM
{
    /// <summary>
    /// 运行期配置。M1 用模组目录下的 sknrm_config.json（NML 设置窗口 API 待确认后再接）。
    /// 字段语义一一对应大日春秋 3.1.6：
    ///   SknPlus / Skn2          = 春秋法则里的 SKN++ / SKN2.0 开关
    ///   Unfair / UnfairAntiDrop = 不公平调度 / 不公平调度防掉压
    ///   SknPopulationThreshold  = 「SKN人口上限」（默认 3000，按年重算闸门）
    /// </summary>
    internal sealed class PerfConfig
    {
        // ── SKN ──
        public bool SknPlus = true;
        public bool Skn2 = false;
        public long SknPopulationThreshold = 400;
        public int SknCityZoneThreshold = 12;
        public bool UseLowResGate = true;
        public bool FreezeNutrition = true;

        // 四条角色侧入口的独立开关（719 拆成了 b4 / b5 / b6_0 / b6 四条 job）。
        // 默认：跳 b4(校验) + b6_0(决策) + b6(AI)；**不跳 b5(路径起跑)**。
        // 原因：719 的路径索引推进在 b5 → updatePathMovement，跳它会让"有路径"的角色卡死在路径上。
        // 想完全复刻 0229「跳 b4+b5+b6 = 冻住不动」，把 SkipPathStart 改 true（代价见 README §5）。
        public bool SkipVerifier = true;
        public bool SkipPathStart = false;
        public bool SkipDecision = true;
        public bool SkipAI = true;

        /// <summary>是否要求「角色有城市」才参与冻结。默认 true（与大日一致）；
        /// 置 false 可把野生单位也一起冻住，同时用于在没有城市的测试局里验证补丁是否真的被调用。</summary>
        public bool SknRequireCity = true;

        /// <summary>硬保护：军队成员 / 战士 / 军队队长 永不冻结（默认开）。</summary>
        /// <summary>普通市民的 AI 节流周期（帧）：0/1 = 硬冻结（最省，但地图会"死"）；N&gt;1 = 每个市民每 N 帧想一次，
        /// 决策速度降为 1/N 但角色仍在走动、仍在干活 —— 用少量帧率换"地图还活着"。默认 6。</summary>
        public int SknThrottleFrames = 8;

        /// <summary>自适应节流（默认开）：实际节流周期 = clamp(人口 / SknPopulationThreshold, 1, SknThrottleFrames)。
        /// 阈值设 400 时：400 人→1（不节流）、800→2、1200→3、1600→4、2000→5、2400+→6。
        /// 好处是"刚过阈值时 AI 几乎不受影响"，人口越多才逐级加重，不会在 800 人的小图上突然冻掉一半决策。</summary>
        public bool SknAdaptiveThrottle = true;

        /// <summary>角色管线（MapBox.updateActors：一帧 29 个 job、10 次全量遍历）的步长上限：
        /// 0=关闭；N=最多每 N 帧才跑一次整条角色管线。默认 3。
        /// 实测 6500 单位时角色管线占 120ms/帧，是真正的瓶颈，靠这个直接按倍数砍。</summary>
        public int SknActorStrideMax = 1;

        /// <summary>逻辑 job 的分帧周期（1=关）。BatchActors 里纯计算 job（b1/b2/b3/b4/b5/b55/b6_0、u1/u4/u5/u6/u7/u8、update_stats、update_hunger）
        /// 每 N 帧才跑一次；视觉类 job（可见性/渲染器/精灵动画/平滑移动/抖动/悬停/授粉）永远每帧跑，保证画面不一顿一顿。</summary>
        public int SknLogicJobStride = 3;

        /// <summary>把"随机出现的大 GC 停顿"变成"可控的定时 GC"：每隔若干秒主动做一次完整回收。
        /// 依据：卡顿探针显示 1–3 秒的单帧停顿发生在堆≈2GB 时的 gen2 回收，而不是 AI。默认开。</summary>

        /// <summary>主动 GC 的间隔秒数（默认 45）。</summary>

        /// <summary>只在像素/小地图视图（整图俯瞰）时做主动 GC —— 那时画面变化最小，停顿最不刺眼。默认开。</summary>

        // ── GC 择机模式（把“战斗中突然卡 3 秒”改成“暂停时回收一次”）──
        // 做法：游玩期间关闭自动 GC（GarbageCollector.GCMode = Disabled），
        // 堆增长超过阈值后，在暂停（或任意时刻，视配置）主动做一次完整回收再继续关闭。
        // 注意：地图本身占用的活内存不会因此变小，只是把停顿挪到不碍事的时机。

        public bool GcDeferMode = true;          // 默认关（想用再开）
        public int GcDeferHeapGrowthMb = 200;     // 堆相对基线增长多少 MB 后回收
        public bool GcDeferOnlyWhenPaused = true;
        public int GcDeferHardCapMb = 1900;   // 堆超过这个数就无条件回收（防 OOM）

        // 阶段 0：读档后输出一次地图内存归因表（只读统计，不改任何状态）

        // 渲染 LOD：名牌不可见（像素视图/缩放太远）时跳过整段名牌更新
        public bool SkipNameplatesWhenHidden = true;

        // 防护：物品/装备的 hand renderer sprites 为 null 时不再抛 NRE（坏资产常见于装备类模组）
        public bool GuardBadItemSprites = true;

        // 资源精灵兜底：资源 gameplay_sprites 缺失时用 UI 图标顶上（避免库存绘制 NRE）
        public bool FixResourceSprites = true;

        // 自动化测试用：启动后自动加载指定存档文件夹（留空=关闭）

        /// <summary>参与分帧的逻辑 job 名单（BatchActors 的方法名，逗号分隔）。</summary>
        public string SknLogicJobs = "updateStats,updateNutritionDecay,u1_checkInside,u4_deadCheck,u5_curTileAction,u6_checkFrozen,u7_checkAugmentationEffects,u8_checkUpdateTimers,b1_checkUnderForce,b2_checkCurrentEnemyTarget,b3_findEnemyTarget,b4_checkTaskVerifier,b5_checkPathMovement,b55_updateNaturalDeaths,b6_0_updateDecision,updateEventsBecomeAdult,updateEventsEggHatched";

        /// <summary>角色管线步长的分母：步长 = 人口 / 这个数（上限 SknActorStrideMax）。默认 1000 → 3000 人时步长 3、4000 人时 4。</summary>
        public int SknActorStrideDivisor = 1000;

        /// <summary>军队保护分级：0=军队/战士/队长全部满速（旧行为）；1=只保护"战斗中/有攻击目标/队长/前线城"的军队（默认）；2=军队也不豁免。</summary>
        public int ArmyProtectMode = 1;

        /// <summary>受保护的军队单位（在战斗/有目标/队长/前线）的节流周期：1=每帧都跑（旧行为），2=每 2 帧跑一次，依此类推。
        /// 大规模会战时这是唯一能削掉"角色耗时尖峰"的旋钮，代价是交战反应变慢。</summary>
        public int ArmyThrottleFrames = 4;

        /// <summary>士兵（军队成员但非队长）在"队长没在打仗"时的节流周期：默认 8，等于行军/待命期间几乎不思考，
        /// 只保留跟随与移动。队长一旦进入战斗，自动回落到 ArmyThrottleFrames（默认 2）。</summary>
        public int ArmyIdleThrottle = 8;

        /// <summary>刚载入世界后的"宽限加速"秒数：这段时间内全局限流加倍（默认 12 秒），避免读大存档时前几帧 2–3 秒卡死。</summary>
        public float LoadGraceSeconds = 12f;

        /// <summary>宽限期间市民的节流周期（默认 16，即 1/16 决策）。</summary>
        public int LoadGraceThrottle = 16;

        /// <summary>禁用的市民职业（逗号分隔，719 CitizenJobAsset id）。被移除的职业不会再被城市指派，等于禁用对应行为。
        /// 默认关掉清洁/粪便清洁/修路——这三个纯粹是"看着有用"的开销。attacker/builder/farmer 等核心职业不在此列。</summary>

        /// <summary>是否启用上面的职业禁用。</summary>

        /// <summary>士兵在队长未参战时完全不寻敌（不做自己的目标扫描），只靠 b2 校验已有目标 + 挨打反应。默认开。</summary>
        public bool ArmyIdleNoScan = true;

        /// <summary>拦截"投射物 ID 为空"的坏装备：跳过投射物生成并记一条日志，避免 Projectile.start 每帧抛异常刷屏。默认开。</summary>
        public bool GuardBadProjectiles = true;

        /// <summary>寻敌缓存跨帧数（1=原版每帧清）。设 2 表示缓存活两帧，寻敌扫描量约减半。</summary>
        public int EnemyCacheFrames = 3;

        /// <summary>简化算法：没在打仗的王国只扫自己所在的 chunk，不再向外扩圈找敌人。</summary>
        public bool EnemyPeacefulRangeZero = true;

        /// <summary>寻敌（b3_findEnemyTarget）的相位轮转周期：按 actor id 打散，让同一帧要扫描的单位摊到 N 帧。1=原版节拍。</summary>
        public int EnemyPhaseFrames = 5;

        /// <summary>战士/军队/队长保持原版寻敌节拍（不参与相位轮转），保证交战反应。</summary>
        public bool EnemyKeepWarriorCadence = true;

        /// <summary>平民（非战士/非军队/非队长）寻敌范围强制为 0：只认自己所在 chunk 的敌人，不再主动发现隔壁 chunk（挨打才反应）。</summary>
        public bool EnemyCivilianRangeZero = true;

        /// <summary>后台不被限速：设 Application.runInBackground = true。切出游戏再切回来时不会因为窗口失去焦点而被降帧（不改变任何帧率上限）。默认开。</summary>
        public bool RunInBackground = true;

        /// <summary>单帧最大步长（秒），0 = 不改。设 0.05~0.1 可避免切回窗口时一次性补帧造成长时间卡顿。默认 0（不改）。</summary>
        public float MaxDeltaTime = 0.05f;

        /// <summary>焦点切换时在日志里记一行，便于区分"切窗口造成的低帧"与"真的卡"。默认开。</summary>


        /// <summary>只对"当前看不见"的角色节流（默认开）：
        ///   · 像素/小地图视图（整图可见）→ 与原来一致，整批节流；
        ///   · 拉近看城市（正常视图）→ **镜头内的角色一律满速**，只节流镜头外的，近景观赏零损失。</summary>
        public bool SknOnlyInvisible = true;

        /// <summary>启用"保活名单"：名单里的 job 永远满速，其余 job（含 job 还没分配 = null 的）都参与节流。
        /// 这是"少算不少看"的关键：random_move / attacker / 动物 保持原速，画面里始终有小人在动。</summary>
        public bool SknJobWhitelist = true;

        /// <summary>保活名单（逗号分隔，719 的 ActorJobLibrary id）：这些 job 不被节流。
        /// 默认放过：随机游走、军队进攻、决策、产卵，以及全部动物/怪物 job。</summary>
        public string SknKeepJobs = "random_move,random_swim,attacker,decision,egg,dragon_job,ufo_job,worm_job,sandspider_job,ant_black,ant_red,ant_blue,ant_green,skeleton_job,cleaner,manure_cleaner,road_builder";

        public bool ProtectArmies = true;

        /// <summary>硬保护：正在打仗的王国里的居民永不冻结（默认开）。</summary>
        public bool ProtectAtWar = true;

        /// <summary>「打仗保护」的范围：0 = 整个交战王国的所有城市/居民都保护（旧行为，最保守但 UPP 几乎失效）；
        /// 1 = 只保护**前线**（邻国在交战）、首都、被围攻、有军队的城市及其居民（默认，推荐）。</summary>
        public int WarProtectScope = 2;

        /// <summary>UPP：有军队 / 正在被围攻 / 首都 / 在打仗的城市不参与轮转节流（默认开）。</summary>
        public bool UppProtectArmyCities = true;

        /// <summary>征兵保护：城市战士名额没满 / 有进攻命令 / 正在被围攻时，不参与 UPP 轮转拒绝（否则征兵会明显变慢）。</summary>
        public bool UppProtectRecruitment = true;

        /// <summary>交战王国的城市不参与 UPP 轮转拒绝（战争期征兵/补兵/调兵最吃城市 AI）。</summary>
        public bool UppProtectWarCities = true;

        /// <summary>交战王国平民的节流周期（比平时的 SknThrottleFrames 更宽松，保证征兵与后勤）。0 或 1 = 不节流。</summary>
        public int WarCivilianThrottle = 2;

        /// <summary>还没分到工作（citizen_job == null）的市民节流周期。这类市民正是征兵/分派的对象，
        /// 而"找工作"本身是一条决策（find_city_job），跑在被节流的 b6 里，所以必须放宽。</summary>
        public int JobSeekerThrottle = 2;

        /// <summary>UPP 饥饿保护：某城连续被拒绝思考超过这么多帧就强制放行一次（0=关闭）。</summary>
        public int UppCityMaxDenyFrames = 20;

        /// <summary>自测模式（默认关）：每秒解除暂停并把游戏速度拉到 5，用于让饥饿/决策/城市闸门等
        /// 需要"世界真的在跑"的路径产出计数器。仅用于验证，正常游玩请保持 false。</summary>

        /// <summary>仅自测：把"像素视图"判定强制成 false，用来验证"镜头内角色满速"这条分支。</summary>
        public bool SelfTestForceNearView = false;

        // ── UPP ──
        public bool Unfair = true;
        public bool UnfairAntiDrop = false;
        public int UppCityAgeMonths = 20;
        public int UppBucketCapacity = 5;
        public int UppGraceFrames = 10;

        // ── 标签（SKN / PI 保护名单）──
        public bool AutoTagImportant = true;
        public bool TagInspectedActors = true;

        // ── 诊断 ──
        public float LogEverySeconds = 30f;

        // 仅供自动化验收：启动后自动加载指定存档文件夹（留空 = 关闭）
        public string AutoLoadSaveFolder = "";
        public float AutoLoadAfterSeconds = 10f;

        // ── 内置性能增强开关 ──
        public bool UnlockFrameRate = true;       // 解锁帧率上限
        public int MaxFrameRate = 0;              // 0 = 不限制（-1）
        public bool UnlockVsync = true;           // 同时关闭垂直同步，否则仍被刷新率限制
        public bool ShowFpsCounter = true;        // 左上角 FPS 显示
        public bool FpsIndependentSpeed = true;   // 高帧率不改变游戏速度
        public bool QuantumSpriteThrottle = true; // 量子精灵分级跳帧（收益最大的一项）
        public bool MapLayerThrottle = true;      // 地图层绘制节流
        public bool PowersTabThrottle = true;     // 权能标签每 15 帧刷新
        public bool PauseProjectiles = true;      // 暂停时冻结投射物
        public bool CachePointerOverUi = true;    // 鼠标没动时复用 UI 命中结果
        public bool PowerButtonsThrottle = true;  // 权能按钮栏每 10 帧更新
        public bool WorldAgeUiThrottle = true;    // 纪元卡片每 5 帧刷新
        public bool SkipDuplicateStatus = true;   // 跳过重复添加的状态（溺水等）

        // 地图层绘制步长（越大越省，但画面会顿）：ZoneCalculator / UnitLayer 分别控制
        public int MapLayerStrideZone = 2;
        public int MapLayerStrideUnit = 4;

        // 图标跳帧倍率：1.0 = 默认表；2.0 = 所有图标类再慢一倍
        public float QuantumStrideMultiplier = 1f;
        public bool Verbose = false;

        private static PerfConfig _instance;
        private static string _path;
        private static string _dir;

        internal static PerfConfig I
        {
            get { return _instance ?? (_instance = new PerfConfig()); }
        }

        internal static string FilePath
        {
            get { return _path; }
        }

        internal static void LoadFrom(string modDir)
        {
            _dir = modDir;
            if (string.IsNullOrEmpty(_dir))
            {
                _instance = new PerfConfig();
                Log.Warn("未取到模组目录，使用内存默认配置");
                return;
            }

            _path = Path.Combine(_dir, "sknrm_config.json");
            try
            {
                if (File.Exists(_path))
                {
                    string json = File.ReadAllText(_path);
                    _instance = JsonConvert.DeserializeObject<PerfConfig>(json) ?? new PerfConfig();
                    Log.Info("配置已读取：" + _path);
                }
                else
                {
                    _instance = new PerfConfig();
                    Save();
                    Log.Info("配置不存在，已写入默认值：" + _path);
                }
            }
            catch (Exception e)
            {
                _instance = new PerfConfig();
                Log.Warn("配置读取失败，改用默认值：" + e.Message);
            }
        }

        internal static void Reload()
        {
            LoadFrom(_dir);
        }

        private static void Save()
        {
            try
            {
                if (string.IsNullOrEmpty(_path)) return;
                File.WriteAllText(_path, JsonConvert.SerializeObject(I, Formatting.Indented));
            }
            catch (Exception e)
            {
                Log.Warn("配置写入失败：" + e.Message);
            }
        }

        public override string ToString()
        {
            return string.Format(
                "SknPlus={0} Skn2={1} 人口阈值={2} 城市zone阈值={3} lowRes闸={4} 饥饿停摆={5} | UPP={6} 防掉压={7} 城龄月={8} 桶容量={9} 宽限帧={10} | 自动PI={11} 查看打标={12} 日志间隔={13}s",
                SknPlus, Skn2, SknPopulationThreshold, SknCityZoneThreshold, UseLowResGate, FreezeNutrition,
                Unfair, UnfairAntiDrop, UppCityAgeMonths, UppBucketCapacity, UppGraceFrames,
                AutoTagImportant, TagInspectedActors, LogEverySeconds);
        }
    }
}
