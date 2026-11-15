using System;
using System.IO;
using NeoModLoader.api;
using NeoModLoader.api.attributes;
using UnityEngine;

namespace SKNRM
{
    /// <summary>
    /// 719(NML 1.2) 入口。语义复刻大日春秋 3.1.6 的 SKN++ / SKN2.0 / UPP(不公平调度)。
    /// 一切游戏内行为都由 PerfPatches 里的 Harmony Prefix/Postfix 实现；
    /// 本类只负责：读配置 -> 装补丁 -> 挂诊断 ticker。
    /// </summary>
    public class SKNRMMain : BasicMod<SKNRMMain>, IReloadable
    {
        internal static HarmonyLib.Harmony HarmonyInstance;

        protected override void OnModLoad()
        {
            try
            {
                string dir = null;
                try
                {
                    var decl = GetDeclaration();
                    dir = decl?.FolderPath;
                }
                catch (Exception e)
                {
                    Log.Warn("GetDeclaration 失败（配置将使用内存默认值）：" + e.Message);
                }

                PerfConfig.LoadFrom(dir);
                ApplySafeModeIfNeeded();
            PerfSettings.Init(GetConfig());

                HarmonyInstance = new HarmonyLib.Harmony("lead.SKNRM");
                int installed = PerfPatches.InstallAll(HarmonyInstance);
            AiThrottle.Install(HarmonyInstance);
            Guards.Install(HarmonyInstance);
            PerfBoost.Install(HarmonyInstance);
                PerfPhase.Install(HarmonyInstance);
                EnemyOpt.Install(HarmonyInstance);
                JobStride.Install(HarmonyInstance);

                HarmonyUtil.LogSummary();
            Log.Info($"已加载：补丁 {installed} 处，配置 {PerfConfig.I}");
                if (installed == 0)
                {
                    Log.Warn("没有任何补丁安装成功——719 的 API 可能再次变动，请查 Player.log 里的 [SKNRM] 警告行。");
                }

                if (gameObject.GetComponent<PerfTicker>() == null)
                {
                    gameObject.AddComponent<PerfTicker>();
                }
            }
            catch (Exception e)
            {
                Log.Error("OnModLoad 异常：" + e);
            }
        }

        [Hotfixable]
        /// <summary>安全模式：关掉所有会改变玩法节拍的优化，只保留渲染/UI 节流与防崩。</summary>
        private static void ApplySafeModeIfNeeded()
        {
            try
            {
                var c = PerfConfig.I;
                if (!c.SafeMode) return;
                c.SknPlus = false;
                c.Skn2 = false;
                c.Unfair = false;
                c.SknLogicJobStride = 1;
                c.EnemyPhaseFrames = 1;
                c.EnemyKeepWarriorCadence = true;
                c.JobSeekerThrottle = 1;
                c.WarCivilianThrottle = 1;
                Log.Info("安全模式已启用：城市轮转/AI 节流/逻辑 job 分帧/寻敌相位 全部关闭（渲染与 UI 节流保留）");
            }
            catch (Exception e) { Log.Warn("安全模式应用失败：" + e.Message); }
        }

        public void Reload()
        {
            try
            {
                PerfConfig.Reload();
                SknFeature.ResetRuntimeState();
                UppDispatcher.Reset(false);
                Log.Info("热重载完成，配置 " + PerfConfig.I);
            }
            catch (Exception e)
            {
                Log.Error("Reload 异常：" + e);
            }
        }
    }

    /// <summary>统一日志：方便在 Player.log 里 grep "[SKNRM]"。</summary>
    internal static class Log
    {
        private const string Prefix = "[SKNRM] ";

        internal static void Info(string msg)
        {
            Debug.Log(Prefix + msg);
        }

        internal static void Warn(string msg)
        {
            Debug.LogWarning(Prefix + msg);
        }

        internal static void Error(string msg)
        {
            Debug.LogError(Prefix + msg);
        }
    }
}
