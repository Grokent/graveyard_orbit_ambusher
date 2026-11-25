using BepInEx;
using System.IO;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace GraveyardOrbit.Ambusher
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "graveyardorbit.ambusher";
        public const string PluginName = "Graveyard Orbit Ambusher (prototype)";
        public const string PluginVersion = "0.1.1";

        internal static Plugin Instance { get; private set; }
        internal static BepInEx.Logging.ManualLogSource Log { get; private set; }

        internal static ConfigEntry<bool> DebugHotkeyEnabled;
        internal static ConfigEntry<KeyboardShortcut> DebugHotkey;
        internal static ConfigEntry<bool> DebugCarrierOnSortie;
        internal static ConfigEntry<int> DebugDronesOnSortie;
        internal static ConfigEntry<float> DebugCarrierDistanceHex;
        internal static ConfigEntry<int> CarrierMaxLive;
        internal static ConfigEntry<int> LobDamage;
        internal static ConfigEntry<float> LobSpeedPx;
        internal static ConfigEntry<float> HuntTimeoutSeconds;
        internal static ConfigEntry<float> HuntSpeedPx;
        internal static ConfigEntry<bool> CloakPlaceholder;
        internal static ConfigEntry<string> CloakEffect;
        internal static ConfigEntry<float> CloakOpacity;
        internal static ConfigEntry<float> RippleKick;
        internal static ConfigEntry<float> RippleInterval;
        internal static ConfigEntry<float> RippleWakeOffset;
        internal static ConfigEntry<float> RippleMinSpeedPx;
        internal static ConfigEntry<float> TrailAlpha;
        internal static ConfigEntry<float> TrailTime;
        internal static ConfigEntry<float> TrailWidthHex;
        internal static ConfigEntry<float> WindupSeconds;
        internal static ConfigEntry<int> CarrierHp;
        internal static Shader CloakGlassShader;
        internal static ConfigEntry<bool> CarriersVisibleAtStart;
        internal static ConfigEntry<bool> AutoSeedUnlisted;
        internal static ConfigEntry<int> CellsPerCarrier;
        internal static ConfigEntry<int> MinCarriers;
        internal static ConfigEntry<int> MaxCarriers;
        private float _nextShaderCheck;
        internal static ConfigEntry<bool> LogVerbose;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            DebugHotkeyEnabled = Config.Bind("Debug", "HotkeyEnabled", false,
                "Enable the debug spawn hotkey (off by default).");
            DebugHotkey = Config.Bind("Debug", "Hotkey", new KeyboardShortcut(KeyCode.F10),
                "Spawn a real ambusher carrier ahead of the ship.");
            DebugCarrierOnSortie = Config.Bind("Debug", "CarrierOnSortie", false,
                "Spawn one debug carrier automatically when a mining sortie starts.");
            DebugDronesOnSortie = Config.Bind("Debug", "DronesOnSortie", 0,
                "Spawn this many debug drones directly when a mining sortie starts.");
            DebugCarrierDistanceHex = Config.Bind("Debug", "CarrierDistanceHex", 24f,
                "How far in front of the ship the F10 debug carrier spawns, in hexes.");

            CarrierMaxLive = Config.Bind("Balance", "CarrierMaxLive", 7,
                "Maximum live ambusher drones per carrier (vanilla swarm uses 7).");
            LobDamage = Config.Bind("Balance", "LobDamage", 1,
                "Damage dealt by one ambusher lob.");
            LobSpeedPx = Config.Bind("Balance", "LobSpeedPx", 140f,
                "Lob speed in px (hexSize / 32). Vanilla enemy projectiles use 140.");
            HuntTimeoutSeconds = Config.Bind("Balance", "HuntTimeoutSeconds", 10f,
                "How long a drone keeps trying to reach its firing position before firing anyway.");
            HuntSpeedPx = Config.Bind("Balance", "HuntSpeedPx", 240f,
                "Repositioning speed in px. The player's ship tops out at 200 px, so 200 could never close a straight chase.");

            CloakPlaceholder = Config.Bind("Visuals", "CloakPlaceholder", true,
                "Fallback shimmer bubble when no distortion shader is available.");
            CloakEffect = Config.Bind("Visuals", "CloakEffect", "trail",
                "trail = faint movement wake; ripple = cool but full-screen SpaceTimeRipple; mirage/glass/shimmer/off.");
            CloakOpacity = Config.Bind("Visuals", "CloakOpacity", 0.02f,
                "Mirage tint alpha (0 = pure refraction, higher = more visible).");
            RippleKick = Config.Bind("Visuals", "RippleKick", 0.45f,
                "Strength of the screen-space heat ripple.");
            RippleInterval = Config.Bind("Visuals", "RippleInterval", 0.10f,
                "Base seconds between heat ripples while moving.");
            RippleWakeOffset = Config.Bind("Visuals", "RippleWakeOffset", 1.5f,
                "How far behind the drone the wake ripple is centred, in hexes.");
            RippleMinSpeedPx = Config.Bind("Visuals", "RippleMinSpeedPx", 10f,
                "Below this speed the drone emits no wake, so a holding drone stays hidden.");
            TrailAlpha = Config.Bind("Visuals", "TrailAlpha", 0.14f,
                "Opacity of the localized cloak wake trail.");
            TrailTime = Config.Bind("Visuals", "TrailTime", 0.35f,
                "Seconds the cloak wake trail persists.");
            TrailWidthHex = Config.Bind("Visuals", "TrailWidthHex", 0.18f,
                "Width of the cloak wake trail in hexes.");
            WindupSeconds = Config.Bind("Balance", "WindupSeconds", 0.85f,
                "How long the drone stays visible and telegraphs before lunging.");
            CarrierHp = Config.Bind("Balance", "CarrierHp", 0,
                "Carrier HP. 0 = inherit the highest vanilla behavior-turret HP.");

            CarriersVisibleAtStart = Config.Bind("Balance", "CarriersVisibleAtStart", true,
                "Reveal ambusher carriers immediately when the sortie starts (easier to test and to kill).");
            AutoSeedUnlisted = Config.Bind("Balance", "AutoSeedUnlisted", true,
                "Automatically seed carriers into any contract not listed in ambusher_contracts.json.");
            CellsPerCarrier = Config.Bind("Balance", "CellsPerCarrier", 10000,
                "Auto-seeding density: one carrier per this many map cells.");
            MinCarriers = Config.Bind("Balance", "MinCarriers", 1,
                "Minimum auto-seeded carriers per map.");
            MaxCarriers = Config.Bind("Balance", "MaxCarriers", 12,
                "Maximum auto-seeded carriers per map.");
            LogVerbose = Config.Bind("Debug", "VerboseLog", true,
                "Log state transitions to the BepInEx console.");

            AmbusherCounts.Load(Path.GetDirectoryName(Info.Location) ?? Paths.PluginPath);
            CarrierIntegration.RegisterAltId();

            Harmony harmony = new Harmony(PluginGuid);
            harmony.PatchAll(typeof(Plugin).Assembly);

            Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");
        }

        private void Update()
        {
            if (CloakGlassShader == null && Time.unscaledTime >= _nextShaderCheck)
            {
                _nextShaderCheck = Time.unscaledTime + 2f;
                CloakGlassShader = Shader.Find("Mirza/PRISM/Glass");
                if (CloakGlassShader != null)
                {
                    Logger.LogInfo("[ambusher] cached PRISM glass shader for cloak distortion.");
                }
            }

            if (!DebugHotkeyEnabled.Value || !DebugHotkey.Value.IsDown())
            {
                return;
            }

            AmbusherDirector director = AmbusherDirector.Instance;
            if (director == null || director.Ship == null)
            {
                Logger.LogWarning("No active mining sortie to spawn an ambusher in.");
                return;
            }

            director.SpawnRealCarrierNearShip();
        }
    }

    [HarmonyPatch(typeof(MiningSortie), "Start")]
    internal static class MiningSortieStartPatch
    {
        private static void Postfix(MiningSortie __instance)
        {
            AmbusherDirector.Ensure(__instance);

            if (Plugin.DebugCarrierOnSortie.Value)
            {
                AmbusherDirector.Instance?.SpawnRealCarrierNearShip();
            }

            int count = Mathf.Max(0, Plugin.DebugDronesOnSortie.Value);
            for (int i = 0; i < count; i++)
            {
                AmbusherDirector.Instance?.SpawnDebugDrone(i);
            }
        }
    }

    /// <summary>Notifies the drone component that its placeholder SwarmUnit took a hit.</summary>
    [HarmonyPatch(typeof(SwarmUnit), nameof(SwarmUnit.TakeDamage))]
    internal static class SwarmUnitTakeDamagePatch
    {
        private static void Postfix(SwarmUnit __instance)
        {
            AmbusherDrone drone = __instance.GetComponent<AmbusherDrone>();
            if (drone != null)
            {
                drone.OnDamaged();
            }
        }
    }
}
