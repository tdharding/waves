using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Procedurally packs "snake" windows (dots, lines, corners, L/U runs) onto a grid
// and bakes the field texture WindowTiling.hlsl samples:
//   R = mask (1 = window cell), G = per-window id (shared across a whole snake).
//
// Placement is a self-avoiding random walk with a 1-cell keep-out rule, so windows
// never touch (which would merge them). This is the "organic fitting" discussed:
// maze-like winding runs with consistent gaps. Each finished window gets one random
// id, so the shader treats it as a single lit/flickering unit.
//
// Tools ▸ Waves ▸ Window Field Generator. Tune, Generate Preview, then Bake to PNG.
public class WindowFieldGenerator : EditorWindow
{
    // ── Core knobs ──────────────────────────────────────────────────────────
    [SerializeField] int   cols       = 32;
    [SerializeField] int   rows       = 48;
    [SerializeField] float density    = 0.35f;  // fraction of cells that become windows
    [SerializeField] float straight   = 0.7f;   // 0 = wiggly, 1 = straight runs
    [SerializeField] int   maxLength  = 7;       // longest a single window runs
    [SerializeField] float lengthSkew = 2.5f;    // >1 favours short windows
    [SerializeField] float dotFrac    = 0.25f;   // share of windows forced to single dots
    [SerializeField] int   maxThick   = 2;       // 1 = thin snakes only, higher = blocky windows
    [SerializeField] int   gap        = 1;       // empty cells forced between windows
    [SerializeField] bool  noDiagonal = true;    // also keep windows apart at diagonal corners
    [SerializeField] int   seed       = 12345;

    const string OutputPath = "Assets/TextureMatShader/Maze/WindowField.png";
    static readonly Vector2Int[] Dirs =
        { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };

    Texture2D preview;

    [MenuItem("Tools/Waves/Window Field Generator")]
    static void Open() => GetWindow<WindowFieldGenerator>("Window Field");

    void OnGUI()
    {
        EditorGUILayout.LabelField("Field size (cells)", EditorStyles.boldLabel);
        cols = Mathf.Max(2, EditorGUILayout.IntField("Columns", cols));
        rows = Mathf.Max(2, EditorGUILayout.IntField("Rows", rows));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Shape & density", EditorStyles.boldLabel);
        density    = EditorGUILayout.Slider("Density", density, 0.02f, 0.8f);
        straight   = EditorGUILayout.Slider("Straightness", straight, 0f, 1f);
        maxLength  = EditorGUILayout.IntSlider("Max length", maxLength, 1, 20);
        lengthSkew = EditorGUILayout.Slider("Length skew (short↔long)", lengthSkew, 0.5f, 6f);
        dotFrac    = EditorGUILayout.Slider("Dot fraction", dotFrac, 0f, 1f);
        maxThick   = EditorGUILayout.IntSlider("Max thickness (1 = thin)", maxThick, 1, 4);
        gap        = EditorGUILayout.IntSlider("Gap (cells)", gap, 1, 4);
        noDiagonal = EditorGUILayout.Toggle("No diagonal touching", noDiagonal);
        seed       = EditorGUILayout.IntField("Seed", seed);

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Generate Preview")) Generate();
            if (GUILayout.Button("Reroll Seed")) { seed = Random.Range(0, 999999); Generate(); }
        }
        if (GUILayout.Button("Bake to PNG")) Bake();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Bakes to", OutputPath);
        using (new EditorGUILayout.HorizontalScope())
        {
            bool exists = File.Exists(OutputPath);
            using (new EditorGUI.DisabledScope(!exists))
            {
                if (GUILayout.Button("Ping in Project"))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<Texture2D>(OutputPath);
                    EditorGUIUtility.PingObject(asset);
                    Selection.activeObject = asset;
                }
                if (GUILayout.Button("Reveal in Explorer"))
                    EditorUtility.RevealInFinder(OutputPath);
            }
        }

        if (preview != null)
        {
            EditorGUILayout.Space();
            float w = EditorGUIUtility.currentViewWidth - 30f;
            float h = w * rows / Mathf.Max(1, cols);
            Rect r = GUILayoutUtility.GetRect(w, h);
            EditorGUI.DrawPreviewTexture(r, preview, null, ScaleMode.ScaleToFit);
            EditorGUILayout.HelpBox("Preview: white = window cell (id shown as brightness). " +
                                    "Bake writes " + OutputPath + " (R = mask, G = id).", MessageType.None);
        }
    }

    // Builds the owner grid and turns it into the preview texture.
    void Generate()
    {
        int[] owner = Pack(out List<float> ids);

        if (preview == null || preview.width != cols || preview.height != rows)
        {
            if (preview != null) DestroyImmediate(preview);
            preview = new Texture2D(cols, rows, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
        }

        var px = new Color32[cols * rows];
        for (int i = 0; i < owner.Length; i++)
        {
            if (owner[i] < 0) { px[i] = new Color32(0, 0, 0, 0); continue; }
            byte g = (byte)Mathf.Clamp(Mathf.RoundToInt(ids[owner[i]] * 255f), 1, 255);
            px[i] = new Color32(255, g, 0, 255);
        }
        preview.SetPixels32(px);
        preview.Apply();
    }

    void Bake()
    {
        if (preview == null) Generate();

        File.WriteAllBytes(OutputPath, preview.EncodeToPNG());
        AssetDatabase.ImportAsset(OutputPath);

        var importer = AssetImporter.GetAtPath(OutputPath) as TextureImporter;
        if (importer == null)
        {
            Debug.LogError($"[WindowFieldGenerator] No TextureImporter for {OutputPath} — set it manually " +
                           "(Point, uncompressed, no mips, sRGB off, Repeat, NPOT None).");
            return;
        }
        importer.textureType        = TextureImporterType.Default;
        importer.sRGBTexture        = false;   // R/G are data, not colour
        importer.mipmapEnabled      = false;
        importer.filterMode         = FilterMode.Point;   // never blend mask/id across cells
        importer.wrapMode           = TextureWrapMode.Repeat;
        importer.npotScale          = TextureImporterNPOTScale.None;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.isReadable         = false;
        importer.SaveAndReimport();

        Debug.Log($"[WindowFieldGenerator] Baked {OutputPath} ({cols}×{rows} cells). " +
                  $"Assign it to _WindowAtlas and set _WindowAtlasGrid = ({cols}, {rows}).");
    }

    // Self-avoiding random-walk packer. Returns owner[] (-1 = empty, else window index)
    // and the per-window random ids. Guarantees windows never share a 4-edge.
    int[] Pack(out List<float> ids)
    {
        var rng   = new System.Random(seed);
        var owner = new int[cols * rows];
        for (int i = 0; i < owner.Length; i++) owner[i] = -1;
        ids = new List<float>();

        int target   = Mathf.RoundToInt(density * cols * rows);
        int filled   = 0;
        int attempts = 0;
        int maxAttempts = cols * rows * 40;

        while (filled < target && attempts < maxAttempts)
        {
            attempts++;
            int sc = rng.Next(cols);
            int sr = rng.Next(rows);
            if (!CanSeed(owner, sc, sr)) continue;

            int wi = ids.Count;
            ids.Add((float)rng.NextDouble());
            Set(owner, sc, sr, wi);
            filled++;

            bool dot = rng.NextDouble() < dotFrac;
            int  len = dot ? 1 : SampleLength(rng);

            int cx = sc, cy = sr;
            Vector2Int last = Vector2Int.zero;
            for (int step = 1; step < len; step++)
            {
                if (!PickNext(owner, wi, cx, cy, last, rng, out Vector2Int dir)) break;
                cx += dir.x; cy += dir.y;
                Set(owner, cx, cy, wi);
                filled++;
                last = dir;
                if (filled >= target) break;
            }
        }
        return owner;
    }

    int SampleLength(System.Random rng)
    {
        double t = System.Math.Pow(rng.NextDouble(), lengthSkew); // skewed toward 0 → short
        return Mathf.Clamp(1 + (int)(t * (maxLength - 1) + 0.5), 1, maxLength);
    }

    // A cell may SEED a window if it is empty and no OTHER window lies within `gap` cells.
    bool CanSeed(int[] owner, int c, int r) => CanPlace(owner, -1, c, r);

    // A cell may EXTEND window wi if placement is legal AND it would not thicken the window
    // past maxThick (so a snake may curl beside itself into a slab, but only so chunky).
    bool CanExtend(int[] owner, int wi, int c, int r)
        => CanPlace(owner, wi, c, r) && !WouldExceedThickness(owner, wi, c, r);

    // True if adding (c,r) to wi would complete a solid (maxThick+1) square of wi's cells —
    // i.e. push the window thicker than allowed. maxThick 1 forbids any 2×2, keeping windows
    // strictly one cell wide (pure lines/corners); higher values permit blocky windows.
    bool WouldExceedThickness(int[] owner, int wi, int c, int r)
    {
        int s = maxThick + 1;
        for (int oy = r - (s - 1); oy <= r; oy++)
            for (int ox = c - (s - 1); ox <= c; ox++)
            {
                bool full = true;
                for (int yy = oy; yy < oy + s && full; yy++)
                    for (int xx = ox; xx < ox + s && full; xx++)
                    {
                        if (xx == c && yy == r) continue;           // the cell we're about to add
                        if (!In(xx, yy) || Get(owner, xx, yy) != wi) full = false;
                    }
                if (full) return true;
            }
        return false;
    }

    // Shared placement test. `self` is the window being grown (-1 when seeding).
    // Forbids any OTHER window within `gap` cells: Chebyshev distance when corners must
    // stay apart (noDiagonal), Manhattan otherwise so windows may still meet at a diagonal.
    bool CanPlace(int[] owner, int self, int c, int r)
    {
        if (!In(c, r) || Get(owner, c, r) != -1) return false;
        for (int dy = -gap; dy <= gap; dy++)
            for (int dx = -gap; dx <= gap; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int reach = noDiagonal ? Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy))
                                       : Mathf.Abs(dx) + Mathf.Abs(dy);
                if (reach > gap) continue;
                if (!In(c + dx, r + dy)) continue;
                int o = Get(owner, c + dx, r + dy);
                if (o != -1 && o != self) return false;
            }
        return true;
    }

    // Chooses the next step, biasing toward continuing straight by `straight`.
    bool PickNext(int[] owner, int wi, int cx, int cy, Vector2Int last, System.Random rng, out Vector2Int dir)
    {
        dir = Vector2Int.zero;
        var valid = new List<Vector2Int>(4);
        foreach (var d in Dirs)
            if (CanExtend(owner, wi, cx + d.x, cy + d.y)) valid.Add(d);
        if (valid.Count == 0) return false;

        bool straightOk = last != Vector2Int.zero && valid.Contains(last);
        if (straightOk && rng.NextDouble() < straight) { dir = last; return true; }

        dir = valid[rng.Next(valid.Count)];
        return true;
    }

    bool In(int c, int r)            => c >= 0 && c < cols && r >= 0 && r < rows;
    int  Get(int[] owner, int c, int r) => owner[r * cols + c];
    void Set(int[] owner, int c, int r, int v) => owner[r * cols + c] = v;

    void OnDisable() { if (preview != null) DestroyImmediate(preview); }
}
