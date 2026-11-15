using System;
using System.Reflection;
using HarmonyLib;

namespace SKNRM
{
    /// <summary>
    /// AI 节流的"通用落点"：直接挂在 `AiSystemActor.update()` 上，而不是 Actor.b4/b6 那些
    /// 被大量模组同时补丁的方法上。
    ///
    /// 为什么这样更可扩展：
    ///   · `Actor.b6_updateAI()` 的职责只是"条件判断 + 调 ai.update()"，节流挂在更下游的
    ///     `AiSystemActor.update()` 上，`b6_updateAI` 本体照常执行 —— **别的模组挂在
    ///     Actor.b4/b6 上的前缀/后缀都不会被跳过**，天然共存；
    ///   · 全仓审计里没有任何模组补丁 `AiSystem*`，所以这一层几乎不会与任何人冲突；
    ///   · 行为等价：跳过 ai.update() 就等于"这一帧这个单位不思考"，与原来跳过 b6 的效果一致。
    /// </summary>
    internal static class AiThrottle
    {
        private static FieldInfo _aiObjectField;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            try
            {
                var target = AccessTools.Method(typeof(AiSystemActor), "update", Type.EmptyTypes);
                if (target == null)
                {
                    Log.Warn("AI 节流：未找到 AiSystemActor.update，回退到 Actor 层节流");
                    return;
                }
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(AiThrottle), nameof(UpdatePrefix)));
                Log.Info("AI 节流已挂到 AiSystemActor.update（通用落点，不再占用 Actor.b4/b6）");
            }
            catch (Exception e)
            {
                Log.Warn("AI 节流安装失败：" + e.Message);
            }
        }

        private static Actor GetActor(AiSystemActor ai)
        {
            if (_aiObjectField == null)
            {
                // ai_object 定义在泛型基类 AiSystem<...> 上，AccessTools 会沿继承链查找
                _aiObjectField = AccessTools.Field(typeof(AiSystemActor), "ai_object");
            }
            try
            {
                return _aiObjectField != null ? _aiObjectField.GetValue(ai) as Actor : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool UpdatePrefix(AiSystemActor __instance)
        {
            try
            {
                if (!PerfConfig.I.SknPlus) return true;
                Actor actor = GetActor(__instance);
                if (actor == null) return true;

                // ShouldSkipThisFrame 内部已包含：人口闸门、军队/战争/被围城保护、
                // 镜头内保护、job 白名单、征兵放宽、大规模变更保护等全部判断。
                bool skip = SknFeature.ShouldSkipThisFrame(actor);
                if (skip) PerfStats.Skip(ref PerfStats.AiSkipped);
                return !skip;
            }
            catch (Exception e)
            {
                PerfStats.OnException(e);
                return true;
            }
        }
    }
}