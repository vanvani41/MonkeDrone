using BepInEx;
using HarmonyLib;

namespace MonkeDrone
{
    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        void Awake()
        {
            var harmony = new Harmony(PluginInfo.GUID);
            harmony.PatchAll();
        }
    }

    // ── Патч на OnPlayerSpawned ──────────────────────────────────────
    [HarmonyPatch(typeof(GorillaTagger), "OnPlayerSpawned")]
    public class HarmonyPatches
    {
        private static void Postfix()
        {
            Mod.Init();
        }
    }
}
