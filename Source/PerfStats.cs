using System;
using System.Reflection;
using HarmonyLib;
using System.Threading;
using UnityEngine;

namespace SKNRM
{
    /// <summary>
    /// 运行期计数器。719 的 Post 阶段按批次并行，计数一律走 Interlocked；
    /// 计数器按引用传入 Skip(...)，避免热路径装箱。
    /// </summary>
    internal static class PerfStats
    {
        internal static long AiSkipped;         // AI 层节流跳过次数（AiSystemActor.update）
        internal static long FrozenVerifier;    // b4 任务校验被跳过
        internal static long FrozenPath;        // b5 路径起跑被跳过（默认关）
        internal static long FrozenDecision;    // b6_0 决策被跳过
        internal static long FrozenAI;          // b6 AI 思考被跳过
        internal static long NutritionSkipped;  // 饥饿更新被跳过
        internal static long ThinkDenied;       // 城市本帧被拒绝思考
        internal static long ThinkAllowed;      // 城市本帧被允许思考
        internal static long Sliced;            // 主循环分片跳过次数
        internal static long UppTicks;          // CityManager.update 调用次数
        internal static long CityUpdateCalls;   // City.update 前缀进入次数
        internal static long ViewProtected;     // 镜头内被保护
        internal static long JobProtected;      // job 不在节流名单被保护
        internal static long ThrottleAllowed;   // 轮到这一帧思考
        internal static long ArmyThrottled;     // 军队单位被相位节流跳过
        internal static long ProtectedArmy;     // 军队相关被保护
        internal static long ProtectedWar;      // 王国在打仗被保护
        internal static long WarriorsMade;      // 征兵成功次数
        internal static long VisibleSkipped;     // 可见对象重算被跳过
        internal static long LayerDrawSkipped;   // 地图层绘制被跳过的次数
        internal static long PointerCacheHits;  // 鼠标命中缓存复用次数
        internal static long PowerButtonsSkipped;
        internal static long WorldAgeUiSkipped;
        internal static long DupStatusSkipped;
        internal static long PowersTabSkipped;  // 权能标签被节流
        internal static long ProjectilesFrozen; // 暂停时被冻结的投射物更新
        internal static long JobSeekerBoost;
        internal static long WarCivilianBoost; // 战争期平民被放宽的次数  // 找工作市民被放宽的次数
        internal static long UppWarAllowed;  // 交战王国城市被放行次数
        internal static long UppRecruit;        // 征兵保护放行的城市更新
        internal static long UppProtected;      // 城市保护规则放行
        internal static long NameplateSkipped;  // 名牌更新被 LOD 跳过
        internal static long LogicJobSkipped;   // 逻辑 job 被分帧跳过
        internal static long LoadGraceSkips;    // 载入宽限期内加严节流
        internal static long EnemyIdleSoldierSkipped;
        internal static long EnemyPhased;
        internal static long EnemyRangeShrunk;
        internal static long UppStarved;        // 城市饥饿保护放行
        internal static long TagAdded;
        internal static long Exceptions;
        internal static float LastExceptionTime;

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal static void Skip(ref long counter)
        {
            Interlocked.Increment(ref counter);
        }

        internal static string Snapshot()
        {
            return "跳过[AI=" + Interlocked.Read(ref AiSkipped) + " b4=" + Interlocked.Read(ref FrozenVerifier)
                   + " b5=" + Interlocked.Read(ref FrozenPath)
                   + " b6_0=" + Interlocked.Read(ref FrozenDecision)
                   + " b6=" + Interlocked.Read(ref FrozenAI)
                   + " 饥饿=" + Interlocked.Read(ref NutritionSkipped)
                   + " 分片=" + Interlocked.Read(ref Sliced)
                   + "] 城市[允许=" + Interlocked.Read(ref ThinkAllowed)
                   + " 拒绝=" + Interlocked.Read(ref ThinkDenied)
                   + "] 城更新=" + Interlocked.Read(ref CityUpdateCalls)
                   + " 视野内=" + Interlocked.Read(ref ViewProtected)
                   + " 非白名单=" + Interlocked.Read(ref JobProtected)
                   + " 轮到思考=" + Interlocked.Read(ref ThrottleAllowed)
                   + " 寻敌缩圈=" + Interlocked.Read(ref EnemyRangeShrunk)
                   + " 寻敌轮转=" + Interlocked.Read(ref EnemyPhased)
                   + " 闲兵不寻敌=" + Interlocked.Read(ref EnemyIdleSoldierSkipped)
                   + " 宽限=" + Interlocked.Read(ref LoadGraceSkips)
                   + " 逻辑job跳过=" + Interlocked.Read(ref LogicJobSkipped)
                   + " 名牌跳过=" + Interlocked.Read(ref NameplateSkipped)
                   + " 军节流=" + Interlocked.Read(ref ArmyThrottled)
                   + " 征兵=" + Interlocked.Read(ref WarriorsMade)
                   + " 征兵放行=" + Interlocked.Read(ref UppRecruit) + " 战争城放行=" + Interlocked.Read(ref UppWarAllowed) + " 找工作放宽=" + Interlocked.Read(ref JobSeekerBoost) + " 图层跳过=" + Interlocked.Read(ref LayerDrawSkipped) + " 鼠标缓存=" + Interlocked.Read(ref PointerCacheHits) + " 按钮栏跳过=" + Interlocked.Read(ref PowerButtonsSkipped) + " 纪元卡跳过=" + Interlocked.Read(ref WorldAgeUiSkipped) + " 重复状态=" + Interlocked.Read(ref DupStatusSkipped) + " 标签跳过=" + Interlocked.Read(ref PowersTabSkipped) + " 战争平民放宽=" + Interlocked.Read(ref WarCivilianBoost)
                   + " 保护[军=" + Interlocked.Read(ref ProtectedArmy)
                   + " 战=" + Interlocked.Read(ref ProtectedWar)
                   + " 城=" + Interlocked.Read(ref UppProtected)
                   + " 饿急=" + Interlocked.Read(ref UppStarved)
                   + "] 标签=" + Interlocked.Read(ref TagAdded)
                   + " 异常=" + Interlocked.Read(ref Exceptions);
        }

        internal static void OnException(Exception e)
        {
            Interlocked.Increment(ref Exceptions);
            if (Time.unscaledTime - LastExceptionTime < 10f) return;
            LastExceptionTime = Time.unscaledTime;
            Log.Warn("补丁内部异常（已吞掉，游戏继续）：" + e.GetType().Name + ": " + e.Message);
        }
    }

    /// <summary>
    /// 诊断 ticker：每秒采样帧时间与分段耗时，每 LogEverySeconds 打一行诊断；
    /// 另外负责载入宽限、GC 择机（带硬上限）、堆基线、卡顿探针。
    /// </summary>
    internal sealed class PerfTicker : MonoBehaviour
    {
        private float _accum, _timer, _worst, _logTimer;
        private int _frames;
        private float _lastSecondAvg, _lastSecondWorst;
        private double _simMs, _actorsMs, _drawMs, _plateMs;
        private double _drawWorldMs, _drawUnitMs, _drawZoneMs, _drawOtherMs;
        private long _gcHeapMb;
        private int _gc0Last, _gc2Last, _gc0Delta, _gc2Delta;
        private bool _skipNextSample;

        private float _hitchCooldown;
        private int _hitchLastG0, _hitchLastG2;

        private bool _gcDeferActive;
        private float _pausedSince = -1f;
        private long _gcDeferBaseMb;

        private static bool _heapBaselineLogged;
        private bool _autoLoadDone;
        private float _autoLoadTimer;

        private void Start()
        {
            try
            {
                if (PerfConfig.I.RunInBackground)
                {
                    Application.runInBackground = true;
                }
                if (PerfConfig.I.MaxDeltaTime > 0f)
                {
                    Time.maximumDeltaTime = PerfConfig.I.MaxDeltaTime;
                }
            }
            catch (Exception e)
            {
                Log.Warn("应用后台/步长设置失败：" + e.Message);
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus) { _skipNextSample = true; _accum = 0f; _frames = 0; _worst = 0f; _timer = 0f; }
        }

        private void Update()
        {
            try
            {
                float dt = Time.unscaledDeltaTime;
                PerfPhase.NoteFrame();   // 分段耗时的分母，必须每帧计一次
                HitchProbe(dt);
                GcDeferTick();
                HeapBaselineOnce();
                AutoLoadTick(dt);
                BulkGuard.Tick(dt);
                _fpsTimer += dt;
                PerfSettings.SyncFromConfig();
                if (_fpsTimer >= 1f) { _fpsTimer = 0f; PerfBoost.ApplyFrameRate(); }

                _accum += dt;
                _frames++;
                if (dt > _worst) _worst = dt;
                _timer += dt;
                if (_timer >= 1f)
                {
                    if (_skipNextSample)
                    {
                        _skipNextSample = false;
                    }
                    else
                    {
                        _lastSecondAvg = _accum / Mathf.Max(1, _frames);
                        _lastSecondWorst = _worst;
                        PerfPhase.Consume(out _simMs, out _actorsMs, out _drawMs, out _plateMs);
                        PerfBoost.ConsumeDraw(out _drawWorldMs, out _drawUnitMs, out _drawZoneMs, out _drawOtherMs);
                        EnemyOpt.ConsumeSecond(out _, out _);
                    }
                    _accum = 0f; _frames = 0; _worst = 0f; _timer = 0f;
                }

                int c0 = GC.CollectionCount(0), c2 = GC.CollectionCount(2);
                _gc0Delta = c0 - _gc0Last;
                _gc2Delta = c2 - _gc2Last;
                _gc0Last = c0; _gc2Last = c2;
                _gcHeapMb = GC.GetTotalMemory(false) / (1024L * 1024L);

                _logTimer += dt;
                float interval = PerfConfig.I.LogEverySeconds;
                if (interval > 0f && _logTimer >= interval)
                {
                    _logTimer = 0f;
                    LogPeriodic();
                }
            }
            catch (Exception e)
            {
                PerfStats.OnException(e);
            }
        }


        private void AutoLoadTick(float dt)
        {
            if (_autoLoadDone) return;
            var cfg = PerfConfig.I;
            if (string.IsNullOrEmpty(cfg.AutoLoadSaveFolder)) return;
            _autoLoadTimer += dt;
            if (_autoLoadTimer < cfg.AutoLoadAfterSeconds) return;
            _autoLoadDone = true;
            try
            {
                Log.Info("自动验收：加载存档 " + cfg.AutoLoadSaveFolder);
                World.world.save_manager.loadWorld(cfg.AutoLoadSaveFolder);
            }
            catch (Exception e) { Log.Warn("自动读档失败：" + e.Message); }
        }

        private float _fpsTimer;
        private GUIStyle _fpsStyle;

        /// <summary>左上角 FPS 显示（左上角常驻）。</summary>
        private void OnGUI()
        {
            try
            {
                if (!PerfConfig.I.ShowFpsCounter) return;
                if (_fpsStyle == null)
                {
                    _fpsStyle = new GUIStyle(GUI.skin.label);
                    _fpsStyle.fontSize = 16;
                    _fpsStyle.normal.textColor = Color.white;
                }
                string text = "FPS: " + FPS.fps;
                var rect = new Rect(6f, 4f, 140f, 24f);
                GUI.color = new Color(0f, 0f, 0f, 0.45f);
                GUI.DrawTexture(rect, Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(rect, "  " + text, _fpsStyle);
            }
            catch { }
        }

        private void LogPeriodic()
        {
            float avg = _lastSecondAvg;
            string fps = avg > 0f ? (1f / avg).ToString("F1") : "n/a";
            Log.Info(
                $"诊断：最近1秒均帧 {avg * 1000f:F1}ms（≈{fps} fps，峰值 {_lastSecondWorst * 1000f:F1}ms）| "
                + $"闸门={(SknFeature.Enabled ? "开" : "关")} 单位={SknFeature.UnitsAtGate} 节流={SknFeature.EffectiveThrottleFrames()} lowRes视图={SafeLowRes()} | "
                + $"耗时[模拟={_simMs:F1}ms 其中角色={_actorsMs:F1}ms 绘制={_drawMs:F1}ms 名牌={_plateMs:F1}ms 其它={(avg * 1000.0 - _simMs - _drawMs - _plateMs):F1}ms] | "
                + $"图层[世界={_drawWorldMs:F1} 单位={_drawUnitMs:F1} 区域={_drawZoneMs:F1} 其它={_drawOtherMs:F1}] | "
                + $"GC[堆={_gcHeapMb}MB gen0+{_gc0Delta} gen2+{_gc2Delta}] | 地图={MapBox.width}x{MapBox.height} | "
                + PerfStats.Snapshot());
        }

        private static string SafeLowRes()
        {
            try { return MapBox.isRenderMiniMap() ? "是" : "否"; } catch { return "?"; }
        }

        /// <summary>卡顿探针：单帧超过 500ms 记一条（含分段耗时与 GC 增量）。</summary>
        private void HitchProbe(float dt)
        {
            try
            {
                if (dt < 0.5f) return;
                if (Time.unscaledTime - _hitchCooldown < 3f) return;
                _hitchCooldown = Time.unscaledTime;
                double ticksPerMs = System.Diagnostics.Stopwatch.Frequency / 1000.0;
                double simMs = PerfPhase.LastSimTicks / ticksPerMs;
                double actMs = PerfPhase.LastActorsTicks / ticksPerMs;
                int g0 = GC.CollectionCount(0), g2 = GC.CollectionCount(2);
                int d0 = g0 - _hitchLastG0, d2 = g2 - _hitchLastG2;
                _hitchLastG0 = g0; _hitchLastG2 = g2;
                Log.Warn("卡顿探针：单帧 " + (dt * 1000f).ToString("F0") + "ms（模拟 " + simMs.ToString("F1")
                         + "ms/角色 " + actMs.ToString("F1") + "ms，GC gen0+" + d0 + " gen2+" + d2
                         + "，堆=" + (GC.GetTotalMemory(false) / 1048576) + "MB，单位="
                         + (World.world != null && World.world.units != null ? World.world.units.Count : 0) + "）");
            }
            catch (Exception e)
            {
                PerfStats.OnException(e);
            }
        }

        /// <summary>
        /// GC 择机：游玩期间关闭自动 GC，堆涨到阈值（或超过硬上限）时主动回收一次。
        /// 硬上限必须存在，否则会一路涨到 OOM（实测过）。
        /// </summary>
        private void GcDeferTick()
        {
            var cfg = PerfConfig.I;
            if (!cfg.GcDeferMode) return;
            try
            {
                long heapMb = GC.GetTotalMemory(false) / 1048576;

                if (!_gcDeferActive)
                {
                    if (World.world == null || World.world.units == null) return;
                    UnityEngine.Scripting.GarbageCollector.GCMode = UnityEngine.Scripting.GarbageCollector.Mode.Disabled;
                    _gcDeferActive = true;
                    _gcDeferBaseMb = heapMb;
                    Log.Info("GC 择机：已暂停自动 GC（当前堆 " + heapMb + "MB，增长超 " + cfg.GcDeferHeapGrowthMb + "MB 或堆超 " + cfg.GcDeferHardCapMb + "MB 时回收）");
                    return;
                }

                bool overCap = heapMb > cfg.GcDeferHardCapMb;
                if (!overCap)
                {
                    if (heapMb - _gcDeferBaseMb < cfg.GcDeferHeapGrowthMb) return;
                    if (cfg.GcDeferOnlyWhenPaused)
                    {
                        bool paused;
                        try { paused = Config.paused; } catch { paused = false; }
                        if (!paused) { _pausedSince = -1f; return; }
                        if (_pausedSince < 0f) _pausedSince = Time.unscaledTime;
                        // 只对"玩家主动暂停"回收：短暂停多半是别的模组在做批量操作（清除/重建）
                        if (Time.unscaledTime - _pausedSince < 3f) return;
                    }
                }

                long before = heapMb;
                UnityEngine.Scripting.GarbageCollector.GCMode = UnityEngine.Scripting.GarbageCollector.Mode.Enabled;
                GC.Collect(2, GCCollectionMode.Forced, true);
                long after = GC.GetTotalMemory(false) / 1048576;
                UnityEngine.Scripting.GarbageCollector.GCMode = UnityEngine.Scripting.GarbageCollector.Mode.Disabled;
                _gcDeferBaseMb = after;
                Log.Info("择机 GC：堆 " + before + "MB → " + after + "MB" + (overCap ? "（触顶强制）" : "（暂停时回收）"));
            }
            catch (Exception e)
            {
                Log.Warn("GC 择机失败（已自动关闭该功能）：" + e.GetType().Name + ": " + e.Message);
                PerfConfig.I.GcDeferMode = false;
            }
        }

        /// <summary>世界载入后记一次堆基线（空图也记），用于对比地图尺寸与模组组合。</summary>
        private void HeapBaselineOnce()
        {
            if (_heapBaselineLogged) return;
            try
            {
                if (World.world == null || MapBox.width <= 0) return;
                if (!SknFeature.WorldLoadedOnce) return;
                _heapBaselineLogged = true;
                long heap = GC.GetTotalMemory(false) / 1048576;
                long tiles = (long)MapBox.width * MapBox.height;
                int units = World.world.units != null ? World.world.units.Count : 0;
                int cities = World.world.cities != null ? World.world.cities.Count : 0;
                Log.Info("堆基线：" + heap + "MB / 地图 " + MapBox.width + "x" + MapBox.height + "（" + (tiles / 10000L) + "万格）/ 单位 " + units + " / 城市 " + cities);
            }
            catch { }
        }

        internal static void ResetHeapBaseline()
        {
            _heapBaselineLogged = false;
        }
    }
}
