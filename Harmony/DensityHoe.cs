using HarmonyLib;
using System.Reflection;
using UnityEngine;

public class DensityHoe : IModApi
{

    public void InitMod(Mod mod)
    {
        Log.Out(" Loading Patch: " + GetType().ToString());
        Harmony harmony = new Harmony(GetType().ToString());
        harmony.PatchAll(Assembly.GetExecutingAssembly());
    }

    // Allow XML config to set solid cube shapes
    // To enable density hoe feature for blocks
    // Note: has too many side-effects if on!
    // [HarmonyPatch(typeof(BlockShapeNew))]
    // [HarmonyPatch("Init")]
    // public class BlockShapeNew_Init
    // {
    //     public static void Postfix(
    //         BlockShapeNew __instance,
    //         Block _block)
    //     {
    //         _block.Properties.ParseBool("IsSolidCube",
    //             ref __instance.IsSolidCube);
    //     }
    // }

    // Patch displaced cube rendering to give user feedback
    // Creates a proper wire-frame around the focused terrain
    // Base code didn't work properly since the terrain blocks
    // are missing a proper prefab to display, as that code is
    // mainly used to display outlines around the block to place.
    [HarmonyPatch(typeof(RenderDisplacedCube))]
    [HarmonyPatch("update0")]
    public class RenderDisplacedCube_Update0
    {

        private static readonly MethodInfo MethodDestroyPreview =
            AccessTools.Method(typeof(RenderDisplacedCube), "DestroyPreview");

        public static bool Prefix(
            ref World _world,
            ref EntityAlive _player,
            ref WorldRayHitInfo _hitInfo,
            RenderDisplacedCube __instance,
            ref Bounds ___myBounds,
            ref Vector3 ___multiDim,
            ref float ___lastTimeFocusTransformMoved,
            ref Transform ___transformFocusCubePrefab,
            ref Transform ___transformWireframeCube,
            ref Material ___previewMaterial)
        {

            // Do some cleanups we seen on original code
            // Not exactly sure what it does, but seems to
            // be safer to keep these than to skip them
            MethodDestroyPreview.Invoke(__instance, null);
            Object.DestroyImmediate(___previewMaterial);

            int clrIdx = _hitInfo.hit.clrIdx;
            Vector3i blockPos = _hitInfo.hit.blockPos;
            BlockValue BV = _hitInfo.hit.blockValue;

            // Assertion used when debugging (never happened so far)
            // if (BV.rawData != _world.GetBlock(blockPos).rawData)
            // 	Log.Warning("Raw Data of Hit Block differs");

            // Check if distance to hit is within our reached range
            float blockRangeSq = GetHoeActionRangeSq(_player?.inventory?
                    .holdingItemItemValue?.ItemClass?.Actions);
            if (blockRangeSq < _hitInfo.hit.distanceSq) return true;

            // Update to avoid other code from thinking it's stale
            ___lastTimeFocusTransformMoved = Time.time;

            // Get density to determine wireframe height
            // May also be used to decide if hit is valid
            var density = _world.GetDensity(clrIdx, blockPos);

            // Play safe to check for existence first
            if (___transformWireframeCube != null)
            {
                // Try to scale the wireframe block to be sure it is visible
                // Sometimes hard to get right, might need to take the adjacent
                // terrain blocks also into account for real good results!?
                float scale = Mathf.Min(1.75f, Mathf.Max(1.1f, 1.25f * density / -50));
                ___transformWireframeCube.position = blockPos - Origin.position
                    - new Vector3(0.05f, 0.25f, 0.05f); // Adjust for paddings
                ___transformWireframeCube.localScale = new Vector3(1.1f, scale, 1.1f);
                ___transformWireframeCube.rotation = BV.Block.shape.GetRotation(BV);
            }

            // Play safe to check for existence first
            if (___transformFocusCubePrefab != null)
            {
                ___transformFocusCubePrefab.localPosition = new Vector3(0.5f, 0.5f, 0.5f);
                ___transformFocusCubePrefab.localScale = new Vector3(2.54f, 2.54f, 2.54f);
                ___transformFocusCubePrefab.parent = ___transformWireframeCube;
            }

            // Update some states, which are static for our use case
            // We only target terrain blocks, so block size is fixed
            ___myBounds = new Bounds(new Vector3(0.5f, 0.5f, 0.5f), Vector3.one);
            ___multiDim = Vector3i.one; // Terrain blocks are never multi-dims?

            // Note: disabled as too cheasy and doesn't always work (when not dense enough)
            // We can work on focused terrain blocks or also if a block has already density.
            // Second condition allows to spread the terrain over an area of opaque blocks.
            // E.g. useful to make ground out of cement blocks, that still looks like grass.
            // Not sure if this is considered cheating; IMO it just adds aesthetics if wanted
            // if ((GameUtils.IsBlockOrTerrain(_hitInfo.tag) && BV.Block.shape.IsTerrain()) || 
            //     density <= MarchingCubes.DensityTerrainHi) // to spread

            if (GameUtils.IsBlockOrTerrain(_hitInfo.tag) && BV.Block.shape.IsTerrain() && density < 0)
            {
                // Enable the two transforms (GameObjects) to show
                ___transformWireframeCube?.gameObject.SetActive(true);
                ___transformFocusCubePrefab?.gameObject.SetActive(true);
                // Update the color for the wire-frame
                // For now we always have the same color
                // Might see some use-case in the future
                foreach (Renderer child in ___transformFocusCubePrefab?
                            .GetComponentsInChildren<Renderer>())
                    child.material.SetColor("_Color", Color.green);
            }
            else
            {
                // Disable the two transforms to hide the helpers
                ___transformWireframeCube?.gameObject.SetActive(false);
                ___transformFocusCubePrefab?.gameObject.SetActive(false);
            }

            // Skip original
            return false;
        }

        private static float GetHoeActionRangeSq(ItemAction[] actions)
        {
            float range = 0;
            foreach (ItemAction action in actions)
                if (action is ItemActionDensityHoe item)
                    range = Mathf.Max(range, item.GetBlockRange());
            return range * range;
        }

    }

}
