using System;
using HarmonyLib;

namespace SKNRM
{
    /// <summary>
    /// 逻辑 job 级分帧：把 BatchActors 里那些"纯计算、和画面无关"的 job 包装方法
    /// （b1/b2/b3/b4/b5/b55/b6_0、u1/u4/u5/u6/u7/u8、update_stats、update_hunger 等）
    /// 按帧轮转跳过，而**视觉类 job 保持每帧**（可见性/渲染器/精灵动画/平滑移动/抖动/悬停/授粉）。
    ///
    /// 为什么这样做：之前是"整条 updateActors 每 N 帧跑一次"，结果连移动和动画也一起降帧，
    /// 画面一顿一顿；改成 job 级分帧后，单位每帧都在动、在播动画，只有 AI/状态检查降频。
    ///
    /// 实测依据：7176 单位时单次完整 actor 管线约 800ms（29 个 job × 7k 单位的固定开销），
    /// 所以必须把固定开销的 job 摊开，而不是整体停摆。
    /// </summary>
    internal static class JobStride
    {
        internal static void Install(HarmonyLib.Harmony harmony)
        {
            var cfg = PerfConfig.I;
            int stride = cfg.SknLogicJobStride;
            if (stride <= 1)
            {
                Log.Info("逻辑 job 分帧：已关闭（SknLogicJobStride<=1）");
                return;
            }

            int ok = 0, fail = 0;
            foreach (string raw in (cfg.SknLogicJobs ?? string.Empty).Split(','))
            {
                string name = raw.Trim();
                if (name.Length == 0) continue;
                try
                {
                    var m = AccessTools.Method(typeof(BatchActors), name, Type.EmptyTypes);
                    if (m == null)
                    {
                        fail++;
                        continue;
                    }
                    if (HarmonyUtil.HasForeignPatch(harmony, m))
                    {
                        HarmonyUtil.ShouldBackOff(harmony, typeof(BatchActors), name, Type.EmptyTypes, "BatchActors." + name);
                        fail++;
                        continue;
                    }
                    harmony.Patch(m, prefix: new HarmonyMethod(typeof(JobStride), nameof(StridePrefix)));
                    ok++;
                }
                catch (Exception e)
                {
                    fail++;
                    Log.Warn("逻辑 job 分帧失败：" + name + " -> " + e.Message);
                }
            }
            Log.Info($"逻辑 job 分帧：{ok} 个 job 每 {stride} 帧跑一次（失败 {fail} 个；视觉类 job 保持每帧）");
        }

        private static bool StridePrefix()
        {
            try
            {
                // 人口未达阈值时完全按原版跑：小图上对决策类 job 分帧会让单位看起来蠕动/不干活
                if (!SknFeature.MasterGateOpen()) return true;
                int n = PerfConfig.I.SknLogicJobStride;
                if (n <= 1) return true;
                bool run = (UnityEngine.Time.frameCount % n) == 0;
                if (!run) PerfStats.Skip(ref PerfStats.LogicJobSkipped);
                return run;
            }
            catch (Exception e)
            {
                PerfStats.OnException(e);
                return true;
            }
        }
    }
}
