using System;
using System.Collections.Generic;

namespace SKNRM
{
    /// <summary>
    /// UPP（不公平调度 / UnfairDispatcher）语义复刻。
    ///
    /// 大日 0229 版的做法是"整体重写 City.update + 自建桶 + 邻城链 + 宽限期"。
    /// 719 的实现换了两个更安全的落点，但**行为等价**：
    ///   · 桶：按相邻城聚簇、每桶容量可配（默认 5），时间上按桶轮转；
    ///   · 命中：当前桶的城市 + 它们的全部邻城 这一帧可以"思考"，其余城市本帧被拒；
    ///   · 拒绝方式：把 timer_action 设成一个极小正值，让原版 City.update 自己跳过 ai.update()
    ///     （719 原生就有 `if (timer_action &gt; 0f) timer_action -= pElapsed; else ai.update();`），
    ///     因此**不需要重写 City.update 的方法体**；
    ///   · 宽限期：ForceRefresh &gt; 0 时全部城市照常思考；「防掉压」关着时每年补一段宽限。
    /// </summary>
    internal static class UppDispatcher
    {
        internal static int Cursor;
        internal static int BucketCount;
        internal static int ForceRefresh;

        private static readonly List<List<City>> _buckets = new List<List<City>>();
        private static readonly HashSet<City> _live = new HashSet<City>();
        private static readonly HashSet<City> _registered = new HashSet<City>();
        private static int _builtForCityCount = -1;
        private static bool _dirty = true;
        private static bool _neighbourAccessFailed;
        private static readonly Dictionary<City, int> _denyStreak = new Dictionary<City, int>();

        /// <summary>城市集合发生变化（新建/摧毁/读档）时置脏，下一帧重建。</summary>
        internal static void Invalidate()
        {
            _dirty = true;
        }

        internal static void Reset(bool debug)
        {
            _buckets.Clear();
            _live.Clear();
            _registered.Clear();
            Cursor = 0;
            BucketCount = 0;
            ForceRefresh = 0;
            _builtForCityCount = -1;
            _denyStreak.Clear();
            _dirty = true;
            if (debug) Log.Info("UPP 已重置（桶清空，下一帧重建）");
        }

        internal static void EnsureBuilt()
        {
            if (World.world == null || World.world.cities == null) return;
            int count = World.world.cities.Count;
            if (!_dirty && count == _builtForCityCount) return;
            Rebuild(count);
        }

        private static void Rebuild(int cityCount)
        {
            _buckets.Clear();
            _registered.Clear();
            _live.Clear();

            int capacity = Math.Max(1, PerfConfig.I.UppBucketCapacity);
            List<City> all;
            try
            {
                all = World.world.cities.list;
            }
            catch (Exception e)
            {
                Log.Warn("UPP 取城市列表失败：" + e.Message);
                return;
            }

            var queue = new Queue<City>();
            for (int i = 0; i < all.Count; i++)
            {
                City seed = all[i];
                if (seed == null || _registered.Contains(seed)) continue;

                var bucket = new List<City>(capacity);
                queue.Clear();
                queue.Enqueue(seed);
                while (queue.Count > 0 && bucket.Count < capacity)
                {
                    City c = queue.Dequeue();
                    if (c == null || _registered.Contains(c)) continue;
                    _registered.Add(c);
                    bucket.Add(c);

                    // 相邻优先：让同一桶尽量是连片疆域（0229 的空间聚簇意图）
                    // neighbours_cities 是 internal，访问失败时退化成分桶不聚簇（只告警一次）
                    if (!_neighbourAccessFailed)
                    {
                        try
                        {
                            var neighbours = c.neighbours_cities;
                            if (neighbours != null)
                            {
                                foreach (City nb in neighbours)
                                {
                                    if (nb != null && !_registered.Contains(nb)) queue.Enqueue(nb);
                                }
                            }
                        }
                        catch (Exception ne)
                        {
                            _neighbourAccessFailed = true;
                            Log.Warn("neighbours_cities 访问失败，UPP 退化为顺序分桶：" + ne.GetType().Name + ": " + ne.Message);
                        }
                    }
                }

                if (bucket.Count > 0) _buckets.Add(bucket);
            }

            BucketCount = _buckets.Count;
            if (BucketCount > 0 && Cursor >= BucketCount) Cursor = 0;
            RefreshLive();
            _builtForCityCount = cityCount;
            _dirty = false;

            Log.Info($"UPP 桶已重建：城市 {cityCount} 个 → {BucketCount} 桶（每桶上限 {capacity}）");
        }

        /// <summary>每帧由 CityManager.update 的 Prefix 调用：推进游标 + 消耗宽限期。</summary>
        internal static void Advance()
        {
            if (BucketCount > 1)
            {
                Cursor++;
                if (Cursor >= BucketCount) Cursor = 0;
            }
            else
            {
                Cursor = 0;
            }

            if (ForceRefresh > 0) ForceRefresh--;
            RefreshLive();
        }

        private static void RefreshLive()
        {
            _live.Clear();
            if (BucketCount == 0 || Cursor < 0 || Cursor >= BucketCount) return;

            List<City> bucket = _buckets[Cursor];
            for (int i = 0; i < bucket.Count; i++)
            {
                City c = bucket[i];
                if (c == null) continue;
                _live.Add(c);
                if (_neighbourAccessFailed) continue;
                try
                {
                    var neighbours = c.neighbours_cities;
                    if (neighbours == null) continue;
                    foreach (City nb in neighbours)
                    {
                        if (nb != null) _live.Add(nb);
                    }
                }
                catch
                {
                    _neighbourAccessFailed = true;
                }
            }
        }

        /// <summary>记录某城连续被拒绝思考的帧数（饥饿保护用）。</summary>
        internal static int NoteDenied(City city)
        {
            if (city == null) return 0;
            _denyStreak.TryGetValue(city, out int n);
            n++;
            _denyStreak[city] = n;
            return n;
        }

        internal static void NoteAllowed(City city)
        {
            if (city == null) return;
            if (_denyStreak.Count > 0) _denyStreak.Remove(city);
        }

        internal static bool IsAllowed(City city)
        {
            if (ForceRefresh > 0) return true;
            return city != null && _live.Contains(city);
        }

        internal static string Describe()
        {
            return $"桶={BucketCount} 游标={Cursor} 命中集={_live.Count} 宽限={ForceRefresh}";
        }
    }
}
