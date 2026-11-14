using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SKNRM
{
    /// <summary>
    /// 防崩补丁集合：把实测过的几类崩溃/刷屏在"读取点"拦住，绝不把 null 传给下游。
    ///
    ///   ① 空投射物 ID      → Projectile.start 的 ArgumentNullException
    ///   ② 坏装备精灵        → ItemRendering / DynamicSprites 的 NRE
    ///   ③ 资源精灵缺失      → ResourceAsset.getGameplaySprite 的 NRE
    ///   ④ 名牌不可见        → 整段 NameplateManager.update 白算
    ///   ⑤ 进攻行为空目标    → BehCityActorCheckAttack 的 NRE（原版不判空）
    ///
    /// 原则：能"跳过整段逻辑"就不"替换返回值"；必须返回值时给合法替代物，绝不给 null。
    /// </summary>
    internal static class Guards
    {
        private static int _badProjectiles, _badItems, _nameplateSkips;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            int ok = 0;
            ok += Patch(harmony, typeof(ProjectileManager), "spawn",
                new[] { typeof(BaseSimObject), typeof(BaseSimObject), typeof(string), typeof(Vector3), typeof(Vector3), typeof(float), typeof(float), typeof(Action), typeof(Kingdom) },
                nameof(ProjectileSpawnPrefix), null, "投射物空 ID");
            ok += Patch(harmony, typeof(CombatActionLibrary), "attackRangeAction", new[] { typeof(AttackData) },
                nameof(RangeActionPrefix), null, "远程攻击空投射物");
            ok += Patch(harmony, typeof(Actor), "checkHasRenderedItem", Type.EmptyTypes,
                null, nameof(HasRenderedItemPostfix), "坏装备精灵");
            ok += Patch(harmony, typeof(NameplateManager), "update", Type.EmptyTypes,
                nameof(NameplatePrefix), null, "名牌 LOD");
            ok += Patch(harmony, typeof(ResourceAsset), "getGameplaySprite", Type.EmptyTypes,
                nameof(GameplaySpritePrefix), null, "资源主精灵兜底");
            ok += Patch(harmony, typeof(ResourceAsset), "getSprites", Type.EmptyTypes,
                nameof(ResourceSpritesPrefix), null, "资源精灵数组兜底");
            ok += Patch(harmony, typeof(ai.behaviours.BehCityActorCheckAttack), "execute", new[] { typeof(Actor) },
                nameof(CheckAttackPrefix), null, "进攻行为空目标");
            ok += Patch(harmony, typeof(MapBox), "finishingUpLoading", Type.EmptyTypes,
                null, nameof(WorldLoadedPostfix), "读档钩子");
            Log.Info("防护补丁安装 " + ok + "/8（投射物/远程/坏装备/名牌/资源精灵×2/进攻行为/读档钩子）");
        }

        internal static int Patch(HarmonyLib.Harmony harmony, Type type, string method, Type[] args,
            string prefixName, string postfixName, string label)
        {
            try
            {
                var target = AccessTools.Method(type, method, args);
                if (target == null)
                {
                    Log.Warn("防护跳过（方法不存在）：" + label);
                    return 0;
                }
                harmony.Patch(target,
                    prefix: prefixName == null ? null : new HarmonyMethod(typeof(Guards), prefixName),
                    postfix: postfixName == null ? null : new HarmonyMethod(typeof(Guards), postfixName));
                return 1;
            }
            catch (Exception e)
            {
                Log.Warn("防护安装失败：" + label + " -> " + e.Message);
                return 0;
            }
        }

        // ── ① 空投射物 ──
        private static bool ProjectileAssetOk(string pAssetID)
        {
            if (string.IsNullOrEmpty(pAssetID)) return false;
            try { return AssetManager.projectiles != null && AssetManager.projectiles.get(pAssetID) != null; }
            catch { return false; }
        }

        private static bool ProjectileSpawnPrefix(string pAssetID, ref Projectile __result)
        {
            try
            {
                if (!PerfConfig.I.GuardBadProjectiles) return true;
                if (ProjectileAssetOk(pAssetID)) return true;
                _badProjectiles++;
                if (_badProjectiles <= 3 || PerfConfig.I.Verbose)
                    Log.Warn("已拦下空投射物：assetID=" + (pAssetID ?? "<null>") + "（累计 " + _badProjectiles + " 次）");
                __result = null;
                return false;
            }
            catch (Exception e) { PerfStats.OnException(e); return true; }
        }

        private static bool RangeActionPrefix(AttackData pData)
        {
            try
            {
                if (!PerfConfig.I.GuardBadProjectiles) return true;
                if (!pData.is_projectile) return true;
                if (ProjectileAssetOk(pData.projectile_id)) return true;
                _badProjectiles++;
                return false;
            }
            catch (Exception e) { PerfStats.OnException(e); return true; }
        }

        // ── ② 坏装备精灵：不进入物品渲染分支 ──
        private static void HasRenderedItemPostfix(Actor __instance, ref bool __result)
        {
            try
            {
                if (!__result || !PerfConfig.I.GuardBadItemSprites || __instance == null) return;
                IHandRenderer hr = null;
                try { hr = __instance.getCachedHandRendererAsset(); } catch { }
                if (hr == null) { try { hr = __instance.getHandRendererAsset(); } catch { } }
                if (hr == null) return;

                Sprite[] sprites = null;
                try { sprites = hr.getSprites(); } catch { }
                if (sprites != null && sprites.Length > 0 && sprites[0] != null) return;

                _badItems++;
                if (_badItems <= 3 || PerfConfig.I.Verbose)
                {
                    string id = "?";
                    try { id = hr.getID(); } catch { }
                    Log.Warn("坏装备：" + id + " 精灵为空 → 该单位跳过物品渲染（累计 " + _badItems + " 次）");
                }
                __result = false;
            }
            catch (Exception e) { PerfStats.OnException(e); }
        }

        // ── ③ 资源精灵：只在读取处兜底，绝不改写资产数组 ──
        private static bool GameplaySpritePrefix(ResourceAsset __instance, ref Sprite __result)
        {
            try
            {
                if (!PerfConfig.I.FixResourceSprites || __instance == null) return true;
                Sprite[] arr = null;
                try { arr = __instance.getSprites(); } catch { }
                if (arr != null && arr.Length > 0 && arr[0] != null) return true;
                Sprite icon = null;
                try { icon = __instance.getSpriteIcon(); } catch { }
                if (icon == null) return true;
                __result = icon;
                return false;
            }
            catch (Exception e) { PerfStats.OnException(e); return true; }
        }

        private static bool ResourceSpritesPrefix(ResourceAsset __instance, ref Sprite[] __result)
        {
            try
            {
                if (!PerfConfig.I.FixResourceSprites || __instance == null) return true;
                var f = AccessTools.Field(typeof(ResourceAsset), "gameplay_sprites");
                Sprite[] arr = f != null ? f.GetValue(__instance) as Sprite[] : null;
                if (arr != null && arr.Length > 0 && arr[0] != null) return true;
                __result = null;   // 让"坏装备防护"跳过物品渲染，避免图集查不到
                return false;
            }
            catch (Exception e) { PerfStats.OnException(e); return true; }
        }

        // ── ④ 名牌 LOD ──
        private static bool _nameplateHidden;

        private static bool NameplatePrefix(NameplateManager __instance)
        {
            try
            {
                if (!PerfConfig.I.SkipNameplatesWhenHidden) return true;
                bool allowed;
                try { allowed = CanvasMain.isNameplatesAllowed(); } catch { return true; }
                if (allowed) { _nameplateHidden = false; return true; }
                if (!_nameplateHidden)
                {
                    try { if (__instance != null && __instance.gameObject != null) __instance.gameObject.SetActive(false); } catch { }
                    _nameplateHidden = true;
                }
                _nameplateSkips++;
                return false;
            }
            catch (Exception e) { PerfStats.OnException(e); return true; }
        }

        // ── ⑤ 进攻行为空目标（原版不判空）──
        private static bool CheckAttackPrefix(Actor pActor, ref ai.behaviours.BehResult __result)
        {
            try
            {
                if (pActor == null || pActor.city == null) { __result = ai.behaviours.BehResult.Stop; return false; }
                var zone = pActor.city.target_attack_zone;
                if (zone == null || zone.city == null) { __result = ai.behaviours.BehResult.Stop; return false; }
            }
            catch { }
            return true;
        }

        // ── ⑥ 读档钩子 ──
        private static void WorldLoadedPostfix()
        {
            try { SknFeature.NoteWorldLoaded(); } catch (Exception e) { PerfStats.OnException(e); }
            try { PerfTicker.ResetHeapBaseline(); } catch { }
        }
    }
}
