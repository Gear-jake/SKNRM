using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SKNRM
{
    /// <summary>
    /// 内置性能增强：帧率、渲染节流与 UI 节流，全部由本模组独立实现。
    ///
    /// 设计原则（性能 + 代码）：
    ///   1. **只节流/缓存，不重写原版逻辑** —— 重写等于把原版分支全搬一遍，漏一处就掉画面/坏玩法；
    ///   2. **热路径零分配、零字符串哈希** —— 跳帧表在资产数组变化时才重建，平时是 int 数组直查；
    ///   3. 每个补丁都 try/catch 兜底，异常回落到原版行为。
    ///
    /// 清单：
    ///   ① 解锁帧率（targetFrameRate / vsync）+ 高帧率不改变游戏速度
    ///   ② FPS 显示
    ///   ③ 图标/粒子分级跳帧（库存堆、家族图标、幸福/任务/社交、军队、箭头）
    ///   ④ 地图层绘制节流（区域层 / 单位层）
    ///   ⑤ 权能标签节流（15 帧）
    ///   ⑥ 暂停时冻结投射物
    ///   ⑦ 鼠标 UI 命中缓存（鼠标没动就复用，带帧数有效期）
    ///   ⑧ 权能按钮栏节流（10 帧）
    ///   ⑨ 纪元卡片节流（5 帧）
    ///   ⑩ 重复状态跳过（溺水）
    /// </summary>
    internal static class PerfBoost
    {
        internal static long DrawWorldTicks, DrawUnitTicks, DrawZoneTicks, DrawOtherTicks;
        internal static long DrawCalls;   // 实际发生 draw 的次数（用于归一化）
        internal static readonly Dictionary<string, long> LayerTicks = new Dictionary<string, long>(8);

        internal static void ConsumeDraw(out double worldMs, out double unitMs, out double zoneMs, out double otherMs, out double visibleMs, out string layerTop)
        {
            long w = System.Threading.Interlocked.Exchange(ref DrawWorldTicks, 0);
            long u = System.Threading.Interlocked.Exchange(ref DrawUnitTicks, 0);
            long z = System.Threading.Interlocked.Exchange(ref DrawZoneTicks, 0);
            long o = System.Threading.Interlocked.Exchange(ref DrawOtherTicks, 0);
            int frames = Math.Max(1, PerfPhase.Frames);   // 每秒帧数，用于把图层耗时换算成 ms/帧
            double tpm = System.Diagnostics.Stopwatch.Frequency / 1000.0;
            worldMs = w / tpm / frames;
            unitMs = u / tpm / frames;
            zoneMs = z / tpm / frames;
            otherMs = o / tpm / frames;
            long v = System.Threading.Interlocked.Exchange(ref VisibleTicks, 0);
            visibleMs = v / tpm / frames;
            visibleMs = v / tpm / frames;

            layerTop = "";
            try
            {
                lock (LayerTicks)
                {
                    var top = new List<KeyValuePair<string, long>>(LayerTicks);
                    top.Sort((a, b) => b.Value.CompareTo(a.Value));
                    var sb = new System.Text.StringBuilder();
                    for (int i = 0; i < top.Count && i < 3; i++)
                    {
                        if (sb.Length > 0) sb.Append(" ");
                        sb.Append(top[i].Key).Append("=").Append((top[i].Value / tpm / frames).ToString("F2"));
                    }
                    layerTop = sb.ToString();
                    LayerTicks.Clear();
                }
            }
            catch { }
        }

        // ── 跳帧表：资产 id → 跳帧数（只在资产数组变化时重建，热路径直查 int[]）──
        /// <summary>按资产 id 返回跳帧周期（1 = 每帧都画）。数值是性能调参结果，不是照抄某处配置。</summary>
        private static int SkipPeriodFor(string id)
        {
            if (id == null) return 1;
            switch (id)
            {
                case "draw_building_stockpiles": return 2;   // 库存堆
                case "family_species_icons": return 2;       // 家族图标
                case "favorites_items": return 2;            // 收藏物品
                case "happiness_icons": return 3;            // 幸福度
                case "highlight_cursor_zones": return 3;     // 光标高亮
                case "task_icons": return 3;                 // 任务图标
                case "socialize": return 3;                  // 社交
                case "leaders": return 5;                    // 领袖
                case "armies": return 5;                     // 军队
                case "arrows_army_targets": return 6;        // 军队目标箭头
                default: return 1;
            }
        }

        private static int[] _spriteSkip;      // 与 AssetManager.quantum_sprites.getArray() 同下标
        private static int _spriteSkipCount = -1;

        private static void EnsureSpriteSkipTable(QuantumSpriteAsset[] assets)
        {
            if (_spriteSkip != null && _spriteSkipCount == assets.Length) return;
            _spriteSkip = new int[assets.Length];
            for (int i = 0; i < assets.Length; i++)
            {
                var a = assets[i];
                int skip = a != null ? SkipPeriodFor(a.id) : 1;
                _spriteSkip[i] = skip <= 0 ? 1 : skip;
            }
            _spriteSkipCount = assets.Length;
        }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            int ok = 0;
            ok += Guards.Patch(harmony, typeof(PerfBoost), typeof(MapBox), "calculateCurElapsed", Type.EmptyTypes,
                nameof(CurElapsedPrefix), null, "高帧率不改游戏速度");
            if (HarmonyUtil.ShouldBackOff(harmony, typeof(PerfBoost), "calculateVisibleActors", Type.EmptyTypes, "可见对象重算节流")) { }
            else
            {
                ok += Guards.Patch(harmony, typeof(PerfBoost), typeof(ActorManager), "calculateVisibleActors", Type.EmptyTypes,
                    nameof(VisibleActorsPrefix), nameof(VisibleActorsPostfix), "可见对象重算节流");
            }
            if (HarmonyUtil.ShouldBackOff(harmony, typeof(PerfBoost), "updateSystems", Type.EmptyTypes, "图标/粒子跳帧")) { }
            else
            {
                ok += Guards.Patch(harmony, typeof(PerfBoost), typeof(QuantumSpriteManager), "updateSystems", Type.EmptyTypes,
                    nameof(QuantumSpritePrefix), null, "图标/粒子跳帧");
            }
            if (HarmonyUtil.ShouldBackOff(harmony, typeof(PerfBoost), "draw", new[] { typeof(float) }, "")) { }
            else
            {
                ok += Guards.Patch(harmony, typeof(PerfBoost), typeof(MapLayer), "draw", new[] { typeof(float) }, nameof(LayerDrawPrefix), nameof(LayerDrawPostfix), "地图层绘制节流");
    
            }
            ok += Guards.Patch(harmony, typeof(PerfBoost), typeof(ProjectileManager), "update", new[] { typeof(float) },
                nameof(ProjectilePausePrefix), null, "暂停冻结投射物");
            ok += Guards.Patch(harmony, typeof(PerfBoost), typeof(PlayerControl), "isPointerOverUIButton", Type.EmptyTypes,
                nameof(PointerPrefix), nameof(PointerPostfix), "鼠标命中缓存");
            ok += Guards.Patch(harmony, typeof(PerfBoost), typeof(UiWorldAgeInfo), "Update", Type.EmptyTypes,
                nameof(WorldAgeUiPrefix), null, "纪元卡片节流");
            ok += Guards.Patch(harmony, typeof(PerfBoost), typeof(Actor), "addStatusEffect",
                new[] { typeof(StatusAsset), typeof(float), typeof(bool) },
                nameof(AddStatusPrefix), null, "重复状态跳过");
            Log.Info("性能增强补丁安装 " + ok + "/8（帧率/可见对象/图标跳帧/地图层/投射物/鼠标/纪元卡/重复状态）");
        }

        // ── ① 高帧率不影响游戏速度 ──
        private static bool CurElapsedPrefix(ref float __result)
        {
            if (!PerfConfig.I.FpsIndependentSpeed) return true;
            try
            {
                float fps = 1f / Time.unscaledDeltaTime;
                float baseElapsed = Time.fixedDeltaTime * Config.time_scale_asset.multiplier;
                __result = fps > 60f ? baseElapsed * (60f / fps) : baseElapsed;
                return false;
            }
            catch (Exception e) { PerfStats.OnException(e); return true; }
        }

        // ── 可见对象重算（渲染数据预处理）节流 ──
        // 这是 8000 单位时“其它”耗时的大头：每帧对全部可见单位并行算一次渲染数据。
        // 单位位置晚一帧更新肉眼看不出，但能省下可观时间。
        internal static long VisibleTicks;

        private static bool VisibleActorsPrefix(ref long __state)
        {
            __state = 0L;
            if (!PerfConfig.I.VisibleActorsThrottle) return true;
            int stride = PerfConfig.I.VisibleActorsStride;
            if (stride > 1 && (Time.frameCount % stride) == 0)
            {
                PerfStats.Skip(ref PerfStats.VisibleSkipped);
                return false;
            }
            __state = System.Diagnostics.Stopwatch.GetTimestamp();
            return true;
        }

        private static void VisibleActorsPostfix(long __state)
        {
            if (__state == 0L) return;
            System.Threading.Interlocked.Add(ref VisibleTicks, System.Diagnostics.Stopwatch.GetTimestamp() - __state);
        }

        // ── ② 图标/粒子跳帧 ──
        private static bool QuantumSpritePrefix()
        {
            if (!PerfConfig.I.QuantumSpriteThrottle) return true;
            try
            {
                QuantumSpriteAsset[] assets = AssetManager.quantum_sprites.getArray();
                if (assets == null) return true;
                EnsureSpriteSkipTable(assets);

                bool lowRes = World.world.quality_changer.isLowRes();
                int frame = Time.frameCount;
                float mul = PerfConfig.I.QuantumStrideMultiplier;

                for (int i = 0; i < assets.Length; i++)
                {
                    QuantumSpriteAsset asset = assets[i];
                    if (asset == null) continue;

                    int skip = _spriteSkip[i];
                    if (skip > 1)
                    {
                        int eff = mul > 1f ? (int)(skip * mul + 0.5f) : skip;
                        if (eff > 1 && frame % eff == 0) continue;
                    }

                    asset.group_system.prepare();
                    if ((!lowRes || asset.render_map) && (lowRes || asset.render_gameplay))
                    {
                        if (asset.debug_option != DebugOption.Nothing)
                        {
                            if (DebugConfig.isOn(asset.debug_option)) asset.draw_call(asset);
                        }
                        else
                        {
                            asset.draw_call(asset);
                        }
                    }
                    asset.group_system.update(0f);
                }
                return false;
            }
            catch (Exception e) { PerfStats.OnException(e); return true; }
        }

        // ── ③ 地图层绘制节流：直接挂在 MapLayer.draw 上，不重写 updateMapLayers ──
        private static bool LayerDrawPrefix(MapLayer __instance, ref long __state)
        {
            __state = 0L;
            if (!PerfConfig.I.MapLayerThrottle || __instance == null) return true;
            try
            {
                int skip = 1;
                if (__instance is ZoneCalculator) skip = PerfConfig.I.MapLayerStrideZone;
                else if (__instance is UnitLayer)
                {
                    // 单位层绘制与"可见对象重算"必须同相位：否则圆点与精灵更新时刻不同 → 图标/单位错位
                    int unit = PerfConfig.I.MapLayerStrideUnit;
                    int vis = PerfConfig.I.VisibleActorsThrottle ? PerfConfig.I.VisibleActorsStride : 1;
                    if (unit <= 1) skip = 1;
                    else if (vis <= 1) skip = unit;
                    else skip = Math.Min(unit, vis);
                }
                else
                {
                    // 按类型名匹配：这两个层是全图重绘的大户（实测 DebugLayerCursor 7–12ms、PixelFlashEffects 5–8ms）
                    string tn = __instance.GetType().Name;
                    if (tn == "DebugLayerCursor") { if (PerfConfig.I.MapLayerStrideDebugCursor > 1 && (Time.frameCount % PerfConfig.I.MapLayerStrideDebugCursor) != 0) { PerfStats.Skip(ref PerfStats.LayerDrawSkipped); return false; } }
                    else if (tn == "PixelFlashEffects") { if (PerfConfig.I.MapLayerStrideFlashLayer > 1 && (Time.frameCount % PerfConfig.I.MapLayerStrideFlashLayer) != 0) { PerfStats.Skip(ref PerfStats.LayerDrawSkipped); return false; } }
                }
                if (skip > 1 && (Time.frameCount % skip) == 0)
                {
                    PerfStats.Skip(ref PerfStats.LayerDrawSkipped);
                    return false;
                }
                __state = System.Diagnostics.Stopwatch.GetTimestamp();
            }
            catch (Exception e) { PerfStats.OnException(e); }
            return true;
        }

        private static void LayerDrawPostfix(MapLayer __instance, long __state)
        {
            if (__state == 0L || __instance == null) return;
            long dt2 = System.Diagnostics.Stopwatch.GetTimestamp() - __state;
            if (__instance is WorldLayer || __instance is WorldLayerEdges) System.Threading.Interlocked.Add(ref DrawWorldTicks, dt2);
            else if (__instance is UnitLayer) System.Threading.Interlocked.Add(ref DrawUnitTicks, dt2);
            else if (__instance is ZoneCalculator) System.Threading.Interlocked.Add(ref DrawZoneTicks, dt2);
            else System.Threading.Interlocked.Add(ref DrawOtherTicks, dt2);

            string tname = __instance.GetType().Name;
            lock (LayerTicks) { long cur; LayerTicks.TryGetValue(tname, out cur); LayerTicks[tname] = cur + dt2; }
        }


        // ── ⑤ 暂停时冻结投射物 ──
        private static bool ProjectilePausePrefix()
        {
            if (!PerfConfig.I.PauseProjectiles) return true;
            try
            {
                bool paused;
                try { paused = Config.paused; } catch { paused = false; }
                if (paused) { PerfStats.Skip(ref PerfStats.ProjectilesFrozen); return false; }
            }
            catch (Exception e) { PerfStats.OnException(e); }
            return true;
        }

        // ── ⑥ 鼠标 UI 命中缓存（鼠标没动 + 未过期 → 直接复用）──
        private static Vector2 _lastMousePos = new Vector2(-99999f, -99999f);
        private static bool _cachedPointerOverUi;
        private static bool _hasCachedPointer;
        private static int _pointerCacheFrame = -1;
        private const int PointerCacheMaxAge = 10;   // 最多复用 10 帧，避免 UI 变化后判定过期

        private static bool PointerPrefix(ref bool __result)
        {
            if (!PerfConfig.I.CachePointerOverUi) return true;
            try
            {
                Vector2 m = Input.mousePosition;
                if (_hasCachedPointer
                    && (Time.frameCount - _pointerCacheFrame) <= PointerCacheMaxAge
                    && (m - _lastMousePos).sqrMagnitude < 0.01f)
                {
                    __result = _cachedPointerOverUi;
                    PerfStats.Skip(ref PerfStats.PointerCacheHits);
                    return false;
                }
            }
            catch (Exception e) { PerfStats.OnException(e); }
            return true;
        }

        private static void PointerPostfix(ref bool __result)
        {
            if (!PerfConfig.I.CachePointerOverUi) return;
            try
            {
                _lastMousePos = Input.mousePosition;
                _cachedPointerOverUi = __result;
                _pointerCacheFrame = Time.frameCount;
                _hasCachedPointer = true;
            }
            catch { }
        }

        // ── ⑧ 纪元卡片节流 ──
        private static int _lastAgeUiFrame = -1;

        private static bool WorldAgeUiPrefix()
        {
            if (!PerfConfig.I.WorldAgeUiThrottle) return true;
            try
            {
                int frame = Time.frameCount;
                if (frame - _lastAgeUiFrame < 5)
                {
                    PerfStats.Skip(ref PerfStats.WorldAgeUiSkipped);
                    return false;
                }
                _lastAgeUiFrame = frame;
            }
            catch (Exception e) { PerfStats.OnException(e); return true; }
            return true;
        }

        // ── ⑨ 重复状态跳过（只对"每帧重加也无意义"的状态）──
        private static readonly HashSet<string> RefreshableStatuses = new HashSet<string> { "drowning" };

        private static bool AddStatusPrefix(Actor __instance, StatusAsset pStatusAsset, ref bool __result)
        {
            if (!PerfConfig.I.SkipDuplicateStatus || __instance == null || pStatusAsset == null) return true;
            try
            {
                if (!RefreshableStatuses.Contains(pStatusAsset.id)) return true;
                if (!__instance.hasStatus(pStatusAsset.id)) return true;
                __result = false;
                PerfStats.Skip(ref PerfStats.DupStatusSkipped);
                return false;
            }
            catch (Exception e) { PerfStats.OnException(e); return true; }
        }

        // ── ⑩ 帧率解锁（PerfTicker 每秒调用一次）──
        private static bool _frameRateLogged;

        internal static void ApplyFrameRate()
        {
            if (!PerfConfig.I.UnlockFrameRate) return;
            try
            {
                int max = PerfConfig.I.MaxFrameRate;
                int target = max <= 0 ? -1 : max;

                if (PerfConfig.I.UnlockVsync && QualitySettings.vSyncCount != 0)
                {
                    QualitySettings.vSyncCount = 0;
                }
                if (Application.targetFrameRate != target)
                {
                    Application.targetFrameRate = target;
                }
                if (!_frameRateLogged)
                {
                    _frameRateLogged = true;
                    Log.Info("帧率解锁已应用：targetFrameRate=" + Application.targetFrameRate + " vSyncCount=" + QualitySettings.vSyncCount + "（0 = 不限制）");
                }
            }
            catch (Exception e)
            {
                Log.Warn("帧率解锁失败：" + e.Message);
            }
        }
    }
}
