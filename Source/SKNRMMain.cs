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
                PerfSettings.Init(GetConfig());

                HarmonyInstance = new HarmonyLib.Harmony("lead.SKNRM");
                int installed = PerfPatches.InstallAll(HarmonyInstance);
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
