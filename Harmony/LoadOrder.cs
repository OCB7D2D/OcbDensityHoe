using HarmonyLib;
using System.Collections.Generic;

namespace LoadOrder
{
    // Return in our load order
    [HarmonyPatch(typeof(ModManager))]
    [HarmonyPatch("GetLoadedMods")]
    public class ModManager_GetLoadedMods
    {
        static void Postfix(ref List<Mod> __result)
        {
            int myPos = -1, depPos = -1;
            if (__result == null) return;
            // Find position of mods we depend on
            for (int i = 0; i < __result.Count; i += 1)
            {
                switch (__result[i].Name)
                {
                    case "Afterlife":
                        if (depPos < i + 1)
                            depPos = i + 1;
                        break;
                    case "OcbDensityHoe":
                        myPos = i;
                        break;
                }
            }
            // Didn't detect ourself?
            if (myPos == -1)
            {
                Log.Error("Did not detect our own Mod?");
                return;
            }
            // Detected no dependencies?
            if (depPos == -1) return;
            if (depPos < myPos) return;
            // Move our mod after deps
            var item = __result[myPos];
            __result.RemoveAt(myPos);
            __result.Insert(depPos - 1, item);
        }

    }

}
