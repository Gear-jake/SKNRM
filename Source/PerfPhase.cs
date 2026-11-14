using System;
using System.Diagnostics;
using HarmonyLib;
using UnityEngine;

namespace SKNRM
{
    /// <summary>
    /// 帧内分段计时：模拟 updateSimulation / 角色 updateActors / 地图层绘制 MapLayer.draw /
    /// 名牌 NameplateManager.update。用来判断卡顿到底在 AI、在渲染、还是在别的模组。
    /// 纯 Stopwatch + Harmony 前后缀，不改任何逻辑。
    /// </summary>
    internal static class PerfPhase
    {
        internal static long SimTicks, ActorsTicks, DrawTicks, NameplateTicks;
        internal static int Frames;

        // 上一帧的原始耗时（供卡顿探针读取）
        internal static long LastSimTicks, LastActorsTicks, LastDrawTicks, LastNameplateTicks;

        [ThreadStatic] private static long _t0Sim;
        [ThreadStatic] private static long _t0Actors;
        [ThreadStatic] private static long _t0Draw;
        [ThreadStatic] private static long _t0Nameplate;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            int ok = 0;
            ok += TryPatch(harmony, typeof(MapBox), "updateSimulation", new[] { typeof(float) }, "SimPrefix", "SimPostfix", "MapBox.updateSimulation");
            ok += TryPatch(harmony, typeof(MapBox), "updateActors", new[] { typeof(float) }, "ActorsPrefix", "ActorsPostfix", "MapBox.updateActors");
            ok += TryPatch(harmony, typeof(MapLayer), "draw", new[] { typeof(float) }, "DrawPrefix", "DrawPostfix", "MapLayer.draw");
            ok += TryPatch(harmony, typeof(NameplateManager), "update", Type.EmptyTypes, "NameplatePrefix", "NameplatePostfix", "NameplateManager.update");
            Log.Info("分段计时补丁安装 " + ok + "/4（模拟/角色/地图层绘制/名牌）");
        }

        private static int TryPatch(HarmonyLib.Harmony harmony, Type type, string method, Type[] args,
            string prefixName, string postfixName, string label)
        {
            try
            {
                var target = AccessTools.Method(type, method, args);
                if (target == null)
                {
                    Log.Warn("计时补丁跳过（方法不存在）：" + label);
                    return 0;
                }
                harmony.Patch(target,
                    prefix: new HarmonyMethod(typeof(PerfPhase), prefixName),
                    postfix: new HarmonyMethod(typeof(PerfPhase), postfixName));
                return 1;
            }
            catch (Exception e)
            {
                Log.Warn("计时补丁失败：" + label + " -> " + e.Message);
                return 0;
            }
        }

        private static void SimPrefix() { _t0Sim = Stopwatch.GetTimestamp(); }

        private static void SimPostfix()
        {
            long d = Stopwatch.GetTimestamp() - _t0Sim;
            LastSimTicks = d;
            if (d > 0) System.Threading.Interlocked.Add(ref SimTicks, d);
        }

        private static void ActorsPrefix() { _t0Actors = Stopwatch.GetTimestamp(); }

        private static void ActorsPostfix()
        {
            long d = Stopwatch.GetTimestamp() - _t0Actors;
            LastActorsTicks = d;
            if (d > 0) System.Threading.Interlocked.Add(ref ActorsTicks, d);
        }

        private static void DrawPrefix() { _t0Draw = Stopwatch.GetTimestamp(); }

        private static void DrawPostfix()
        {
            long d = Stopwatch.GetTimestamp() - _t0Draw;
            LastDrawTicks = d;
            if (d > 0) System.Threading.Interlocked.Add(ref DrawTicks, d);
        }

        private static void NameplatePrefix() { _t0Nameplate = Stopwatch.GetTimestamp(); }

        private static void NameplatePostfix()
        {
            long d = Stopwatch.GetTimestamp() - _t0Nameplate;
            LastNameplateTicks = d;
            if (d > 0) System.Threading.Interlocked.Add(ref NameplateTicks, d);
        }

        internal static void NoteFrame()
        {
            System.Threading.Interlocked.Increment(ref Frames);
        }

        /// <summary>取走这一秒的累计值，换算成「每帧毫秒」并清零。</summary>
        internal static void Consume(out double simMs, out double actorsMs, out double drawMs, out double nameplateMs)
        {
            long sim = System.Threading.Interlocked.Exchange(ref SimTicks, 0);
            long act = System.Threading.Interlocked.Exchange(ref ActorsTicks, 0);
            long draw = System.Threading.Interlocked.Exchange(ref DrawTicks, 0);
            long plate = System.Threading.Interlocked.Exchange(ref NameplateTicks, 0);
            int frames = System.Threading.Interlocked.Exchange(ref Frames, 0);
            double ticksPerMs = Stopwatch.Frequency / 1000.0;
            simMs = frames > 0 ? sim / ticksPerMs / frames : 0.0;
            actorsMs = frames > 0 ? act / ticksPerMs / frames : 0.0;
            drawMs = frames > 0 ? draw / ticksPerMs / frames : 0.0;
            nameplateMs = frames > 0 ? plate / ticksPerMs / frames : 0.0;
        }
    }
}
