using UnityEngine;

/// <summary>
/// Single authority for converting positions to map surface positions.
/// Two methods:
///   GridToMap  — for static grid objects (walls, finish, soul fish)
///   WorldToMap — for moving world objects (snake, boat)
/// Both output world space positions via bilinear interpolation
/// between the four MapCornerReference transforms.
/// </summary>
public static class MapProjection
{
    public static MapCornerReference Corners     { get; private set; }
    public static Vector3            ArenaOrigin { get; private set; }
    public static float              ArenaWidth  { get; private set; }
    public static float              ArenaHeight { get; private set; }
    public static bool               IsReady     { get; private set; }

    public static void Initialise(
        MapCornerReference corners,
        Bounds             arenaBounds)
    {
        Corners      = corners;
        ArenaOrigin  = arenaBounds.min;
        ArenaWidth   = arenaBounds.size.x;
        ArenaHeight  = arenaBounds.size.z;
        IsReady      = true;
    }

    public static void Reset()
    {
        Corners = null;
        IsReady = false;
    }

    /// <summary>
    /// For static grid objects — walls, finish, soul fish markers.
    /// Takes a grid cell X and Z (0..GridSize-1) and returns a world
    /// position on the map surface.
    /// </summary>
    public static Vector3 GridToMap(int cellX, int cellZ)
    {
        float nx = cellX / (float)(GridData.GridSize - 1);
        float nz = cellZ / (float)(GridData.GridSize - 1);
        return Corners.GetWorldPosition(nx, nz);
    }

    /// <summary>
    /// For moving world-space objects — snake, boat pointer.
    /// Takes a world position and returns a proportional position
    /// on the map surface relative to arena bounds.
    /// </summary>
    public static Vector3 WorldToMap(Vector3 worldPos)
    {
        float nx = Mathf.Clamp01((worldPos.x - ArenaOrigin.x) / ArenaWidth);
        float nz = Mathf.Clamp01((worldPos.z - ArenaOrigin.z) / ArenaHeight);
        return Corners.GetWorldPosition(nx, nz);
    }
}