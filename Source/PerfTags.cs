using System;

namespace SKNRM
{
    /// <summary>
    /// SKN / PI 标签。719 的 BaseSystemData 仍有 set(string,bool) / get(string,out bool,default)，
    /// 所以底层写法与 0.22.9 版一致：写进 ActorData.custom_data_bool 的 "&lt;tag&gt;_runtag" 键，
    /// 随 SavedMap 序列化（719: SavedMap.actors_data）——即标签会进存档。
    /// </summary>
    internal static class PerfTags
    {
        internal const string SKN = "SKN";
        internal const string PI = "PI";

        // 常量键：原来用 $"{tag}_runtag" 每帧要为每个角色分配 2 个字符串
        // （3000 单位 × 60fps × 3 个 b* 入口 × 2 个标签 ≈ 100 万次/秒的短字符串分配），改成常量后为 0。
        private const string KeySkn = "SKN_runtag";
        private const string KeyPi = "PI_runtag";

        private static string Key(string tag)
        {
            if (tag == SKN) return KeySkn;
            if (tag == PI) return KeyPi;
            return tag + "_runtag";
        }

        internal static bool Has(Actor actor, string tag)
        {
            if (actor == null) return false;
            var data = actor.data;
            if (data == null) return false;
            data.get(Key(tag), out bool result, false);
            return result;
        }

        internal static void Add(Actor actor, string tag)
        {
            if (actor == null || actor.asset == null || !actor.asset.has_ai_system) return;
            var data = actor.data;
            if (data == null) return;
            if (Has(actor, tag)) return;
            data.set(Key(tag), true);
            PerfStats.TagAdded++;
            if (PerfConfig.I.Verbose)
            {
                Log.Info("打标签 " + tag + " -> " + SafeName(actor));
            }
        }

        internal static void Remove(Actor actor, string tag)
        {
            if (actor == null) return;
            var data = actor.data;
            if (data == null) return;
            data.set(Key(tag), false);
        }

        internal static bool HasSkn(Actor actor)
        {
            return Has(actor, SKN);
        }

        internal static bool HasPi(Actor actor)
        {
            return Has(actor, PI);
        }

        internal static string SafeName(Actor actor)
        {
            try
            {
                return actor == null ? "<null>" : actor.getName();
            }
            catch
            {
                return "<unnamed>";
            }
        }
    }
}
