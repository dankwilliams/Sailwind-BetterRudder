using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;

namespace BetterRudder
{
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    public class BetterRudderPlugin : BaseUnityPlugin
    {
        public const string PLUGIN_GUID = "com.dank.sailwind.BetterRudder";
        public const string PLUGIN_NAME = "BetterRudder";
        public const string PLUGIN_VERSION = "1.0.40";

        public static BetterRudderPlugin Instance { get; private set; }

        public static ConfigEntry<float> GlobalRudderMultiplier;
        public static ConfigEntry<float> GlobalDragPowerMultiplier;
        public static ConfigEntry<bool> EnableDebugLogging;

        public static JObject boatConfigJson = null;
        private static readonly Dictionary<object, float> lastLogTimes = new Dictionary<object, float>();

        private void Awake()
        {
            try
            {
                Instance = this;
                Debug.Log("[BetterRudder] === INITIALIZING PLUGIN v1.0.40 ===");

                GlobalRudderMultiplier = Config.Bind(
                    "Global Defaults", "RudderMultiplier", 2.0f,
                    "Default global multiplier for rudder power."
                );

                GlobalDragPowerMultiplier = Config.Bind(
                    "Global Defaults", "DragPowerMultiplier", 0.5f,
                    "Default global multiplier for drag power."
                );

                EnableDebugLogging = Config.Bind(
                    "General", "EnableDebugLogging", true,
                    "Logs active ship name and applied multipliers once every 10 seconds."
                );

                LoadAndVerifyBoatConfigFile();

                Harmony harmony = new Harmony(PLUGIN_GUID);
                harmony.PatchAll();

                Debug.Log("[BetterRudder] === HARMONY PATCHES LOADED SUCCESSFULLY ===");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[BetterRudder FATAL ERROR in Awake]: {ex}");
            }
        }

        private void LoadAndVerifyBoatConfigFile()
        {
            string pluginFolder = Path.GetDirectoryName(Info.Location);
            string configPath = Path.Combine(pluginFolder, "com.dank.betterrudder.shipdata.json");

            if (!File.Exists(configPath))
            {
                string exampleJson = @"{
  ""20"": {
    ""RudderPowerMultiplier"": 2.0,
    ""SidewaysDragMultiplier"": 0.5
  },
  ""187"": {
    ""RudderPowerMultiplier"": 3.0,
    ""SidewaysDragMultiplier"": 0.25
  },
  ""192"": {
    ""RudderPowerMultiplier"": 2.0,
    ""SidewaysDragMultiplier"": 0.6
  }}";
                try
                {
                    File.WriteAllText(configPath, exampleJson);
                }
                catch { }
            }

            if (File.Exists(configPath))
            {
                try
                {
                    string jsonContent = File.ReadAllText(configPath);
                    boatConfigJson = JObject.Parse(jsonContent);
                    Debug.Log($"[BetterRudder] Loaded JSON containing {boatConfigJson.Count} entries.");
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[BetterRudder JSON ERROR]: {ex.Message}");
                }
            }
        }

        public static void ResolveMultipliers(string boatName, out float rudderMult, out float dragMult, out string source)
        {
            rudderMult = GlobalRudderMultiplier.Value;
            dragMult = GlobalDragPowerMultiplier.Value;
            source = "Global Defaults";

            if (boatConfigJson != null && !string.IsNullOrEmpty(boatName))
            {
                string searchName = boatName.ToLower().Trim();

                // 1. Check strict numeric index match first
                Match match = Regex.Match(boatName, @"\((\d+)\)|\b(\d+)\b");
                if (match.Success)
                {
                    string extractedIndex = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                    foreach (var property in boatConfigJson.Properties())
                    {
                        string propKey = property.Name.Trim();
                        if (propKey == extractedIndex)
                        {
                            source = $"JSON Index Match -> [{property.Name}]";
                            ApplyValues(property.Value, ref rudderMult, ref dragMult);
                            return;
                        }
                    }
                }

                // 2. Check text-based name matching (Skipping purely numeric keys)
                foreach (var property in boatConfigJson.Properties())
                {
                    string key = property.Name.Trim().ToLower();
                    if (int.TryParse(key, out _)) continue;

                    if (!string.IsNullOrEmpty(key) && searchName.Contains(key))
                    {
                        source = $"JSON Name Match -> [{property.Name}]";
                        ApplyValues(property.Value, ref rudderMult, ref dragMult);
                        return;
                    }
                }
            }
        }

        private static void ApplyValues(JToken token, ref float rudderMult, ref float dragMult)
        {
            var boatData = token as JObject;
            if (boatData != null)
            {
                if (boatData.TryGetValue("RudderPowerMultiplier", out var rVal))
                    rudderMult = rVal.Value<float>();
                if (boatData.TryGetValue("SidewaysDragMultiplier", out var dVal))
                    dragMult = dVal.Value<float>();
            }
        }

        public static bool CanLog(object key)
        {
            if (EnableDebugLogging == null || !EnableDebugLogging.Value) return false;

            float currentTime = Time.time;
            if (lastLogTimes.TryGetValue(key, out float lastTime))
            {
                if (currentTime - lastTime < 10f) return false;
            }
            lastLogTimes[key] = currentTime;
            return true;
        }
    }

    [HarmonyPatch(typeof(Rudder), "Update")]
    public class RudderPhysicsPatch
    {
        private static readonly FieldInfo powerField = AccessTools.Field(typeof(Rudder), "rudderPower");
        private static readonly FieldInfo dragField = AccessTools.Field(typeof(Rudder), "dragPower");

        private static readonly Dictionary<Rudder, float> basePowers = new Dictionary<Rudder, float>();
        private static readonly Dictionary<Rudder, float> baseDrags = new Dictionary<Rudder, float>();

        public static void Prefix(Rudder __instance)
        {
            if (__instance == null) return;

            if (!IsRudderOnActiveVessel(__instance.transform))
            {
                return;
            }

            string boatName = GetRootBoatName(__instance.transform);

            if (!basePowers.ContainsKey(__instance))
            {
                float initialPower = (powerField != null && powerField.GetValue(__instance) is float p) ? p : 1000f;
                float initialDrag = (dragField != null && dragField.GetValue(__instance) is float d) ? d : 0.02f;

                basePowers[__instance] = initialPower;
                baseDrags[__instance] = initialDrag;
            }

            BetterRudderPlugin.ResolveMultipliers(boatName, out float rMult, out float dMult, out string source);

            if (BetterRudderPlugin.CanLog(__instance))
            {
                Debug.Log($"[BetterRudder Active] Boarded Vessel: \"{boatName}\" | Source: {source} | Rudder Power Mult: {rMult} | Drag Mult: {dMult}");
            }

            if (powerField != null)
                powerField.SetValue(__instance, basePowers[__instance] * rMult);

            if (dragField != null)
                dragField.SetValue(__instance, baseDrags[__instance] * dMult);
        }

        private static bool IsRudderOnActiveVessel(Transform rudderTransform)
        {
            Transform activeBoat = GameState.currentBoat;
            if (activeBoat == null)
            {
                return false;
            }

            Transform current = rudderTransform;
            while (current != null)
            {
                if (current == activeBoat)
                {
                    return true;
                }
                current = current.parent;
            }

            return false;
        }

        private static string GetRootBoatName(Transform t)
        {
            if (GameState.currentBoat != null)
            {
                Transform parent = GameState.currentBoat.parent;
                if (parent != null) return parent.name;
                return GameState.currentBoat.name;
            }

            Transform current = t;
            while (current != null)
            {
                string nameLower = current.name.ToLower();
                if (nameLower.Contains("boat") || nameLower.Contains("chronian") || nameLower.Contains("santa maria") || nameLower.Contains("dhow") || nameLower.Contains("sanpan") || nameLower.Contains("kakam") || nameLower.Contains("cog") || nameLower.Contains("junk") || nameLower.Contains("caelanor") || nameLower.Contains("gallus") || nameLower.Contains("leopard"))
                {
                    return current.name;
                }
                current = current.parent;
            }
            return "Unknown Vessel";
        }
    }
}