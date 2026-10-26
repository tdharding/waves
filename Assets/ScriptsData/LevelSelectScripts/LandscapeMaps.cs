using System;
using UnityEngine;

/// <summary>
/// One painted grid laid over the landscape tile area: a grey value per cell, 0 black to 1 white.
/// Cell (x, z) is <c>cells[z * cellsX + x]</c>, x running with world X and z with world Z.
///
/// Read smoothly between cell centres (Catmull-Rom), so painted cells come out as rounded ground
/// rather than steps. Empty (no cells) reads as black everywhere.
/// </summary>
[Serializable]
public class LandscapeMapGrid
{
    [Tooltip("Cells across the landscape's width (X). The depth gets as many as keep the cells square.")]
    public int density = 32;
    public int cellsX, cellsZ;
    public float[] cells = new float[0];
    [Tooltip("Where in the world the cells lie: x/y the min world X/Z, width/height its extent. " +
             "None (zero size) lies over the tile area — grids painted before it existed.")]
    public Rect area;

    public bool IsEmpty => cells == null || cellsX <= 0 || cellsZ <= 0 || cells.Length != cellsX * cellsZ;
    public bool HasArea => area.width > 0f && area.height > 0f;

    /// <summary>The grid's own area, or <paramref name="tileArea"/> when it has none.</summary>
    public Rect AreaOr(Rect tileArea) => HasArea ? area : tileArea;

    public void CopyFrom(LandscapeMapGrid o)
    {
        density = o.density;
        cellsX  = o.cellsX;
        cellsZ  = o.cellsZ;
        cells   = o.cells != null ? (float[])o.cells.Clone() : new float[0];
        area    = o.area;
    }

    /// <summary>
    /// Grows the grid by whole cells, black, until it covers <paramref name="cover"/> — what is
    /// painted stays where it is in the world. Never shrinks, and stops at <see cref="LandscapeMaps.MaxCells"/>.
    /// True if it grew.
    /// </summary>
    public bool GrowToCover(Rect cover)
    {
        if (IsEmpty || !HasArea || cover.width <= 0f || cover.height <= 0f) return false;

        const float Eps = 1e-3f;
        float cellW = area.width / cellsX, cellH = area.height / cellsZ;
        int addL = Mathf.Max(0, Mathf.CeilToInt((area.xMin - cover.xMin) / cellW - Eps));
        int addR = Mathf.Max(0, Mathf.CeilToInt((cover.xMax - area.xMax) / cellW - Eps));
        int addB = Mathf.Max(0, Mathf.CeilToInt((area.yMin - cover.yMin) / cellH - Eps));
        int addT = Mathf.Max(0, Mathf.CeilToInt((cover.yMax - area.yMax) / cellH - Eps));

        int room = LandscapeMaps.MaxCells - cellsX;
        addR = Mathf.Min(addR, room);  addL = Mathf.Min(addL, room - addR);
        room = LandscapeMaps.MaxCells - cellsZ;
        addT = Mathf.Min(addT, room);  addB = Mathf.Min(addB, room - addT);
        if (addL + addR + addB + addT == 0) return false;

        int nx = cellsX + addL + addR, nz = cellsZ + addB + addT;
        var next = new float[nx * nz];
        for (int z = 0; z < cellsZ; z++)
            Array.Copy(cells, z * cellsX, next, (z + addB) * nx + addL, cellsX);

        area = new Rect(area.xMin - addL * cellW, area.yMin - addB * cellH, nx * cellW, nz * cellH);
        cellsX  = nx;
        cellsZ  = nz;
        cells   = next;
        density = nx;
        return true;
    }

    /// <summary>
    /// Sizes the grid to <see cref="density"/> over an area <paramref name="width"/> by
    /// <paramref name="depth"/>, stretching whatever is painted to fit the new cell count. An
    /// empty grid starts black. <paramref name="nearest"/> takes each new cell from the one old
    /// cell under it rather than blending — for the Texture map, whose cells are colours, not greys.
    /// </summary>
    public void Resize(float width, float depth, bool nearest = false)
    {
        int nx = Mathf.Clamp(density, 1, LandscapeMaps.MaxCells);
        int nz = width > 0f ? Mathf.Clamp(Mathf.RoundToInt(nx * depth / width), 1, LandscapeMaps.MaxCells) : nx;
        if (!IsEmpty && nx == cellsX && nz == cellsZ) return;

        var next = new float[nx * nz];
        if (!IsEmpty)
        {
            // Stretched: each new cell reads the old grid at the same place across the area.
            for (int z = 0; z < nz; z++)
            for (int x = 0; x < nx; x++)
            {
                float u = (x + 0.5f) / nx * cellsX - 0.5f;
                float v = (z + 0.5f) / nz * cellsZ - 0.5f;
                next[z * nx + x] = nearest ? Cell(Mathf.RoundToInt(u), Mathf.RoundToInt(v)) : Bilinear(u, v);
            }
        }
        cellsX = nx;
        cellsZ = nz;
        cells  = next;
    }

    float Cell(int x, int z) => cells[Mathf.Clamp(z, 0, cellsZ - 1) * cellsX + Mathf.Clamp(x, 0, cellsX - 1)];

    float Bilinear(float u, float v)
    {
        int   ix = Mathf.FloorToInt(u), iz = Mathf.FloorToInt(v);
        float fx = u - ix,              fz = v - iz;
        return Mathf.Lerp(Mathf.Lerp(Cell(ix, iz),     Cell(ix + 1, iz),     fx),
                          Mathf.Lerp(Cell(ix, iz + 1), Cell(ix + 1, iz + 1), fx), fz);
    }

    /// <summary>The grey at a world point, 0–1, read smoothly between cell centres.</summary>
    public float Sample(Rect area, Vector2 xz)
    {
        if (IsEmpty || area.width <= 0f || area.height <= 0f) return 0f;

        float u  = (xz.x - area.xMin) / area.width  * cellsX - 0.5f;
        float v  = (xz.y - area.yMin) / area.height * cellsZ - 0.5f;
        int   ix = Mathf.FloorToInt(u), iz = Mathf.FloorToInt(v);
        float fx = u - ix,              fz = v - iz;

        float r0 = CatmullRom(Cell(ix - 1, iz - 1), Cell(ix, iz - 1), Cell(ix + 1, iz - 1), Cell(ix + 2, iz - 1), fx);
        float r1 = CatmullRom(Cell(ix - 1, iz),     Cell(ix, iz),     Cell(ix + 1, iz),     Cell(ix + 2, iz),     fx);
        float r2 = CatmullRom(Cell(ix - 1, iz + 1), Cell(ix, iz + 1), Cell(ix + 1, iz + 1), Cell(ix + 2, iz + 1), fx);
        float r3 = CatmullRom(Cell(ix - 1, iz + 2), Cell(ix, iz + 2), Cell(ix + 1, iz + 2), Cell(ix + 2, iz + 2), fx);
        return Mathf.Clamp01(CatmullRom(r0, r1, r2, r3, fz));
    }

    static float CatmullRom(float p0, float p1, float p2, float p3, float t)
        => 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t
                   + (3f * p1 - p0 - 3f * p2 + p3) * t * t * t);

    /// <summary>The cell a world point falls in, or false outside the area.</summary>
    public bool CellAt(Rect area, Vector2 xz, out int x, out int z)
    {
        x = z = -1;
        if (IsEmpty || area.width <= 0f || area.height <= 0f) return false;
        x = Mathf.FloorToInt((xz.x - area.xMin) / area.width  * cellsX);
        z = Mathf.FloorToInt((xz.y - area.yMin) / area.height * cellsZ);
        return x >= 0 && x < cellsX && z >= 0 && z < cellsZ;
    }

    /// <summary>
    /// Feeds every cell that can change the ground between the given world bounds to
    /// <paramref name="add"/> — the smooth read reaches two cells round each point.
    /// </summary>
    public void Fingerprint(Rect area, float minX, float maxX, float minZ, float maxZ, Action<float> add)
    {
        add(cellsX); add(cellsZ);
        if (IsEmpty || area.width <= 0f || area.height <= 0f) return;

        int x0 = Mathf.Clamp(Mathf.FloorToInt((minX - area.xMin) / area.width  * cellsX) - 2, 0, cellsX - 1);
        int x1 = Mathf.Clamp(Mathf.FloorToInt((maxX - area.xMin) / area.width  * cellsX) + 2, 0, cellsX - 1);
        int z0 = Mathf.Clamp(Mathf.FloorToInt((minZ - area.yMin) / area.height * cellsZ) - 2, 0, cellsZ - 1);
        int z1 = Mathf.Clamp(Mathf.FloorToInt((maxZ - area.yMin) / area.height * cellsZ) + 2, 0, cellsZ - 1);
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++)
            add(cells[z * cellsX + x]);
    }
}

/// <summary>
/// The landscape's three painted maps, held by a <see cref="LandscapeMapsPreset"/> and painted in
/// the Level Select Designer's Landscape Maps tool:
///   Height   black = Low, white = High: the base the hill points (if on) add to
///   Noise    white = full rocky noise, black = smooth; Noise Height is how far full noise pushes
///   Texture  Red, Blue or Yellow (or unpainted): which designation's parts the Level Select
///            Landscape Tuner shades the ground with. Read by the shader, never baked.
/// Each grid lies over its own world area (<see cref="LandscapeMapGrid.area"/>), so changing the
/// tiles leaves what is painted where it is; the reads take the tile area for a grid without one.
/// </summary>
[Serializable]
public class LandscapeMaps
{
    public const int MaxCells = 512;

    public LandscapeMapGrid height = new LandscapeMapGrid();
    [Tooltip("Metres above the tile base where the height map is black.")]
    public float low = 0f;
    [Tooltip("Metres above the tile base where the height map is white.")]
    public float high = 20f;

    public LandscapeMapGrid noise = new LandscapeMapGrid();
    [Tooltip("How far full (white) rocky noise pushes the ground, in metres.")]
    public float noiseHeight = 2f;

    // Each cell one of the Texture* numbers below, not a grey.
    public LandscapeMapGrid texture = new LandscapeMapGrid();

    // What a Texture map cell holds. Unpainted ground is the Landscape designation.
    public const int TextureNone = 0, TextureRed = 1, TextureBlue = 2, TextureYellow = 3;

    public bool IsEmpty => height.IsEmpty && noise.IsEmpty && texture.IsEmpty;

    public void CopyFrom(LandscapeMaps o)
    {
        if (o == null) return;
        height.CopyFrom(o.height);
        low  = o.low;
        high = o.high;
        noise.CopyFrom(o.noise);
        noiseHeight = o.noiseHeight;
        if (o.texture != null) texture.CopyFrom(o.texture);
        else                   texture = new LandscapeMapGrid();
    }

    /// <summary>Metres above the tile base the height map puts the ground; 0 with no height map.</summary>
    public float HeightAt(Rect area, Vector2 xz)
        => height.IsEmpty ? 0f : Mathf.Lerp(low, high, height.Sample(height.AreaOr(area), xz));

    /// <summary>How much rocky noise the noise map asks for here, 0–1.</summary>
    public float NoiseAt(Rect area, Vector2 xz) => noise.Sample(noise.AreaOr(area), xz);

    public void Fingerprint(Rect area, float minX, float maxX, float minZ, float maxZ, Action<float> add)
    {
        add(low); add(high); add(noiseHeight);
        Grid(height);
        add(-7f);
        Grid(noise);

        void Grid(LandscapeMapGrid g)
        {
            var a = g.AreaOr(area);
            add(a.x); add(a.y); add(a.width); add(a.height);
            g.Fingerprint(a, minX, maxX, minZ, maxZ, add);
        }
    }
}
