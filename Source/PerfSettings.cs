using NeoModLoader.api;
using NeoModLoader.General;

namespace SKNRM
{
    /// <summary>
    /// NML 1.2 模组设置页（F7 打开）。开关状态由 NML 持久化到 mods_config，
    /// 数值项（人口阈值、步长、倍率等）仍走 sknrm_config.json。
    /// </summary>
    internal static class PerfSettings
    {
        private const string Group = "sknrm";

        private static ModConfigItem _sknPlus, _skn2, _freezeNutrition, _useLowResGate;
        private static ModConfigItem _skipPathStart, _skipDecision, _unfair, _antiDrop, _verbose;
        private static ModConfigItem _unlockFrameRate, _displayFps, _fpsIndependentSpeed;
        private static ModConfigItem _quantumThrottle, _mapLayerThrottle;
        private static ModConfigItem _guardProjectiles, _protectArmies, _uppRecruitment, _bulkGuard;
        private static ModConfigItem _pauseProjectiles, _cachePointerOverUi, _worldAgeUiThrottle, _skipDuplicateStatus, _visibleActorsThrottle;
        internal static bool Ready { get; private set; }

        internal static void Init(ModConfig config)
        {
            try
            {
                if (config == null)
                {
                    Log.Warn("GetConfig() 返回 null，跳过设置页注册（开关只能用 sknrm_config.json）");
                    return;
                }

                var c = PerfConfig.I;
                try { config.CreateGroup(Group); } catch { /* 已存在（NML 从磁盘恢复）时忽略 */ }

                _sknPlus = GetOrAdd(config, "skn_plus", ConfigItemType.SWITCH, c.SknPlus);
                _skn2 = GetOrAdd(config, "skn2", ConfigItemType.SWITCH, c.Skn2);
                _freezeNutrition = GetOrAdd(config, "freeze_nutrition", ConfigItemType.SWITCH, c.FreezeNutrition);
                _useLowResGate = GetOrAdd(config, "use_lowres_gate", ConfigItemType.SWITCH, c.UseLowResGate);
                _skipPathStart = GetOrAdd(config, "skip_path_start", ConfigItemType.SWITCH, c.SkipPathStart);
                _skipDecision = GetOrAdd(config, "skip_decision", ConfigItemType.SWITCH, c.SkipDecision);
                _unfair = GetOrAdd(config, "unfair", ConfigItemType.SWITCH, c.Unfair);
                _antiDrop = GetOrAdd(config, "unfair_anti_drop", ConfigItemType.SWITCH, c.UnfairAntiDrop);

                _unlockFrameRate = GetOrAdd(config, "unlock_frame_rate", ConfigItemType.SWITCH, c.UnlockFrameRate);
                _displayFps = GetOrAdd(config, "display_fps", ConfigItemType.SWITCH, c.ShowFpsCounter);
                _fpsIndependentSpeed = GetOrAdd(config, "fps_independent_speed", ConfigItemType.SWITCH, c.FpsIndependentSpeed);
                _quantumThrottle = GetOrAdd(config, "quantum_sprite_throttle", ConfigItemType.SWITCH, c.QuantumSpriteThrottle);
                _mapLayerThrottle = GetOrAdd(config, "map_layer_throttle", ConfigItemType.SWITCH, c.MapLayerThrottle);
                _visibleActorsThrottle = GetOrAdd(config, "visible_actors_throttle", ConfigItemType.SWITCH, c.VisibleActorsThrottle);
                _guardProjectiles = GetOrAdd(config, "guard_projectiles", ConfigItemType.SWITCH, c.GuardBadProjectiles);
                _protectArmies = GetOrAdd(config, "protect_armies", ConfigItemType.SWITCH, c.ProtectArmies);
                _uppRecruitment = GetOrAdd(config, "upp_protect_recruitment", ConfigItemType.SWITCH, c.UppProtectRecruitment);
                _bulkGuard = GetOrAdd(config, "bulk_change_guard", ConfigItemType.SWITCH, c.BulkChangeGuard);
                _pauseProjectiles = GetOrAdd(config, "pause_projectiles", ConfigItemType.SWITCH, c.PauseProjectiles);
                _cachePointerOverUi = GetOrAdd(config, "cache_pointer_ui", ConfigItemType.SWITCH, c.CachePointerOverUi);
                _worldAgeUiThrottle = GetOrAdd(config, "world_age_ui_throttle", ConfigItemType.SWITCH, c.WorldAgeUiThrottle);
                _skipDuplicateStatus = GetOrAdd(config, "skip_duplicate_status", ConfigItemType.SWITCH, c.SkipDuplicateStatus);

                _verbose = GetOrAdd(config, "verbose", ConfigItemType.SWITCH, c.Verbose);

                LM.AddToCurrentLocale(Group, "SKNRM 性能设置");
                LM.AddToCurrentLocale("skn_plus", "平民 AI 跳过（SKN++）");
                LM.AddToCurrentLocale("skn_plus Description", "人口超过阈值时按相位跳过普通市民的 AI 思考/决策/任务校验；军队、战士、船、带标签单位不受影响。");
                LM.AddToCurrentLocale("skn2", "主循环分片（SKN2.0）");
                LM.AddToCurrentLocale("skn2 Description", "把城市与建筑降到 25% 帧、角色管线降到 50% 帧。需与上一项同时开启。");
                LM.AddToCurrentLocale("freeze_nutrition", "饥饿停摆");
                LM.AddToCurrentLocale("freeze_nutrition Description", "节流生效时保持单位营养，避免被冻结的市民饿死。");
                LM.AddToCurrentLocale("use_lowres_gate", "只在像素视图生效");
                LM.AddToCurrentLocale("use_lowres_gate Description", "开=相机拉到最远时才省算力；关=任何时候都生效。");
                LM.AddToCurrentLocale("skip_path_start", "同时跳过路径起跑");
                LM.AddToCurrentLocale("skip_path_start Description", "更接近 0.22.9 原版语义，但 719 的路径索引在 b5 推进，可能导致角色卡在路径上（默认关）。");
                LM.AddToCurrentLocale("skip_decision", "跳过决策阶段");
                LM.AddToCurrentLocale("skip_decision Description", "跳过 b6_0 决策，再省一趟。");
                LM.AddToCurrentLocale("unfair", "城市轮转（UPP）");
                LM.AddToCurrentLocale("unfair Description", "城市按相邻成簇分桶，每帧只让当前桶及邻城思考。");
                LM.AddToCurrentLocale("unfair_anti_drop", "轮转不补宽限");
                LM.AddToCurrentLocale("unfair_anti_drop Description", "开=完全不补宽限，更省但风险高。");

                LM.AddToCurrentLocale("unlock_frame_rate", "解锁帧率上限");
                LM.AddToCurrentLocale("unlock_frame_rate Description", "每秒应用 targetFrameRate=-1 并关闭垂直同步；具体上限见 sknrm_config.json 的 MaxFrameRate（0=不限）。");
                LM.AddToCurrentLocale("display_fps", "显示 FPS");
                LM.AddToCurrentLocale("display_fps Description", "左上角显示帧率（帧率显示）。");
                LM.AddToCurrentLocale("fps_independent_speed", "高帧率不改变游戏速度");
                LM.AddToCurrentLocale("fps_independent_speed Description", "接管 calculateCurElapsed，60fps 以上按比例补偿，避免解锁帧率后游戏变快。");
                LM.AddToCurrentLocale("quantum_sprite_throttle", "图标/粒子节流");
                LM.AddToCurrentLocale("quantum_sprite_throttle Description", "库存堆、家族图标、幸福/任务/社交图标、军队与箭头按 2–6 帧分级刷新。");
                LM.AddToCurrentLocale("map_layer_throttle", "地图层绘制节流");
                LM.AddToCurrentLocale("map_layer_throttle Description", "区域层每 2 帧、单位层每 4 帧绘制一次（步长可在配置文件里改）。");
                LM.AddToCurrentLocale("visible_actors_throttle", "可见对象重算节流");
                LM.AddToCurrentLocale("visible_actors_throttle Description", "每 2 帧算一次可见单位的渲染数据（实测 +6fps）。");
                LM.AddToCurrentLocale("guard_projectiles", "投射物防护");
                LM.AddToCurrentLocale("guard_projectiles Description", "拦下空投射物 ID / 坏投射物资产，避免 ArgumentNullException。");
                LM.AddToCurrentLocale("protect_armies", "军队保护");
                LM.AddToCurrentLocale("protect_armies Description", "军队单位不受市民级节流影响（队长与战斗中的单位始终满速）。");
                LM.AddToCurrentLocale("upp_protect_recruitment", "缺兵城市不轮转");
                LM.AddToCurrentLocale("upp_protect_recruitment Description", "缺战士的城市不参与城市轮转，保证征兵不被拖慢。");
                LM.AddToCurrentLocale("bulk_change_guard", "大规模变更保护");
                LM.AddToCurrentLocale("bulk_change_guard Description", "清除/重建期间暂停 GC 择机并关闭节流。");
                LM.AddToCurrentLocale("pause_projectiles", "暂停时冻结投射物");
                LM.AddToCurrentLocale("pause_projectiles Description", "游戏暂停时不再更新投射物（真正的暂停）。");
                LM.AddToCurrentLocale("cache_pointer_ui", "鼠标 UI 命中缓存");
                LM.AddToCurrentLocale("cache_pointer_ui Description", "鼠标不动时复用上一帧的 UI 命中结果，省掉每帧的 UI 射线检测。");
                LM.AddToCurrentLocale("world_age_ui_throttle", "纪元卡片节流");
                LM.AddToCurrentLocale("world_age_ui_throttle Description", "纪元/年份卡片每 5 帧刷新一次。");
                LM.AddToCurrentLocale("skip_duplicate_status", "重复状态跳过");
                LM.AddToCurrentLocale("skip_duplicate_status Description", "溺水等状态已存在时不再重复添加（原版每帧重加）。");
                LM.AddToCurrentLocale("verbose", "详细日志");
                LM.AddToCurrentLocale("verbose Description", "逐条打印补丁安装与打标结果。");

                Ready = true;
                Log.Info("NML 设置页已注册（分组 sknrm，17 个开关）；数值项仍走 sknrm_config.json");
            }
            catch (System.Exception e)
            {
                Log.Warn("设置页注册失败（不影响性能补丁）：" + e.GetType().Name + ": " + e.Message);
            }
        }

        /// <summary>
        /// 取已有配置项，没有才创建。
        /// NML 启动时会从 mods_config 里恢复上次保存的开关，如果我们再无条件 AddConfigItem，
        /// 就会触发 "already exists ... Overwriting"，把用户上一局的设置覆盖回默认值，
        /// 并且可能让 ModConfig 内部状态不一致（打开设置窗口时 NRE）。
        /// </summary>
        private static ModConfigItem GetOrAdd(ModConfig config, string id, ConfigItemType type, object defaultValue)
        {
            try
            {
                var getter = config.GetType().GetMethod("get_Item", new[] { typeof(string) });
                var dict = getter != null ? getter.Invoke(config, new object[] { Group }) as System.Collections.IDictionary : null;
                if (dict != null && dict.Contains(id))
                {
                    var existing = dict[id] as ModConfigItem;
                    if (existing != null) return existing;
                }
            }
            catch { }
            return config.AddConfigItem(Group, id, type, defaultValue);
        }
        /// <summary>每秒把 NML 设置页里的开关同步到运行期配置（NML 配置项没有回调，轮询最省事）。</summary>
        internal static void SyncFromConfig()
        {
            if (!Ready) return;
            try
            {
                var c = PerfConfig.I;
                c.SknPlus = _sknPlus.BoolVal;
                c.Skn2 = _skn2.BoolVal;
                c.FreezeNutrition = _freezeNutrition.BoolVal;
                c.UseLowResGate = _useLowResGate.BoolVal;
                c.SkipPathStart = _skipPathStart.BoolVal;
                c.SkipDecision = _skipDecision.BoolVal;
                c.Unfair = _unfair.BoolVal;
                c.UnfairAntiDrop = _antiDrop.BoolVal;

                c.UnlockFrameRate = _unlockFrameRate.BoolVal;
                c.ShowFpsCounter = _displayFps.BoolVal;
                c.FpsIndependentSpeed = _fpsIndependentSpeed.BoolVal;
                c.QuantumSpriteThrottle = _quantumThrottle.BoolVal;
                c.MapLayerThrottle = _mapLayerThrottle.BoolVal;
                c.VisibleActorsThrottle = _visibleActorsThrottle.BoolVal;
                c.GuardBadProjectiles = _guardProjectiles.BoolVal;
                c.ProtectArmies = _protectArmies.BoolVal;
                c.UppProtectRecruitment = _uppRecruitment.BoolVal;
                c.BulkChangeGuard = _bulkGuard.BoolVal;
                c.PauseProjectiles = _pauseProjectiles.BoolVal;
                c.CachePointerOverUi = _cachePointerOverUi.BoolVal;
                c.WorldAgeUiThrottle = _worldAgeUiThrottle.BoolVal;
                c.SkipDuplicateStatus = _skipDuplicateStatus.BoolVal;

                c.Verbose = _verbose.BoolVal;
            }
            catch (System.Exception e)
            {
                PerfStats.OnException(e);
            }
        }
    }
}
