using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace SKNRM
{
    /// <summary>
    /// 与其它模组的共存检查。
    ///
    /// 本模组大量使用"跳过原版方法"的前缀（节流/分帧）。Harmony 的规则是：
    /// 任一前缀返回 false，原版方法体与所有 postfix 都不执行（同优先级下后续前缀也会被跳过）。
    /// 因此如果别的模组也在同一个方法上挂了前缀/后缀，我们的跳过就会把它的逻辑一并吃掉。
    ///
    /// 对策：安装"跳过型"补丁之前先查目标方法的 HarmonyPatchInfo，
    /// 只要发现非本模组的补丁，就放弃安装该补丁（保留原版行为）并记日志。
    /// </summary>
    internal static class HarmonyUtil
    {
        private static readonly List<string> _backedOff = new List<string>();

        internal static bool HasForeignPatch(HarmonyLib.Harmony harmony, MethodInfo method)
        {
            try
            {
                if (method == null) return false;
                Patches info = Harmony.GetPatchInfo(method);
                if (info == null) return false;

                foreach (Patch p in info.Prefixes) if (p != null && p.owner != harmony.Id) return true;
                foreach (Patch p in info.Postfixes) if (p != null && p.owner != harmony.Id) return true;
                foreach (Patch p in info.Transpilers) if (p != null && p.owner != harmony.Id) return true;
                foreach (Patch p in info.Finalizers) if (p != null && p.owner != harmony.Id) return true;
            }
            catch
            {
            }
            return false;
        }

        /// <summary>跳过型补丁的安装闸：目标已被别的模组补丁时返回 true（调用方放弃安装）。</summary>
        internal static bool ShouldBackOff(HarmonyLib.Harmony harmony, Type type, string method, Type[] args, string label)
        {
            try
            {
                MethodInfo target = AccessTools.Method(type, method, args);
                if (target == null) return false;
                if (!HasForeignPatch(harmony, target)) return false;
            }
            catch
            {
                return false;
            }
            if (!_backedOff.Contains(label)) _backedOff.Add(label);
            return true;
        }

        internal static void LogSummary()
        {
            if (_backedOff.Count == 0)
            {
                Log.Info("共存检查：未发现冲突的补丁点（全部节流补丁已安装）");
                return;
            }
            Log.Info("共存检查：有 " + _backedOff.Count + " 个补丁点已被其它模组占用，已放弃安装以保护对方功能：" + string.Join(", ", _backedOff.ToArray()));
        }
    }
}