using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace GraveyardOrbit.Ambusher
{
    /// <summary>
    /// Option (b) contract integration: a plugin-side table of
    /// contract id -> { layer: carrierCount }.
    /// File: ambusher_contracts.json next to the DLL.
    /// </summary>
    internal static class AmbusherCounts
    {
        private static readonly Dictionary<string, Dictionary<int, int>> Table =
            new Dictionary<string, Dictionary<int, int>>(StringComparer.OrdinalIgnoreCase);

        internal static Dictionary<int, int> Wildcard { get; private set; }

        internal static void Load(string pluginDirectory)
        {
            Table.Clear();
            Wildcard = null;
            string path = Path.Combine(pluginDirectory, "ambusher_contracts.json");
            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                JObject root = JObject.Parse(File.ReadAllText(path));
                foreach (KeyValuePair<string, JToken> contract in root)
                {
                    if (!(contract.Value is JObject bands))
                    {
                        continue;
                    }

                    Dictionary<int, int> counts = new Dictionary<int, int>();
                    foreach (KeyValuePair<string, JToken> band in bands)
                    {
                        if (!int.TryParse(band.Key, out int layer))
                        {
                            continue;
                        }

                        int count = (int?)band.Value ?? 0;
                        if (count > 0)
                        {
                            counts[layer] = count;
                        }
                    }

                    if (counts.Count == 0)
                    {
                        continue;
                    }

                    if (contract.Key == "default" || contract.Key == "*")
                    {
                        Wildcard = counts;
                    }
                    else
                    {
                        Table[contract.Key] = counts;
                    }
                }

                Plugin.Log?.LogInfo($"[ambusher] loaded ambusher_contracts.json ({Table.Count} contracts).");
            }
            catch (Exception e)
            {
                Plugin.Log?.LogError($"[ambusher] failed to read {path}: {e.Message}");
            }
        }

        internal static Dictionary<int, int> For(string contractId)
        {
            if (string.IsNullOrEmpty(contractId))
            {
                return null;
            }

            return Table.TryGetValue(contractId, out Dictionary<int, int> counts) ? counts : null;
        }
    }
}
