using System;
using System.Threading;
using HarmonyLib;

namespace SKNRM
{
    /// <summary>
    /// 寻敌（EnemiesFinder）方向的两级优化 + 一级观测。
    ///
    /// 背景（719 源码事实）：
    ///   · `EnemiesFinder.findEnemiesFrom(tile, kingdom, range)` 用「每王国 → 每 chunk+range」的字典缓存；
    ///   · `MapBox.Update` 每帧调一次 `EnemiesFinder.clear()`（MapBox.cs:1828），所以缓存**只在一帧内有效**；
    ///   · 缓存未命中时：先看本 chunk（range=0），空了就按圈向外扩（`checkRange` 会走 (2r+1)² 个 chunk），
    ///     每个 chunk 还要遍历其中的王国列表并 `isEnemy` 判定 —— 大军混战时命中率骤降，于是出现单帧尖峰。
    ///
    /// 本文件提供：
    ///   1) 观测：每秒统计「寻敌调用次数 / 命中缓存次数」，换算命中率（默认只记日志，不改行为）；
    ///   2) 分帧消化：`EnemyCacheFrames = N` → 缓存跨 N 帧才清一次（默认 2，等于寻敌扫描减半）；
    ///   3) 简化算法：`EnemyPeacefulRangeZero = true` → 没在打仗的王国只扫自己所在的 chunk，不再向外扩圈。
    /// 三项都可在 sknrm_config.json / F7 设置页关掉。
    /// </summary>
    internal static class EnemyOpt
    {
        /// <summary>线程内的寻敌范围覆盖：b3 前缀按"是不是平民"设 0，findEnemiesFrom 前缀读取（同线程同步调用链）。</summary>
        [ThreadStatic] private static int _rangeOverride = -1;

        internal static long Calls;        // 本秒 findEnemiesFrom 调用次数
        internal static long ReusedLast;   // 上次统计点时的 counter_reused

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            int ok = 0;
            ok += TryPatch(harmony, "findEnemiesFrom", nameof(FindPrefix), null, "寻敌观测/范围简化");
            ok += TryPatch(harmony, "clear", nameof(ClearPrefix), null, "寻敌缓存跨帧");
            ok += TryPatchActorB3(harmony);
            Log.Info($"寻敌优化已安装 {ok}/2（缓存跨帧={PerfConfig.I.EnemyCacheFrames}，和平王国只扫本 chunk={PerfConfig.I.EnemyPeacefulRangeZero}）");
        }

        private static int TryPatch(HarmonyLib.Harmony harmony, string method, string prefixName, string postfixName, string label)
        {
            try
            {
                var target = AccessTools.Method(typeof(EnemiesFinder), method);
                if (target == null)
                {
                    Log.Warn("寻敌补丁跳过（方法不存在）：" + label);
                    return 0;
                }
                harmony.Patch(target,
                    prefix: new HarmonyMethod(typeof(EnemyOpt), prefixName),
                    postfix: postfixName == null ? null : new HarmonyMethod(typeof(EnemyOpt), postfixName));
                return 1;
            }
            catch (Exception e)
            {
                Log.Warn("寻敌补丁失败：" + label + " -> " + e.Message);
                return 0;
            }
        }

        /// <summary>每秒取一次调用量，用于算缓存命中率（counter_reused 由游戏自己累加）。</summary>
        private static int TryPatchActorB3(HarmonyLib.Harmony harmony)
        {
            try
            {
                var target = AccessTools.Method(typeof(Actor), "b3_findEnemyTarget", new[] { typeof(float) });
                if (target == null)
                {
                    Log.Warn("寻敌相位补丁跳过：Actor.b3_findEnemyTarget 不存在");
                    return 0;
                }
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(EnemyOpt), nameof(B3Prefix)), postfix: new HarmonyMethod(typeof(EnemyOpt), nameof(B3Postfix)));
                return 1;
            }
            catch (Exception e)
            {
                Log.Warn("寻敌相位补丁失败：" + e.Message);
                return 0;
            }
        }

        private static void B3Postfix()
        {
            _rangeOverride = -1;
        }

        private static bool B3Prefix(Actor __instance)
        {
            try
            {
                var cfg = PerfConfig.I;
                int n = cfg.EnemyPhaseFrames;
                if (n <= 1 || __instance == null) return true;

                // 士兵（非队长）+ 队长没在打仗 → 不做自己的目标扫描，只跟随/挨打反应
                if (cfg.ArmyIdleNoScan)
                {
                    try
                    {
                        if (__instance.isArmyGroupWarrior())
                        {
                            Army cap = __instance.army;
                            Actor captain = cap != null ? cap.getCaptain() : null;
                            bool captainFighting = captain != null
                                && ((captain.has_attack_target) || captain.isFighting());
                            if (!captainFighting)
                            {
                                PerfStats.Skip(ref PerfStats.EnemyIdleSoldierSkipped);
                                return false;
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                if (cfg.EnemyKeepWarriorCadence)
                {
                    bool important = false;
                    try
                    {
                        important = __instance.isWarrior() || __instance.hasArmy() || SknFeature.IsProtectedCaptain(__instance);
                    }
                    catch
                    {
                        important = true;
                    }
                    if (important) return true;
                }

                long id;
                try
                {
                    id = __instance.getID();
                }
                catch
                {
                    return true;
                }

                if (PerfConfig.I.EnemyCivilianRangeZero) _rangeOverride = 0;   // 平民只认本 chunk
                int phase = (int)(id % n);
                if (((UnityEngine.Time.frameCount + phase) % n) != 0)
                {
                    PerfStats.Skip(ref PerfStats.EnemyPhased);
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                PerfStats.OnException(e);
                return true;
            }
        }

        internal static void ConsumeSecond(out long calls, out long reused)
        {
            calls = Interlocked.Exchange(ref Calls, 0);
            long totalReused;
            try
            {
                totalReused = EnemiesFinder.counter_reused;
            }
            catch
            {
                totalReused = 0;
            }
            // counter_reused 会在 clear() 时归零；跨帧缓存后 clear 每 N 帧才跑，一次归零会让"本次 < 上次"，
            // 旧的 Math.Max(0,..) 就会把这一秒误报成 0% 命中。这里把回绕当作"本秒全部命中"。
            long delta = totalReused - ReusedLast;
            reused = delta >= 0 ? delta : totalReused;
            ReusedLast = totalReused;
            ReusedLast = totalReused;
        }

        private static bool ClearPrefix()
        {
            try
            {
                int n = PerfConfig.I.EnemyCacheFrames;
                if (n <= 1) return true;                       // 原版：每帧都清
                // 只在第 0 帧清：缓存跨 N 帧复用
                return (UnityEngine.Time.frameCount % n) == 0;
            }
            catch (Exception e)
            {
                PerfStats.OnException(e);
                return true;
            }
        }

        private static void FindPrefix(WorldTile pTile, Kingdom pKingdom, ref int pChunkRange)
        {
            try
            {
                Interlocked.Increment(ref Calls);

                if (_rangeOverride == 0)
                {
                    pChunkRange = 0;
                    PerfStats.Skip(ref PerfStats.EnemyRangeShrunk);
                    return;
                }

                // 简化算法：没在打仗的王国不去向外扩圈找敌人（只看自己所在 chunk）
                if (!PerfConfig.I.EnemyPeacefulRangeZero) return;
                if (pTile == null || pKingdom == null) return;
                if (pChunkRange == 0) return;
                if (SknFeature.IsKingdomAtWar(pKingdom)) return;   // 交战王国保持原样
                if (pKingdom.asset != null && pKingdom.asset.mobs) return; // 野兽/怪物保持原样
                pChunkRange = 0;
                PerfStats.Skip(ref PerfStats.EnemyRangeShrunk);
            }
            catch (Exception e)
            {
                PerfStats.OnException(e);
            }
        }
    }
}
