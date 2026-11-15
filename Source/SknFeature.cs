using System;
using System.Collections.Generic;

namespace SKNRM
{
    /// <summary>
    /// SKN 语义复刻（大日春秋 3.1.6）+ 「军队永不被冻结」的硬保护。
    ///
    /// 基本规则：
    ///   1) 年度闸门：每个游戏年重算一次 Enabled = units.Count &gt; 「SKN人口上限」(默认 3000)；
    ///   2) 三条件同时成立才生效：SknPlus 开关 + 年度闸门 + 相机像素视图(lowRes 等价物)；
    ///   3) 在此前提下，普通市民的 b4/b6_0/b6 被整批跳过，其余照常；
    ///   4) 城市 zone 数 &gt; 阈值的市民才参与冻结。
    ///
    /// 硬保护（ProtectArmies / ProtectAtWar，默认都开）：以下角色**永不冻结**
    ///   · 军队成员（actor.army != null）
    ///   · 战士职业（isWarrior()）
    ///   · 任何军队的队长（即使队长本人没被计入 army.units —— setCaptain 不改 actor.army）
    ///   · 所属王国正在打仗
    ///   · 国王 / 城市领袖 / 有 PI、SKN 标签
    /// 队长与交战王国都是"缓存 + 定期刷新"，热路径上只做 HashSet.Contains。
    /// 719 映射：unit_group → army、isBoat → asset.is_boat、lowRes → MapBox.isRenderMiniMap()。
    /// </summary>
    internal static class SknFeature
    {
        internal static bool Enabled;
        internal static long UnitsAtGate;
        internal static int LastYear = -1;
        internal static bool EverComputed;

        private static readonly HashSet<Actor> _captains = new HashSet<Actor>();
        private static readonly HashSet<Kingdom> _warKingdoms = new HashSet<Kingdom>();
        private static int _protectCacheAge = int.MaxValue;
        private static float _loadGraceUntil;

        /// <summary>世界载入完成时调用：开一段"加速宽限"，让读大存档的头几秒别卡死。</summary>
        /// <summary>是否已经历过一次世界载入（堆基线等只在载入后记录）。</summary>
        internal static bool WorldLoadedOnce;

        internal static void NoteWorldLoaded()
        {
            var cfg = PerfConfig.I;
            if (cfg.LoadGraceSeconds > 0f)
            {
                _loadGraceUntil = UnityEngine.Time.unscaledTime + cfg.LoadGraceSeconds;
                Log.Info($"世界载入完成：开启 {cfg.LoadGraceSeconds:F0} 秒宽限节流（市民 1/{cfg.LoadGraceThrottle}）");
            }
        }

        internal static bool InLoadGrace()
        {
            return _loadGraceUntil > 0f && UnityEngine.Time.unscaledTime < _loadGraceUntil;
        }

        internal static void ResetRuntimeState()
        {
            Enabled = false;
            UnitsAtGate = 0;
            LastYear = -1;
            WorldLoadedOnce = true;
            EverComputed = false;
            _captains.Clear();
            _warKingdoms.Clear();
            _protectCacheAge = int.MaxValue;
            UppDispatcher.Reset(false);
        }

        /// <summary>由 MapBox.updateObjectAge 的 Postfix 调用（719: 每年一次）。</summary>
        internal static void OnYearTick()
        {
            try
            {
                var cfg = PerfConfig.I;
                long count = CountUnits();
                bool old = Enabled;
                Enabled = count > cfg.SknPopulationThreshold;
                UnitsAtGate = count;
                EverComputed = true;
                try
                {
                    LastYear = Date.getCurrentYear();
                }
                catch
                {
                    LastYear = -1;
                }

                if (old != Enabled || cfg.Verbose)
                {
                    Log.Info($"年度闸门重算：单位 {count} vs 阈值 {cfg.SknPopulationThreshold} => sknEnable={Enabled}"
                             + (LastYear >= 0 ? $"（第 {LastYear} 年）" : string.Empty));
                }

                if (cfg.AutoTagImportant) TagImportantActors();
                RefreshProtectionCaches(force: true);

                // UPP「防掉压」：关着的时候，每年补一段宽限期（与 0229 一致）
                if (cfg.Unfair && !cfg.UnfairAntiDrop)
                {
                    UppDispatcher.ForceRefresh = cfg.UppGraceFrames;
                }
            }
            catch (Exception e)
            {
                Log.Warn("OnYearTick 异常：" + e.Message);
            }
        }

        private static void EnsureComputed()
        {
            if (EverComputed) return;
            EverComputed = true;
            long count = CountUnits();
            UnitsAtGate = count;
            Enabled = count > PerfConfig.I.SknPopulationThreshold;
            Log.Info($"闸门首次计算：单位 {count} vs 阈值 {PerfConfig.I.SknPopulationThreshold} => sknEnable={Enabled}");
        }

        private static long CountUnits()
        {
            try
            {
                return World.world != null && World.world.units != null ? World.world.units.Count : 0L;
            }
            catch
            {
                return 0L;
            }
        }

        /// <summary>SKN 总闸：开关 + 年度闸门 +（可选）像素视图。</summary>
        internal static bool MasterGateOpen()
        {
            var cfg = PerfConfig.I;
            if (!cfg.SknPlus) return false;
            EnsureComputed();
            // 自愈：读档后的第一帧还没有年度快照（UnitsAtGate=0），此时用实时人口重算，避免"闸门=关"导致完全不节流
            if (UnitsAtGate <= 0L)
            {
                long live = CountUnits();
                if (live > 0L)
                {
                    UnitsAtGate = live;
                    Enabled = live > PerfConfig.I.SknPopulationThreshold;
                }
            }
            if (!Enabled) return false;
            // SknOnlyInvisible：视角判定下沉到 ShouldFreeze 逐角色做（像素视图=全图节流；拉近=只节流镜头外）
            if (cfg.SknOnlyInvisible) return true;
            if (!cfg.UseLowResGate) return true;
            try
            {
                return MapBox.isRenderMiniMap();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 刷新"军队队长 / 交战王国"缓存。成本 O(军队数 + 王国数)，由 ticker 每秒调一次，
        /// 不在每个 actor 的热路径上跑。
        /// </summary>
        internal static void RefreshProtectionCaches(bool force = false)
        {
            var cfg = PerfConfig.I;
            if (!cfg.ProtectArmies && !cfg.ProtectAtWar) return;
            if (!force && _protectCacheAge < 60) { _protectCacheAge++; return; }
            _protectCacheAge = 0;

            try
            {
                _captains.Clear();
                if (cfg.ProtectArmies && World.world != null && World.world.armies != null)
                {
                    var armies = World.world.armies.list;
                    for (int i = 0; i < armies.Count; i++)
                    {
                        Army a = armies[i];
                        if (a == null || !a.hasCaptain()) continue;
                        Actor captain = a.getCaptain();
                        if (captain != null) _captains.Add(captain);
                    }
                }
            }
            catch (Exception e)
            {
                PerfStats.OnException(e);
            }

            try
            {
                _warKingdoms.Clear();
                if (cfg.ProtectAtWar && World.world != null && World.world.kingdoms != null)
                {
                    var kingdoms = World.world.kingdoms.list;
                    for (int i = 0; i < kingdoms.Count; i++)
                    {
                        Kingdom k = kingdoms[i];
                        if (k == null) continue;
                        try
                        {
                            foreach (War w in k.getWars())
                            {
                                if (w == null) continue;
                                _warKingdoms.Add(k);
                                break;
                            }
                        }
                        catch
                        {
                            // 单个王国查询失败不影响其它
                        }
                    }
                }
            }
            catch (Exception e)
            {
                PerfStats.OnException(e);
            }
        }

        /// <summary>关键单位：国王 / 城市领袖 / 军队队长 / 任何在军队里的单位 —— 永不节流。</summary>
        /// <summary>
        /// 是否"智慧单位"。三种来源任一成立即算：
        ///   · 资产本身就是文明单位（asset.civ）；
        ///   · 亚种已被"赋予智慧"（Subspecies.isSapient，游戏里点化动物走这条）；
        ///   · 已加入文明王国（Kingdom.isCiv）。
        /// 只有三者都不成立（野兽/怪物）才会被 DisableAnimalAI 冻结。
        /// </summary>
        private static bool IsSapientUnit(Actor actor, ActorAsset asset)
        {
            try
            {
                if (asset != null && asset.civ) return true;
                if (actor.subspecies != null && actor.subspecies.isSapient()) return true;
                if (actor.kingdom != null && actor.kingdom.isCiv()) return true;
            }
            catch { }
            return false;
        }

        private static bool IsEssentialActor(Actor actor)
        {
            try
            {
                if (actor.isKing()) return true;
                if (actor.isCityLeader()) return true;
                City c = actor.city;
                if (c != null && c.leader != null && ReferenceEquals(c.leader, actor)) return true;
                if (IsProtectedCaptain(actor)) return true;
                if (actor.hasArmy() || actor.isWarrior()) return true;
            }
            catch { }
            return false;
        }

        private static bool SafeIsFighting(Actor actor)
        {
            try { return actor != null && actor.isFighting(); } catch { return true; }
        }

        private static bool SafeHasAttackTarget(Actor actor)
        {
            try { return actor != null && actor.has_attack_target; } catch { return true; }
        }

        internal static bool IsProtectedCaptain(Actor actor)
        {
            return actor != null && _captains.Contains(actor);
        }

        /// <summary>
        /// 这座城市是不是"前线"：与敌国城市相邻。用于把「打仗保护」从"整个王国的所有城"收窄到真正需要 AI 的边境城。
        /// 依据 719 的 City.neighbours_kingdoms（相邻王国集合）+ Kingdom.isInWarWith。
        /// </summary>
        internal static bool IsFrontlineCity(City city)
        {
            if (city == null) return false;
            try
            {
                Kingdom mine = city.kingdom;
                if (mine == null) return false;
                var neighbours = city.neighbours_kingdoms;
                if (neighbours == null) return false;
                foreach (Kingdom other in neighbours)
                {
                    if (other == null || other == mine) continue;
                    if (mine.isInWarWith(other)) return true;
                }
            }
            catch (Exception e)
            {
                PerfStats.OnException(e);
            }
            return false;
        }

        internal static bool IsKingdomAtWar(Kingdom kingdom)
        {
            return kingdom != null && _warKingdoms.Count > 0 && _warKingdoms.Contains(kingdom);
        }

        private static readonly HashSet<string> _throttleJobs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static string _throttleJobsRaw;

        private static void EnsureJobSet()
        {
            string raw = PerfConfig.I.SknKeepJobs ?? string.Empty;
            if (ReferenceEquals(raw, _throttleJobsRaw) || raw == _throttleJobsRaw) return;
            _throttleJobsRaw = raw;
            _throttleJobs.Clear();
            foreach (string part in raw.Split(','))
            {
                string id = part.Trim();
                if (id.Length > 0) _throttleJobs.Add(id);
            }
        }

        /// <summary>角色当前 tile 的 zone 是否在镜头内（719 用 TileZone.visible 表示）。</summary>
        internal static bool IsInView(Actor actor)
        {
            try
            {
                WorldTile tile = actor.current_tile;
                if (tile == null) return false;
                TileZone zone = tile.zone;
                return zone != null && zone.visible;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>角色当前 job 是否属于"可以延后"的生产类（白名单模式才有意义）。</summary>
        internal static bool IsJobThrottleable(Actor actor)
        {
            try
            {
                var ai = actor.ai;
                if (ai == null) return false;
                EnsureJobSet();
                // job 还没分配（null）→ 视为"可节流"，否则开局大量角色会因为 job==null 被永久保护
                if (ai.job == null) return true;
                string id = ai.job.id;
                if (string.IsNullOrEmpty(id)) return true;
                return !_throttleJobs.Contains(id);   // 名单里的 = 保活，不节流
            }
            catch
            {
                return false;
            }
        }

        // ── 每帧记忆化：b4 / b6_0 / b6 三个入口 + b5 会对同一个角色问 3-4 次，同一帧答案必然相同。
        // [ThreadStatic]：719 的 Post 阶段按批次并行，线程间不共享缓存，也不需要锁。
        [ThreadStatic] private static int _memoFrame;
        [ThreadStatic] private static Actor _memoActor;
        [ThreadStatic] private static bool _memoResult;

        internal static bool ShouldFreeze(Actor actor)
        {
            if (actor == null) return false;
            int frame = UnityEngine.Time.frameCount;
            if (frame == _memoFrame && ReferenceEquals(actor, _memoActor)) return _memoResult;
            bool result = ShouldFreezeInternal(actor);
            _memoFrame = frame;
            _memoActor = actor;
            _memoResult = result;
            return result;
        }

        private static bool ShouldFreezeInternal(Actor actor)
        {
            if (actor == null) return false;
            if (!MasterGateOpen()) return false;

            var asset = actor.asset;
            if (asset == null) return false;
            if (!asset.has_ai_system) return false;
            if (asset.is_boat) return false;

            // ── 非智慧动物：按配置完全禁用 AI（默认开，最省算力）──
            // 关掉 DisableAnimalAI 则动物完全不受节流影响（行为与原版一致）。
            // 注意：世界里的"赋予智慧"是把**亚种**变成 sapient（Subspecies.makeSapient），
            // 单位资产仍是动物 asset（asset.civ 依旧为 false）。所以判断必须看亚种/王国，
            // 否则被点化的熊/狼人也会被永久冻结。
            if (!IsSapientUnit(actor, asset)) return PerfConfig.I.DisableAnimalAI;

            // ── 硬保护：正在赶路的单位永不节流 ──
            // 有移动目标却跳过 ai.update() 时，路径与决策不同步，表现就是来回蠕动。
            try { if (actor.beh_tile_target != null) return false; } catch { }

            // ── 硬保护 0：关键单位永不冻结（国王 / 城市领袖 / 军队队长 / 军队成员）──
            // 这几类一旦被跳过 AI，表现就是"国王、村长、军队一动不动"，代价远大于收益。
            if (IsEssentialActor(actor)) return false;

            // ── 硬保护 0.5：正在执行动作的单位不跳 ──
            // 跳过 ai.update() 会同时掐掉"动作推进"，一旦单位正处于某个动作里（走路/砍树/搬运/战斗），
            // 表现就是"一动不动"。所以只节流"动作之间"的单位，动作执行一律每帧跑。
            try { if (actor.ai != null && actor.ai.action != null) return false; } catch { }

            var cfg = PerfConfig.I;

            // ── 硬保护 1：军队相关（按 ArmyProtectMode 分级）──
            if (cfg.ProtectArmies)
            {
                bool isArmy = actor.hasArmy() || actor.isWarrior() || IsProtectedCaptain(actor);
                if (isArmy)
                {
                    bool protect = true;
                    if (cfg.ArmyProtectMode >= 2)
                    {
                        protect = false;                       // 最省：军队也不豁免
                    }
                    else if (cfg.ArmyProtectMode == 1)
                    {
                        // 只保"真正在打"的：战斗中 / 有攻击目标 / 队长 / 所在城前线或被围
                        protect = IsProtectedCaptain(actor)
                                  || SafeIsFighting(actor)
                                  || SafeHasAttackTarget(actor)
                                  || IsFrontlineCity(actor.city)
                                  || (actor.city != null && actor.city.isGettingCaptured());
                    }

                    if (protect)
                    {
                        int armyN = cfg.ArmyThrottleFrames;
                        // 士兵 + 队长没在打仗 → 用更狠的 ArmyIdleThrottle（行军/待命几乎不思考）
                        try
                        {
                            if (cfg.ArmyIdleThrottle > armyN && actor.isArmyGroupWarrior())
                            {
                                Army cap = actor.army;
                                Actor captain = cap != null ? cap.getCaptain() : null;
                                bool captainFighting = captain != null && (SafeIsFighting(captain) || SafeHasAttackTarget(captain));
                                if (!captainFighting) armyN = cfg.ArmyIdleThrottle;
                            }
                        }
                        catch
                        {
                        }
                        if (armyN <= 1)
                        {
                            PerfStats.Skip(ref PerfStats.ProtectedArmy);
                            return false;
                        }
                        long aid = 0L;
                        try
                        {
                            aid = actor.getID();
                        }
                        catch
                        {
                            PerfStats.Skip(ref PerfStats.ProtectedArmy);
                            return false;
                        }
                        if (((UnityEngine.Time.frameCount + (int)(aid % armyN)) % armyN) != 0)
                        {
                            PerfStats.Skip(ref PerfStats.ArmyThrottled);
                            return true;   // 本帧不思考；位移/动画仍由 u10/u3 跑
                        }
                        PerfStats.Skip(ref PerfStats.ProtectedArmy);
                        return false;
                    }
                }
            }

            // ── 硬保护 2：在打仗的王国，居民一律不冻结（补充兵员/撤退都要脑子）──
            if (cfg.ProtectAtWar && IsKingdomAtWar(actor.kingdom))
            {
                bool protect;
                if (cfg.WarProtectScope <= 0)
                {
                    protect = true;                       // 0：整个交战王国都豁免
                }
                else if (cfg.WarProtectScope == 1)
                {
                    City wc = actor.city;                 // 1：只豁免前线/首都/被围城
                    protect = wc == null
                              || IsFrontlineCity(wc)
                              || wc.isCapitalCity()
                              || wc.isGettingCaptured();
                }
                else
                {
                    // 2（默认）：交战国平民照常节流，只豁免"正在被围攻的城市"的居民
                    City wc2 = actor.city;
                    protect = wc2 != null && wc2.isGettingCaptured();
                }
                if (protect)
                {
                    PerfStats.Skip(ref PerfStats.ProtectedWar);
                    return false;
                }
            }

            if (cfg.SknRequireCity)
            {
                City city = actor.city;
                if (city == null) return false;
                if (city.countZones() <= cfg.SknCityZoneThreshold) return false;
            }

            // ── 硬保护 3：拉近看的时候，镜头内的角色一律满速（像素视图下不生效，那时全图都"可见"）──
            if (cfg.SknOnlyInvisible)
            {
                bool pixelView;
                try
                {
                    pixelView = !PerfConfig.I.SelfTestForceNearView && MapBox.isRenderMiniMap();
                }
                catch
                {
                    pixelView = false;
                }
                if (!pixelView && IsInView(actor))
                {
                    PerfStats.Skip(ref PerfStats.ViewProtected);
                    return false;
                }
            }

            // ── 硬保护 4：只节流生产类 job，让 random_move / attacker 等保持满速 ──
            if (cfg.SknJobWhitelist && !IsJobThrottleable(actor))
            {
                PerfStats.Skip(ref PerfStats.JobProtected);
                return false;
            }

            if (PerfTags.HasPi(actor) || PerfTags.HasSkn(actor)) return false;

            return true;
        }

        /// <summary>
        /// 「这一帧要不要跳过这个角色」——在 ShouldFreeze（分类：是不是普通市民）之上再叠一层节流策略：
        ///   SknThrottleFrames &lt;= 1 → 硬冻结（0229 原语义，最省帧，但角色会长期不动）
        ///   SknThrottleFrames = N  → 每个市民每 N 帧才思考一次（决策变慢 N 倍，但人一直在动、一直在干活）
        /// 相位用 actor id 打散，避免同一帧所有角色一起想或一起停。
        /// </summary>
        /// <summary>当前生效的节流周期（自适应模式下随人口线性放大，上限 SknThrottleFrames）。</summary>
        internal static int EffectiveThrottleFrames()
        {
            var cfg = PerfConfig.I;
            int max = cfg.SknThrottleFrames;
            if (max <= 1) return max;
            if (!cfg.SknAdaptiveThrottle) return max;

            long threshold = cfg.SknPopulationThreshold > 0 ? cfg.SknPopulationThreshold : 1;
            long ratio = UnitsAtGate / threshold;
            if (ratio < 1) ratio = 1;
            if (ratio > max) ratio = max;
            return (int)ratio;
        }

        internal static bool ShouldSkipThisFrame(Actor actor)
        {
            if (!ShouldFreeze(actor)) return false;
            if (BulkGuard.Active) return false;   // 大规模清除/重建期间不节流
            int n = EffectiveThrottleFrames();

            // ── 征兵放宽（严格版）──
            // 只对"真正在等工作的市民 + 城市确实缺战士"放宽，否则会把大量市民的 AI 从 1/8 提到 1/2，
            // 直接把帧率打回去（实测 6.7k 单位从 ~60fps 掉到 ~32fps 就是这个原因）。
            if (n > 2)
            {
                try
                {
                    City c = actor.city;
                    int deficit = 0;
                    if (c != null && c.status != null)
                        deficit = c.status.warrior_slots - c.status.warriors_current;

                    if (deficit >= 2 && actor.citizen_job == null)
                    {
                        int seekerN = PerfConfig.I.JobSeekerThrottle;
                        if (seekerN > 1 && seekerN < n)
                        {
                            n = seekerN;
                            PerfStats.Skip(ref PerfStats.JobSeekerBoost);
                        }
                    }
                    else if (deficit >= 2 && PerfConfig.I.WarCivilianThrottle > 1 && IsKingdomAtWar(actor.kingdom))
                    {
                        int warN = PerfConfig.I.WarCivilianThrottle;
                        if (warN > 1 && warN < n)
                        {
                            n = warN;
                            PerfStats.Skip(ref PerfStats.WarCivilianBoost);
                        }
                    }
                }
                catch { }
            }
            if (n <= 1) return false;   // 周期 1 = 每帧都跑，绝不能跳过

            long id = 0L;
            try
            {
                id = actor.getID();
            }
            catch
            {
                return true;
            }

            int phase = (int)(id % n);
            bool skip = ((UnityEngine.Time.frameCount + phase) % n) != 0;
            if (!skip) PerfStats.Skip(ref PerfStats.ThrottleAllowed);
            return skip;
        }

        /// <summary>饥饿停摆（0229 是全局的，不分角色，这里保持一致）。</summary>
        internal static bool FreezeNutritionActive()
        {
            return PerfConfig.I.FreezeNutrition && MasterGateOpen();
        }

        /// <summary>年度给"在职重要人物"打 PI：国王 + 城市领袖。</summary>
        internal static void TagImportantActors()
        {
            try
            {
                if (World.world == null) return;
                if (World.world.kingdoms != null)
                {
                    var kingdoms = World.world.kingdoms.list;
                    for (int i = 0; i < kingdoms.Count; i++)
                    {
                        Kingdom k = kingdoms[i];
                        if (k == null || !k.isCiv()) continue;
                        if (k.king != null) PerfTags.Add(k.king, PerfTags.PI);
                    }
                }

                if (World.world.cities != null)
                {
                    var cities = World.world.cities.list;
                    for (int i = 0; i < cities.Count; i++)
                    {
                        City c = cities[i];
                        if (c == null) continue;
                        if (c.leader != null) PerfTags.Add(c.leader, PerfTags.PI);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warn("TagImportantActors 异常：" + e.Message);
            }
        }

        /// <summary>玩家点开角色信息窗 → 打 SKN（对应大日"看过就保护"那条路）。</summary>
        internal static void TagInspected(Actor actor)
        {
            if (!PerfConfig.I.TagInspectedActors) return;
            if (actor == null) return;
            PerfTags.Add(actor, PerfTags.SKN);
        }
    }
}
