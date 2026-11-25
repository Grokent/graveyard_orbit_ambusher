using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace GraveyardOrbit.Ambusher
{
    /// <summary>
    /// Presentation and quality-of-life for the carrier:
    /// - a distinguishable turret core on reveal
    /// - a health bar visible even before the first hit
    /// - optional reveal-at-start for carriers
    /// </summary>
    internal static class CarrierPresentation
    {
        [HarmonyPatch(typeof(EnemyManager), "TickHealthBars")]
        private static class HealthBarPatch
        {
            private static void Postfix(EnemyManager __instance)
            {
                try
                {
                    IList active = Reflect.Get<IList>(__instance, "_active");
                    if (active == null)
                    {
                        return;
                    }

                    for (int i = 0; i < active.Count; i++)
                    {
                        object enemy = active[i];
                        if (enemy == null || !IsCarrier(enemy))
                        {
                            continue;
                        }

                        if (Reflect.Get<GameObject>(enemy, "bar") == null)
                        {
                            AccessTools.Method(typeof(EnemyManager), "MakeHealthBar")?.Invoke(__instance, new[] { enemy });
                        }

                        if (Reflect.Get<GameObject>(enemy, "bar") != null)
                        {
                            AccessTools.Method(typeof(EnemyManager), "PlaceHealthBar")?.Invoke(__instance, new[] { enemy });
                        }
                    }
                }
                catch (System.Exception e)
                {
                    Plugin.Log?.LogWarning($"[ambusher] health bar failed: {e.Message}");
                }
            }
        }

        [HarmonyPatch(typeof(EnemyManager), nameof(EnemyManager.SeedAtCellsExplicit))]
        private static class RevealAtSeedPatch
        {
            private static void Postfix(EnemyManager __instance, IEnumerable<(Vector2Int cell, string kind)> placements)
            {
                if (!Plugin.CarriersVisibleAtStart.Value || placements == null)
                {
                    return;
                }

                try
                {
                    List<Vector2Int> cells = new List<Vector2Int>();
                    foreach ((Vector2Int cell, string kind) in placements)
                    {
                        if (kind == "ambusher")
                        {
                            cells.Add(cell);
                        }
                    }

                    if (cells.Count == 0)
                    {
                        return;
                    }

                    System.Reflection.MethodInfo reveal = AccessTools.Method(typeof(EnemyManager), "Reveal");
                    IList dormant = Reflect.Get<IList>(__instance, "_dormant");

                    foreach (Vector2Int cell in cells)
                    {
                        reveal?.Invoke(__instance, new object[] { cell, "ambusher" });
                        RemoveDormant(dormant, cell);
                    }

                    IList active = Reflect.Get<IList>(__instance, "_active");
                    string state = "no active list";
                    if (active != null && active.Count > 0)
                    {
                        object last = active[active.Count - 1];
                        object cfg = Reflect.Get(last, "cfg");
                        state = cfg == null ? "cfg=NULL" : "cfg.kind=" + Reflect.Get<string>(cfg, "kind");
                    }

                    Plugin.Log?.LogInfo($"[ambusher] revealed {cells.Count} carrier(s) at sortie start ({state}, active={active?.Count ?? 0}).");
                }
                catch (System.Exception e)
                {
                    Plugin.Log?.LogWarning($"[ambusher] reveal-at-start failed: {e.Message}");
                }
            }
        }

        private static void RemoveDormant(IList dormant, Vector2Int cell)
        {
            if (dormant == null)
            {
                return;
            }

            for (int i = dormant.Count - 1; i >= 0; i--)
            {
                object entry = dormant[i];
                if (entry == null)
                {
                    continue;
                }

                Vector2Int entryCell = Reflect.Get<Vector2Int>(entry, "Item1");
                string entryKind = Reflect.Get<string>(entry, "Item2");
                if (entryCell == cell && entryKind == "ambusher")
                {
                    dormant.RemoveAt(i);
                }
            }
        }

        private static bool IsCarrier(object enemy)
        {
            object cfg = Reflect.Get(enemy, "cfg");
            return cfg != null && Reflect.Get<string>(cfg, "kind") == "ambusher";
        }

        internal static void RevealCarrier(EnemyManager manager, Vector2Int cell)
        {
            if (manager == null)
            {
                return;
            }

            IList dormant = Reflect.Get<IList>(manager, "_dormant");
            AccessTools.Method(typeof(EnemyManager), "Reveal")?.Invoke(manager, new object[] { cell, "ambusher" });

            if (dormant == null)
            {
                return;
            }

            for (int i = dormant.Count - 1; i >= 0; i--)
            {
                object entry = dormant[i];
                if (entry != null
                    && Reflect.Get<Vector2Int>(entry, "Item1") == cell
                    && Reflect.Get<string>(entry, "Item2") == "ambusher")
                {
                    dormant.RemoveAt(i);
                }
            }
        }
    }
}
