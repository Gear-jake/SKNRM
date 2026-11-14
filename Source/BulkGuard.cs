using System;
using UnityEngine;

namespace SKNRM
{
    /// <summary>
    /// 大规模变更保护（“清除 / 重建”这类批量操作的兼容层）。
    ///
    /// 背景：像「天工技艺」的“清除”这种功能，会在**暂停状态**下用协程分批删除成千上万个
    /// 建筑 / 单位 / 城市。此时本模组如果还在做两件事，就会表现为“清除卡死 / 冲突”：
    ///   1. **GC 择机**看到“已暂停 + 堆增长”就触发一次完整回收 —— 1.9GB 堆要 1–3 秒，
    ///      正好卡在清除过程中；
    ///   2. **节流**让城市 / 角色少思考，清除后的重建（新城、军队、寻路）明显变慢。
    ///
    /// 识别方式与具体模组无关：每 0.5 秒采样一次 单位 / 建筑 / 城市 数量，
    /// 任一出现“断崖式下跌”（少了 ≥20% 且 ≥50 个）就进入宽限窗口（默认 5 秒）：
    ///   · 窗口内不触发 GC 择机；
    ///   · 窗口内不节流（城市与角色按原速运行）；
    ///   · 让 UPP 桶强制重建，丢掉已被删除的城市引用。
    ///
    /// 好处：不需要 patch 任何第三方模组，也不会因为对方改名 / 换实现而失效。
    /// </summary>
    internal static class BulkGuard
    {
        private static float _until;
        private static float _timer;
        private static int _lastUnits = -1, _lastBuildings = -1, _lastCities = -1;
        private static bool _logged;

        internal static bool Active => Time.unscaledTime < _until;

        internal static void Tick(float dt)
        {
            if (!PerfConfig.I.BulkChangeGuard) return;

            if (!Active) _logged = false;

            _timer += dt;
            if (_timer < 0.5f) return;
            _timer = 0f;

            try
            {
                if (World.world == null) return;
                int units = World.world.units != null ? World.world.units.Count : 0;
                int buildings = World.world.buildings != null ? World.world.buildings.Count : 0;
                int cities = World.world.cities != null ? World.world.cities.Count : 0;

                if (_lastUnits >= 0 && IsBulkDrop(_lastUnits, units)) Enter("单位 " + _lastUnits + " → " + units);
                else if (_lastBuildings >= 0 && IsBulkDrop(_lastBuildings, buildings)) Enter("建筑 " + _lastBuildings + " → " + buildings);
                else if (_lastCities >= 0 && IsBulkDrop(_lastCities, cities)) Enter("城市 " + _lastCities + " → " + cities);

                _lastUnits = units;
                _lastBuildings = buildings;
                _lastCities = cities;
            }
            catch (Exception e)
            {
                PerfStats.OnException(e);
            }
        }

        /// <summary>断崖式下跌：少了 ≥20% 且绝对数量 ≥50。</summary>
        private static bool IsBulkDrop(int before, int after)
        {
            if (before <= 0) return false;
            int drop = before - after;
            if (drop < 50) return false;
            return drop * 5 >= before;
        }

        private static void Enter(string what)
        {
            float seconds = Math.Max(1f, PerfConfig.I.BulkChangeSeconds);
            _until = Time.unscaledTime + seconds;
            try { UppDispatcher.Reset(false); } catch { }
            if (_logged) return;
            _logged = true;
            Log.Info("检测到大规模变更（" + what + "）：接下来 " + seconds.ToString("F0")
                     + " 秒暂停 GC 择机并关闭节流，避免与清除/重建操作抢资源");
        }
    }
}
