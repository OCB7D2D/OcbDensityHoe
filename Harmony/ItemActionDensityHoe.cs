using System.Collections.Generic;

public class ItemActionDensityHoe : ItemActionDynamicMelee
{
    public enum DensityAction : byte
    {
        LevelAray,
        FillDensity
    }

    private string SoundHoe = string.Empty;

    private DensityAction ActionType = DensityAction.LevelAray;

    public float GetBlockRange() => BlockRange;

    public override void ReadFrom(DynamicProperties _props)
    {
        _props.ParseString("SoundHoe", ref SoundHoe);
        string type = DensityAction.LevelAray.ToString();
        _props.ParseString("ActionType", ref type);
        ActionType = EnumUtils.Parse(type,
            DensityAction.LevelAray, true);
        base.ReadFrom(_props);
    }

    protected override void hitTarget(
        ItemActionData action,
        WorldRayHitInfo hitInfo,
        bool isGrazingHit)
    {
        var invData = action.invData;
        if (IsHitValid(invData, out sbyte density))
            if (ExecuteDensityHoe(invData, density)) return;
        base.hitTarget(action, hitInfo, isGrazingHit);
    }

    public override RenderCubeType GetFocusType(ItemActionData action) =>
        IsHitValid(action) ? RenderCubeType.FaceTop : base.GetFocusType(action);

    public override ItemClass.EnumCrosshairType GetCrosshairType(ItemActionData action) =>
        IsHitValid(action) ? ItemClass.EnumCrosshairType.Plus : base.GetCrosshairType(action);

    protected override bool isShowOverlay(ItemActionData action) =>
        IsHitValid(action) || base.isShowOverlay(action);

    protected override void getOverlayData(
        ItemActionData action,
        out float _perc,
        out string _text)
    {
        if (IsHitValid(action.invData, out sbyte density))
        {
            _text = Localization.Get(IsPlayerCrouching(action.invData)
                ? "ttDensityHoeActionCrouched" : "ttDensityHoeAction");
            if (density < 0) _perc = density / -128f; // sbyte.MinValue
            else if (density > 0) _perc = density / 127f; // sbyte.MaxValue
            else _perc = 0f;
            return;
        }
        base.getOverlayData(action, out _perc, out _text);
    }

    private static bool IsPlayerCrouching(ItemInventoryData invData)
    {
        var player = invData.holdingEntity as EntityPlayerLocal;
        return (bool)player?.vp_FPController?.Player?.Crouch.Active;
    }

    // Execute the level averaging/filling action
    // Return `true` if action was executed
    private bool ExecuteDensityHoe(
        ItemInventoryData invData,
        sbyte density)
    {

        var hitInfo = invData.hitInfo;

        // Accumulate all block changes
        List<BlockChangeInfo> changes =
            new List<BlockChangeInfo>();

        // Check if modifier is pressed?
        // ToDo: make this configurable?
        // Input.GetKey(KeyCode.LeftShift);

        int clrIdx = hitInfo.hit.clrIdx;
        Vector3i pos = hitInfo.hit.blockPos;
        BlockValue BV = hitInfo.hit.blockValue;

        // Prepare for averaging also target block
        if (ActionType == DensityAction.LevelAray)
        {
            density = MarchingCubes.DensityTerrain;
            changes.Add(new BlockChangeInfo(clrIdx, pos, BV,
                invData.world.GetDensity(clrIdx, pos)));
        }

        // Add direct neighbours for potential leveling
        GatherNeighbours(invData.world, clrIdx,
            pos + Vector3i.forward, ref changes, density,
            ActionType == DensityAction.FillDensity);
        GatherNeighbours(invData.world, clrIdx,
            pos + Vector3i.right, ref changes, density,
            ActionType == DensityAction.FillDensity);
        GatherNeighbours(invData.world, clrIdx,
            pos + Vector3i.back, ref changes, density,
            ActionType == DensityAction.FillDensity);
        GatherNeighbours(invData.world, clrIdx,
            pos + Vector3i.left, ref changes, density,
            ActionType == DensityAction.FillDensity);

        // Also add diagonal blocks for leveling area if crouching
        var player = invData.holdingEntity as EntityPlayerLocal;
        if ((bool)player?.vp_FPController?.Player?.Crouch.Active)
        {
            GatherNeighbours(invData.world, clrIdx,
                pos + Vector3i.forward + Vector3i.right, ref changes,
                density, ActionType == DensityAction.FillDensity);
            GatherNeighbours(invData.world, clrIdx,
                pos + Vector3i.back + Vector3i.right, ref changes,
                density, ActionType == DensityAction.FillDensity);
            GatherNeighbours(invData.world, clrIdx,
                pos + Vector3i.back + Vector3i.left, ref changes,
                density, ActionType == DensityAction.FillDensity);
            GatherNeighbours(invData.world, clrIdx,
                pos + Vector3i.forward + Vector3i.left, ref changes,
                density, ActionType == DensityAction.FillDensity);
        }

        // Now average all densities for level action
        // ToDo: implement better 2x2 sampling average?
        if (ActionType == DensityAction.LevelAray)
        {
            int sum = 0;
            foreach (var change in changes)
                sum += change.density;
            float avg = sum / changes.Count;
            foreach (var change in changes)
            {
                change.density = (sbyte)(0.5f *
                    (change.density + avg));
                sum -= change.density;
            }
            changes[0].density += (sbyte)sum;
            sum = 0;
            foreach (var change in changes)
                sum += change.density;
        }

        if (changes.Count > 0) invData.
            world.SetBlocksRPC(changes);

        if (changes.Count > 0 && SoundHoe != null)
            invData.holdingEntity.PlayOneShot(SoundHoe);

        return true;
    }

    private void GatherNeighbours(World world,
        int clrIdx, Vector3i position,
        ref List<BlockChangeInfo> changes,
        sbyte density, bool skipTerrain)
    {
        BlockValue BV = world.GetBlock(position);
        if (BV.isair || BV.isWater) return;
        if (skipTerrain && BV.Block.shape.IsTerrain()) return;
        if (!IsShapeSolidCube(BV.Block.shape)) return;
        density = (sbyte)((density + MarchingCubes.DensityTerrainHi) / 2);
        if (skipTerrain && world.GetDensity(clrIdx, position) < density) return;
        changes.Add(new BlockChangeInfo(position, BV, density));
    }

    private static bool IsShapeSolidCube(BlockShape shape)
    {
        if (shape.IsSolidCube) return true;
        string name = shape.GetName();
        return name.EqualsCaseInsensitive("cube") ||
            name.EqualsCaseInsensitive("cube_glass") ||
            name.EqualsCaseInsensitive("cube_frame");
    }

    private bool IsHitValid(ItemInventoryData invData, out sbyte density)
    {
        density = MarchingCubes.DensityAir;
        WorldRayHitInfo hitInfo = invData.hitInfo;
        // Check for overall hit validity
        if (!hitInfo.bHitValid) return false;
        // Only works for terrain and blocks (with density)
        if (!GameUtils.IsBlockOrTerrain(hitInfo.tag)) return false;
        // Get additional info from structs
        int clrIdx = hitInfo.hit.clrIdx;
        Vector3i pos = hitInfo.hit.blockPos;
        BlockValue BV = hitInfo.hit.blockValue;
        // Allow action for blocks with density and for all terrain
        density = invData.world.GetDensity(clrIdx, pos);
        if (density > MarchingCubes.DensityTerrainHi &&
            !BV.Block.shape.IsTerrain()) return false;
        // Check distance for this hit to be within our range
        if (hitInfo.hit.distanceSq > BlockRange * BlockRange) return false;
        // Hit is valid
        return true;
    }

    private bool IsHitValid(ItemActionData action)
        => IsHitValid(action.invData, out sbyte _);

}
