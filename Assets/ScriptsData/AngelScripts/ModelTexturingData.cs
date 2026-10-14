using System.Collections.Generic;
using UnityEngine;

// Model Texturing Tool data: groups of faces on a mesh, each with the colour it is filled with and
// the gradients laid over it (each fading out from its own lines across the group's faces).
// Faces are selected and grouped in Tools ▸ Characters ▸ Model Texturing Tool, and baked into a map
// with its own layout — each group's pieces of the generated unique UVs, scaled to the group's share
// of the map, written into the replicant's UV1: RGB = the group's colour, A = 1 where a group is (0 where
// none is). How see-through a Transparency group is isn't in the map: it's a property on the transparent
// material, "<group>_Transparency". Faces are stored as triangle indices, so they only hold while the
// model keeps its triangle count.
[CreateAssetMenu(menuName = "Waves/Model Texturing Data", fileName = "ModelTexturingData")]
public class ModelTexturingData : ScriptableObject
{
    [System.Serializable]
    public class Fill
    {
        public string name = "Group";
        // Triangle indices into mesh.triangles / 3 (all submeshes, in order).
        public List<int> triangles = new List<int>();
        public Color colour = new Color(0.3f, 0.3f, 0.35f, 1f);
        // The window's Transparency box, stored inverted. Ticked, the group's faces draw with the
        // transparent material, as see-through as Transparency says (0 = solid, 1 = gone); unticked, it's
        // opaque. The amount is the colour's alpha, and the transparent material carries it as
        // "<name>_Transparency" — the window keeps the two the same.
        public bool opacityOff;
        public bool Transparent => !opacityOff;
        public float Transparency => colour.a;
        // The fill's noise: how much it breaks the colour up towards the base colour (0 = flat) and
        // how fine its grain is (grains across the model's longest side). Baked into the shade map.
        [Range(0f, 1f)] public float noiseStrength = 0.5f;
        public float noiseScale = 40f;
        // Noise left out of this group: it bakes as a flat colour (the window's Noise box, unticked).
        public bool noiseOff;
        // How much of the shade map the group's faces get, as a part of all the groups' shares (the
        // window's Map Space bar keeps them adding up to 1). More map = finer detail, finer noise.
        public float mapShare = 1f;

        // Editor only — the bake ignores both. Locked: its faces can't be picked, so a click goes
        // through to the faces behind. Hidden: not drawn in the stage, and can't be picked either.
        public bool locked;
        public bool hidden;

        // The group's gradients, laid over its colour (and noise) in list order. Each only paints this
        // group's faces.
        public List<LineGradient> gradients = new List<LineGradient>();

        // A gradient from the group's own outline — every line with a group face on one side only —
        // laid before the gradients above. Its lines are worked out from the faces at each bake, so
        // it follows the group as faces come and go; outerLinesGradient.lines stays empty.
        public bool gradientOuterLines;
        public LineGradient outerLinesGradient = new LineGradient { name = "Outer Lines" };

        // Images stamped onto the group's faces, laid over everything above in list order. Each
        // only paints this group's faces.
        public List<Decal> decals = new List<Decal>();

        // Gradient settings from the first pass, before gradients had their own lines (see gradients).
        // Not shown in the window and not used by the bake.
        public List<Vector3> startEdges = new List<Vector3>();
        [Range(0f, 1f)] public float strength = 1f;
        [Range(0.05f, 1f)] public float length = 1f;
    }

    // A colour that fades out from a set of lines (mesh edges) across its group's faces, measured
    // over the surface, so it wraps round the model rather than cutting through it.
    [System.Serializable]
    public class LineGradient
    {
        public string name = "Gradient";
        // Its lines: pairs of rest-pose (bind-pose) points, one pair per line.
        public List<Vector3> lines = new List<Vector3>();
        // The same lines as corners of the model's faces (face number × 3 + corner), a pair per
        // line, so they follow the model when it's reshaped in Blender — a corner keeps its number
        // when Unity renumbers the vertices (the generated UVs split them by shape). Empty on
        // gradients made before these — `lines` is read then, and they're filled in the first time
        // those points are found on the model.
        public List<int> lineCorners = new List<int>();
        public Color colour = new Color(0.1f, 0.1f, 0.12f, 1f);   // alpha = its opacity at the lines
        // How far it reaches from its lines, as a part of the farthest the group's faces reach from
        // them (1 = it fades out at the far side of the group).
        [Range(0.01f, 1f)] public float length = 0.5f;
    }

    // An image projected onto the model from the spot it was placed, straight along the surface
    // there, like a projector held against it. Faces turned away from it don't take it.
    [System.Serializable]
    public class Decal
    {
        public string name = "Decal";
        public Texture2D texture;
        // Where it was placed: a rest-pose (bind-pose) point and the surface's facing there, and
        // which way the image's top points (the Scene view's up when it was placed, laid flat on
        // the surface).
        public Vector3 point;
        public Vector3 normal = Vector3.forward;
        public Vector3 up = Vector3.up;
        // The same, pinned to the face it sits on, so it rides that face when the model is reshaped
        // in Blender: the face's number, and the spot and the up as amounts of the face's two sides
        // from its first corner (the facing is the face's own). -1 = not pinned yet (decals made
        // before this) — point/normal/up are read then, and it's pinned the first time that spot
        // is found on the model.
        public int triangle = -1;
        public Vector2 facePoint;
        public Vector2 faceUp;
        // Its width and height, and how far it is slid across the surface from where it was placed —
        // all as parts of the model's longest side.
        public Vector2 scale = new Vector2(0.1f, 0.1f);
        public Vector2 offset;
        [Range(0f, 1f)] public float opacity = 1f;
    }

    // A line kept sharp when the replicant is smoothed (Blender's edge crease).
    [System.Serializable]
    public class Crease
    {
        // The line's two ends as corners of the model's faces (face number × 3 + corner), like a
        // gradient's lineCorners, so it follows the model when it's reshaped in Blender. -1 while it
        // is only on its rest-pose points (a, b) — it's pinned again the first time they're found.
        public int cornerA = -1, cornerB = -1;
        public Vector3 a, b;
        // Only read when moving creases made before crease groups into groups (see OnEnable);
        // a line's sharpness is its group's amount.
        [Range(0f, 1f)] public float amount = 1f;
    }

    // Lines creased together, all as sharp as the group's amount. A line is in one crease group.
    [System.Serializable]
    public class CreaseGroup
    {
        public string name = "Crease Group";
        // 1 = a hard edge; in between, that much of the hard edge mixed into the smooth one.
        [Range(0f, 1f)] public float amount = 1f;
        public List<Crease> lines = new List<Crease>();
    }

    public Mesh mesh;
    // The window's Subdivision box: the replicant comes out smoothed the way Blender's Subdivision
    // Surface does it (Catmull-Clark), this many times over — so every model wearing it is smooth.
    // Off, it's the plain replicant again; the model file is never changed. Faces are still picked
    // and grouped on the unsmoothed shape, and each keeps its own part of the map.
    public bool subdivision;
    [Range(1, 2)] public int subdivisionLevels = 1;
    public List<CreaseGroup> creaseGroups = new List<CreaseGroup>();
    // Creases made before crease groups — moved into groups on load, one per amount (see OnEnable).
    public List<Crease> creases = new List<Crease>();
    // The replicant: a copy of the mesh on the same armature, its faces split by group — opaque
    // faces in submesh 0, Transparency groups' faces in submesh 1 — its UV1 the shade map's layout
    // (vertices split along group edges, so it has more than the original), and its UV2.x each
    // vertex's place in the list of Transparency groups (-1 for the rest), which picks the
    // transparent material's "<group>_Transparency". Smoothed when subdivision is on. Made when the
    // model opens in the stage; the model file itself is never changed. Faces are still numbered by
    // the original mesh.
    public Mesh replicant;
    public int triangleCount;
    public int textureSize = 1024;
    public Texture2D shadeMap;
    // The materials are named from the title: "<title>Opaque" and "<title>Transparent", saved next to
    // this data. Empty = the mesh's name.
    public string materialTitle = "";
    // The opaque faces' material (AngelMaterial graph) — submesh 0 of the replicant.
    public Material material;
    // The see-through faces' material — submesh 1, made only once a group has Transparency ticked. Its
    // graph is the model's own copy of AngelMaterialTransparent, "<title>Transparent.shadergraph" next
    // to this data, with a "<group>_Transparency" property per Transparency group.
    public Material transparentMaterial;
    public List<Fill> fills = new List<Fill>();

    // 1 = the groups' box is Transparency. Before it was Opacity (2026-10-07): the alpha was how much
    // the group covered, and a ticked group only went see-through below 1.
    public int transparencyVersion;

    void OnEnable()
    {
        if (transparencyVersion < 1)
        {
            // A group ticked at full opacity was opaque, so it comes out unticked; the rest keep their
            // number, now read as transparency (the user's call).
            foreach (var g in fills)
                if (!g.opacityOff && g.colour.a >= 1f) g.opacityOff = true;
            transparencyVersion = 1;
        }

        // Creases from before crease groups: one group per amount used.
        if (creases != null && creases.Count > 0)
        {
            creaseGroups ??= new List<CreaseGroup>();
            var byAmount = new Dictionary<float, CreaseGroup>();
            foreach (var c in creases)
            {
                if (!byAmount.TryGetValue(c.amount, out var group))
                {
                    group = new CreaseGroup { name = "Crease Group " + (creaseGroups.Count + 1), amount = c.amount };
                    creaseGroups.Add(group);
                    byAmount[c.amount] = group;
                }
                group.lines.Add(c);
            }
            creases.Clear();
        }
    }
}
