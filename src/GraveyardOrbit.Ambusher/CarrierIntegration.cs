using System.Collections.Generic;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GraveyardOrbit.Ambusher
{
    /// <summary>
    /// Registers the ambusher behaviour tile and wires it into the game's own
    /// seeding/placement paths (authored alt 16 and plugin-side band counts).
    /// </summary>
    internal static class CarrierIntegration
    {
        internal const int AltId = 16;
        internal const string TileId = "ambusher";

        private const string BehaviourJson =
            "{\"behavior\":{\"attack\":{\"type\":\"ambusher_spawn\",\"range\":900,\"interval\":1.1,\"max_live\":7}}}";

        internal static void RegisterAltId()
        {
            try
            {
                SandboxMap.AltToTile[AltId] = TileId;
                SandboxMap.TileToAlt[TileId] = AltId;
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"[ambusher] could not register alt id: {e.Message}");
            }
        }

        internal static void InjectTile()
        {
            try
            {
                if (GameData.Tiles == null || GameData.Tiles.ContainsKey(TileId))
                {
                    return;
                }

                GameData.Tiles[TileId] = new TileDef
                {
                    id = TileId,
                    altId = AltId,
                    label = "Ambusher",
                    role = "behavior",
                    baseHp = 7,
                    baseHpSealed = 7,
                    resourceValue = 1,
                    minLaserTier = 0,
                    behaviorKind = "ambusher",
                    revealRadius = 720,
                    hardens = false,
                    isMeta = false,
                    color = new Color(0.55f, 0.18f, 0.65f),
                    rawJson = BehaviourJson,
                    loot = new Dictionary<string, int>(),
                    materialCounts = new Dictionary<string, int>(),
                    behaviorCounts = new Dictionary<string, int>()
                };

                Plugin.Log?.LogInfo("[ambusher] injected behaviour tile 'ambusher' (alt 16).");
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogError($"[ambusher] tile injection failed: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(GameData), nameof(GameData.HydrateFromAsset))]
    internal static class GameDataAssetPatch
    {
        private static void Postfix()
        {
            CarrierIntegration.InjectTile();
        }
    }

    [HarmonyPatch(typeof(GameData), nameof(GameData.Hydrate))]
    internal static class GameDataJsonPatch
    {
        private static void Postfix()
        {
            CarrierIntegration.InjectTile();
        }
    }

    /// <summary>Adds alt-16 cells to the authored enemy placement list.</summary>
    [HarmonyPatch(typeof(MiningSortie), "AuthoredEnemyPlacements")]
    internal static class AuthoredAmbusherPatch
    {
        private static void Postfix(MegastructureData data, List<(Vector2Int cell, string tile)> __result)
        {
            if (data == null || data.alt == null || __result == null)
            {
                return;
            }

            if (GameState.I != null && CustomRun.Of(GameState.I).noEnemies)
            {
                return;
            }

            int limit = Mathf.Min(data.count, data.alt.Length);
            for (int i = 0; i < limit; i++)
            {
                if (data.alt[i] == CarrierIntegration.AltId)
                {
                    __result.Add((new Vector2Int(data.q[i], data.r[i]), CarrierIntegration.TileId));
                }
            }
        }
    }

    /// <summary>Seeds plugin-side band counts for contracts listed in ambusher_contracts.json.</summary>
    [HarmonyPatch(typeof(EnemyManager), nameof(EnemyManager.SeedPerLevel))]
    internal static class AmbusherSeedPatch
    {
        private static void Postfix(MegastructureData data, int seed, StructureDef def, EnemyManager __instance)
        {
            if (data == null || def == null || __instance == null || data.layer == null)
            {
                return;
            }

            if (GameState.I != null && CustomRun.Of(GameState.I).noEnemies)
            {
                return;
            }

            Dictionary<int, int> counts = AmbusherCounts.For(def.id);
            if (counts == null)
            {
                counts = AmbusherCounts.Wildcard;
            }

            if ((counts == null || counts.Count == 0) && Plugin.AutoSeedUnlisted.Value)
            {
                int total = Mathf.RoundToInt(data.count / (float)Mathf.Max(1000, Plugin.CellsPerCarrier.Value));
                total = Mathf.Clamp(total, Plugin.MinCarriers.Value, Plugin.MaxCarriers.Value);
                if (total > 0)
                {
                    counts = new Dictionary<int, int> { [1] = total };
                    Plugin.Log?.LogInfo($"[ambusher] auto-density for '{def.id}': {total} carrier(s) over {data.count:N0} cells.");
                }
            }

            if (counts == null || counts.Count == 0)
            {
                return;
            }

            System.Random rng = new System.Random(seed ^ 0x2F6E2B1);
            List<(Vector2Int cell, string kind)> placements = new List<(Vector2Int, string)>();

            foreach (KeyValuePair<int, int> entry in counts)
            {
                int layer = entry.Key;
                int remaining = entry.Value;
                List<Vector2Int> cells = new List<Vector2Int>();

                for (int i = 0; i < data.count; i++)
                {
                    if (data.layer[i] != layer)
                    {
                        continue;
                    }

                    int alt = data.alt != null && i < data.alt.Length ? data.alt[i] : 0;
                    if (alt != 0)
                    {
                        continue;
                    }

                    cells.Add(new Vector2Int(data.q[i], data.r[i]));
                }

                while (remaining-- > 0 && cells.Count > 0)
                {
                    int index = rng.Next(cells.Count);
                    placements.Add((cells[index], CarrierIntegration.TileId));
                    cells.RemoveAt(index);
                }
            }

            if (placements.Count > 0)
            {
                __instance.SeedAtCellsExplicit(placements);
                Plugin.Log?.LogInfo($"[ambusher] seeded {placements.Count} carrier(s) for '{def.id}'.");
            }
        }
    }

    [HarmonyPatch(typeof(EnemyManager), "BuildConfigs")]
    internal static class BuildConfigsPatch
    {
        private static void Postfix(EnemyManager __instance)
        {
            try
            {
                System.Collections.IDictionary cfg = Reflect.Get<System.Collections.IDictionary>(__instance, "_cfg");
                bool present = cfg != null && cfg.Contains(CarrierIntegration.TileId);
                Plugin.Log?.LogInfo($"[ambusher] carrier config present: {present}.");

                if (!present)
                {
                    return;
                }

                int highest = 0;
                object carrier = null;
                foreach (System.Collections.DictionaryEntry entry in cfg)
                {
                    string kind = Reflect.Get<string>(entry.Value, "kind");
                    int hp = Reflect.Get<int>(entry.Value, "baseHp");
                    if (kind == CarrierIntegration.TileId)
                    {
                        carrier = entry.Value;
                    }
                    else if (kind == "explosive" || kind == "lightning" || kind == "void" || kind == "swarm")
                    {
                        highest = Mathf.Max(highest, hp);
                    }
                }

                if (carrier != null)
                {
                    int target = Plugin.CarrierHp.Value > 0 ? Plugin.CarrierHp.Value : Mathf.Max(1, highest);
                    Reflect.Set(carrier, "baseHp", target);
                    Plugin.Log?.LogInfo($"[ambusher] carrier HP set to {target} (vanilla max {highest}).");
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"[ambusher] config check failed: {e.Message}");
            }
        }
    }

    /// <summary>Ambushing carrier: replaces the vanilla DoAttack for our kind.</summary>
    [HarmonyPatch(typeof(EnemyManager), "DoAttack")]
    internal static class EnemyManagerDoAttackPatch
    {
        private static bool Prefix(object[] __args)
        {
            if (__args == null || __args.Length == 0)
            {
                return true;
            }

            object enemy = __args[0];
            object cfg = Reflect.Get(enemy, "cfg");
            if (cfg == null || Reflect.Get<string>(cfg, "kind") != CarrierIntegration.TileId)
            {
                return true;
            }

            AmbusherDirector director = AmbusherDirector.Instance;
            if (director == null || director.Ship == null)
            {
                return false;
            }

            if (Plugin.LogVerbose.Value)
            {
                Plugin.Log?.LogInfo($"[ambusher] carrier attack tick (live drones {director.LiveDrones}).");
            }

            if (director.LiveDrones < Plugin.CarrierMaxLive.Value)
            {
                Vector3 world = Reflect.Get<Vector3>(enemy, "world");
                director.SpawnDroneNear(world);
            }

            return false;
        }
    }

    /// <summary>Ambushing drones block the structure-cleared win until they are gone.</summary>
    [HarmonyPatch(typeof(MiningSortie), "StructureCleared")]
    internal static class StructureClearedGatePatch
    {
        private static bool Prefix(MiningSortie __instance)
        {
            AmbusherDirector director = AmbusherDirector.Instance;
            if (director != null && director.LiveDrones > 0)
            {
                director.BlockWin(__instance);
                return false;
            }

            return true;
        }
    }
}
