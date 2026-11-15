using System;
using HarmonyLib;

namespace SKNRM
{
    /// <summary>
    /// 全部 Harmony 落点（719 版）。1 处失败不影响其它补丁，安装结果逐条写日志。
    ///
    /// 与原版（0.22.9 / 大日春秋 3.1.6）的落点对照：
    ///   0229 BatchActors.b456_checkMergedJobs  → 719 拆成 Actor.b4 / b5 / b6_0 / b6 四条独立 Post job
    ///   0229 Actor.updateHunger                → 719 Actor.updateNutritionDecay(bool)
    ///   0229 MapBox.updateObjectAge            → 719 同名同触发（年变化）
    ///   0229 MapBox.Update 整体重写             → 719 改为 MapBox.updateCities / updateBuildings / updateActors 三个一行委托上分片
    ///   0229 CitiesManager.update              → 719 CityManager.update
    ///   0229 City.update 整体重写               → 719 用 City.timer_action 节流拒绝（不重写方法体）
    ///   0229 WindowCreatureInfo.OnEnable       → 719 UnitWindow.showInfo
    ///
    /// 线程安全：719 的 Post 阶段按批次并行（JobType.Post），所以四个 b* Prefix 只做「读 + Interlocked 计数」，
    /// 不在批处理里写数据；写 tag 的两个入口（年度 PI、UnitWindow 打标）都在主线程。
    /// </summary>
    internal static class PerfPatches
    {
        internal static int InstallAll(HarmonyLib.Harmony harmony)
        {
            int ok = 0;
            ok += Patch(harmony, typeof(Actor), "updateNutritionDecay", new[] { typeof(bool) },
                new HarmonyMethod(typeof(PerfPatches), nameof(NutritionPrefix)), null, "Actor.updateNutritionDecay 饥饿停摆");
            ok += Patch(harmony, typeof(MapBox), "updateObjectAge", Type.EmptyTypes,
                null, new HarmonyMethod(typeof(PerfPatches), nameof(YearTickPostfix)), "MapBox.updateObjectAge 年度闸门");
            ok += Patch(harmony, typeof(UnitWindow), "showInfo", Type.EmptyTypes,
                null, new HarmonyMethod(typeof(PerfPatches), nameof(InspectPostfix)), "UnitWindow.showInfo 查看打标");
            ok += Patch(harmony, typeof(CityManager), "update", new[] { typeof(float) },
                new HarmonyMethod(typeof(PerfPatches), nameof(CityManagerPrefix)), null, "CityManager.update UPP 游标");
            ok += Patch(harmony, typeof(City), "update", new[] { typeof(float) },
                new HarmonyMethod(typeof(PerfPatches), nameof(CityUpdatePrefix)), null, "City.update UPP 思考闸");
            ok += Patch(harmony, typeof(City), "makeWarrior", new[] { typeof(Actor) },
                null, new HarmonyMethod(typeof(PerfPatches), nameof(MakeWarriorPostfix)), "City.makeWarrior 征兵计数");
            ok += Patch(harmony, typeof(WorldLog), "logNewCity", new[] { typeof(City) },
                null, new HarmonyMethod(typeof(PerfPatches), nameof(NewCityPostfix)), "WorldLog.logNewCity 城市登记");
            ok += Patch(harmony, typeof(MapBox), "updateCities", new[] { typeof(float) },
                new HarmonyMethod(typeof(PerfPatches), nameof(SliceCitiesPrefix)), null, "MapBox.updateCities 分片");
            ok += Patch(harmony, typeof(MapBox), "updateBuildings", new[] { typeof(float) },
                new HarmonyMethod(typeof(PerfPatches), nameof(SliceCitiesPrefix)), null, "MapBox.updateBuildings 分片");
            ok += Patch(harmony, typeof(MapBox), "updateActors", new[] { typeof(float) },
                new HarmonyMethod(typeof(PerfPatches), nameof(SliceActorsPrefix)), null, "MapBox.updateActors 分片");
            return ok;
        }

        private static int Patch(HarmonyLib.Harmony harmony, Type type, string method, Type[] args,
            HarmonyMethod prefix, HarmonyMethod postfix, string label)
        {
            try
            {
                var target = AccessTools.Method(type, method, args);
                if (target == null)
                {
                    Log.Warn($"补丁跳过：{type.Name}.{method} 未找到（719 API 可能已变）");
                    return 0;
                }
                harmony.Patch(target, prefix: prefix, postfix: postfix);
                if (PerfConfig.I.Verbose) Log.Info("补丁已装：" + label);
                return 1;
            }
            catch (Exception e)
            {
                Log.Warn($"补丁失败：{label} -> {e.GetType().Name}: {e.Message}");
                return 0;
            }
        }

        // ────────── SKN：四个角色侧入口，各自独立开关 ──────────
        // 默认跳过 b4(校验) / b6_0(决策) / b6(AI)，**不跳** b5(路径起跑)。
        // 原因：719 的路径索引推进发生在 b5 → updatePathMovement，跳它会让"有路径"的角色卡死在路径上；
        //      0229 是三合一的 b456，跳掉后由 u10 接力，语义不同。要完全复刻 0229 的"冻住不动"，
        //      把 sknrm_config.json 的 SkipPathStart 改 true 即可（已知代价见 README）。





        private static bool NutritionPrefix(Actor __instance)
        {
            try
            {
                if (SknFeature.FreezeNutritionActive())
                {
                    __instance.setNutrition(__instance.getMaxNutrition());
                    PerfStats.Skip(ref PerfStats.NutritionSkipped);
                    return false;
                }
            }
            catch (Exception e)
            {
                PerfStats.OnException(e);
            }
            return true;
        }

        private static void YearTickPostfix(MapBox __instance)
        {
            SknFeature.OnYearTick();
        }

        private static void InspectPostfix(UnitWindow __instance)
        {
            try
            {
                SknFeature.TagInspected(__instance.actor);
            }
            catch (Exception e)
            {
                PerfStats.OnException(e);
            }
        }

        // ────────── SKN2.0 / 主循环分片 ──────────
        // 0229 的分片表（照抄语义，落点从"重写 MapBox.Update"换成三个子方法）：
        //   SKN2.0 开：墙钟毫秒 % 20 → [5,10) 跑 cities+buildings，[10,20) 跑 actors
        //   SKN2.0 关：墙钟毫秒 % 10 → [3,6) 跑 cities+buildings，actors 每帧都跑

        private static long FrameBucket(int mod)
        {
            long ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long b = ms % mod;
            return b < 0 ? b + mod : b;
        }

        private static bool SliceCitiesPrefix()
        {
            try
            {
                if (!SknFeature.MasterGateOpen()) return true;
                bool slice2 = PerfConfig.I.Skn2;
                long b = FrameBucket(slice2 ? 20 : 10);
                bool allow = slice2 ? (b >= 5 && b < 10) : (b >= 3 && b < 6);
                if (!allow) PerfStats.Skip(ref PerfStats.Sliced);
                return allow;
            }
            catch (Exception e)
            {
                PerfStats.OnException(e);
                return true;
            }
        }

        private static bool SliceActorsPrefix()
        {
            try
            {
                if (!SknFeature.MasterGateOpen()) return true;
                var cfg = PerfConfig.I;
                int maxStride = cfg.SknActorStrideMax;
                if (maxStride <= 0) return true;                       // 关闭步长

                // 自动步长：人口 / 1500，上限 maxStride；Skn2 打开时至少 2
                int divisor = cfg.SknActorStrideDivisor > 0 ? cfg.SknActorStrideDivisor : 1500;
                int stride = (int)(SknFeature.UnitsAtGate / (long)divisor);
                if (stride < 1) stride = 1;
                if (cfg.Skn2 && stride < 2) stride = 2;
                if (stride > maxStride) stride = maxStride;
                if (stride <= 1) return true;

                bool allow = (UnityEngine.Time.frameCount % stride) == 0;
                if (!allow) PerfStats.Skip(ref PerfStats.Sliced);
                return allow;
            }
            catch (Exception e)
            {
                PerfStats.OnException(e);
                return true;
            }
        }

        // ────────── UPP ──────────

        private static void CityManagerPrefix(CityManager __instance)
        {
            try
            {
                PerfStats.Skip(ref PerfStats.UppTicks);
                if (!PerfConfig.I.Unfair) return;
                UppDispatcher.EnsureBuilt();
                UppDispatcher.Advance();
            }
            catch (Exception e)
            {
                PerfStats.OnException(e);
            }
        }

        /// <summary>城市是否应当无条件放行（军队/围城/首都/交战王国）。</summary>
        /// <summary>统计征兵次数，用于验证"征兵难"是否被节流影响。</summary>
        private static void MakeWarriorPostfix()
        {
            PerfStats.Skip(ref PerfStats.WarriorsMade);
        }

        private static bool CityIsProtected(City city)
        {
            if (city == null) return false;
            try
            {
                if (city.army != null && city.army.hasCaptain()) return true;
                if (city.isGettingCaptured()) return true;
                if (city.isCapitalCity()) return true;
                if (SknFeature.IsKingdomAtWar(city.kingdom))
                {
                    if (PerfConfig.I.WarProtectScope == 0) return true;      // 旧：整个交战王国都保护
                    if (SknFeature.IsFrontlineCity(city)) return true;       // 新：只保护前线城
                }
            }
            catch (Exception e)
            {
                PerfStats.OnException(e);
            }
            return false;
        }

        private static void CityUpdatePrefix(City __instance)
        {
            try
            {
                PerfStats.Skip(ref PerfStats.CityUpdateCalls);
                var cfg = PerfConfig.I;
                if (!cfg.Unfair) return;
                if (UppDispatcher.ForceRefresh > 0) return;
                if (__instance == null || __instance.ai == null) return;

                // 不调 World.world.isPaused()（internal，且原版 City.update 自己会在 ai 之前 return），
                // 避免多一个内部成员依赖。
                if (cfg.UppCityAgeMonths > 0)
                {
                    int months = Date.getMonthsSince(__instance.data.created_time);
                    if (months <= cfg.UppCityAgeMonths) return;
                }

                if (BulkGuard.Active) { UppDispatcher.NoteAllowed(__instance); return; }   // 大规模清除/重建期间城市全速
                if (__instance.timer_action > 0f) return; // 原版自己就会节流，不重复干预

                // 城市级硬保护：有军队 / 正在被围攻 / 首都 / 王国在打仗 —— 不参与轮转节流
                if (cfg.UppProtectArmyCities && CityIsProtected(__instance))
                {
                    UppDispatcher.NoteAllowed(__instance);
                    PerfStats.Skip(ref PerfStats.UppProtected);
                    return;
                }

                // 征兵保护：战士名额没满 / 有进攻命令 / 正在被围攻 → 放行，保证征兵与补兵不被人为拖慢
                // 交战王国的城市：不参与轮转（战争期征兵/调兵/补兵全靠城市 AI）
                if (cfg.UppProtectWarCities)
                {
                    bool atWar = false;
                    try { atWar = SknFeature.IsKingdomAtWar(__instance.kingdom); } catch { }
                    if (atWar)
                    {
                        UppDispatcher.NoteAllowed(__instance);
                        PerfStats.Skip(ref PerfStats.UppWarAllowed);
                        return;
                    }
                }

                if (cfg.UppProtectRecruitment)
                {
                    bool needsWarriors = false;
                    try { needsWarriors = __instance.status != null && __instance.status.warriors_current < __instance.status.warrior_slots; } catch { }
                    if (!needsWarriors) { try { needsWarriors = __instance.hasAttackZoneOrder(); } catch { } }
                    if (!needsWarriors) { try { needsWarriors = __instance.isGettingCaptured(); } catch { } }
                    if (needsWarriors)
                    {
                        UppDispatcher.NoteAllowed(__instance);
                        PerfStats.Skip(ref PerfStats.UppRecruit);
                        return;
                    }
                }

                if (UppDispatcher.IsAllowed(__instance))
                {
                    UppDispatcher.NoteAllowed(__instance);
                    PerfStats.Skip(ref PerfStats.ThinkAllowed);
                    return;
                }

                // 饥饿保护：连续被拒太多次就强制放行一次，避免某城长期轮不到
                int denyStreak = UppDispatcher.NoteDenied(__instance);
                if (cfg.UppCityMaxDenyFrames > 0 && denyStreak >= cfg.UppCityMaxDenyFrames)
                {
                    UppDispatcher.NoteAllowed(__instance);
                    PerfStats.Skip(ref PerfStats.UppStarved);
                    return;
                }

                // 本帧拒绝思考：原版 City.update 里 timer_action>0 就只递减、不调 ai.update()
                __instance.timer_action = 0.0001f;
                PerfStats.Skip(ref PerfStats.ThinkDenied);
            }
            catch (Exception e)
            {
                PerfStats.OnException(e);
            }
        }

        private static void NewCityPostfix(City pCity)
        {
            try
            {
                if (!PerfConfig.I.Unfair) return;
                UppDispatcher.Invalidate();
            }
            catch (Exception e)
            {
                PerfStats.OnException(e);
            }
        }
    }
}
