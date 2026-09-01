using UnityEngine;
using UnityEditor;

/// <summary>
/// Live readout for the fog field, in the spirit of RockRingManagerEditor: the numbers that tell
/// you whether the thing is actually running, and which of them is the one biting when it isn't.
///
/// Worth watching the dot count. With elliptical BaseDots a blob should sit somewhere around
/// 15-30 dots; if it is up near a hundred, the ellipse stretch on the preset has been left at 1
/// and every limb is being laid down with round dots — which costs four times as much for no
/// visible difference once the grid is blurred.
/// </summary>
[CustomEditor(typeof(FogFieldManager))]
public class FogFieldManagerEditor : Editor
{
    public override bool RequiresConstantRepaint() => true;

    Vector2 _reportScroll;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Live", EditorStyles.boldLabel);

        int blobs = FogFieldManager.BlobCount;
        int dots  = FogFieldManager.DotTotal;
        int reps  = FogFieldManager.RepellerCount;

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.IntField(new GUIContent("Blobs",
                "Fog masses currently alive, before the budget."), blobs);
            EditorGUILayout.IntField(new GUIContent("BaseDots",
                "Dots that reached the GPU this frame, across every blob."), dots);
            EditorGUILayout.IntField(new GUIContent("Dots per blob",
                "Around 15-30 with elliptical dots. Near a hundred means the preset's ellipse " +
                "stretch is still 1."), blobs > 0 ? dots / blobs : 0);
            EditorGUILayout.IntField(new GUIContent("Faded out",
                "Masses alive but beyond the mask, painting nothing. Persistently equal to the " +
                "blob count means the mask is too small for where the map places masses."),
                FogFieldManager.FadedCount);
            EditorGUILayout.IntField(new GUIContent("Repellers",
                "Registered components. Rocks adopted from IRockRing are counted separately and " +
                "do not appear here."), reps);

        }

        var mgr = (FogFieldManager)target;
        var paint = serializedObject.FindProperty("paintMaterial");
        var blur  = serializedObject.FindProperty("blurMaterial");

        EditorGUILayout.Space(4);

        if (paint.objectReferenceValue == null || blur.objectReferenceValue == null)
        {
            EditorGUILayout.HelpBox(
                "No fog until both materials are assigned. FogPaint.mat and FogBlur.mat are in " +
                "Assets/ScriptsData/FogScripts.", MessageType.Warning);
        }
        else if (serializedObject.FindProperty("fogMap").objectReferenceValue == null)
        {
            EditorGUILayout.HelpBox(
                "No Fog Map, so this level gets no fog. There is no fallback — the map is " +
                "the only thing that decides where fog sits. Author one in Waves > Fog Map.",
                MessageType.Warning);
        }
        else if (Application.isPlaying && blobs == 0)
        {
            EditorGUILayout.HelpBox(
                "A map is assigned but no masses exist. Either its cloud list is empty, or every " +
                "allocation sits outside the cull radius from the boat.", MessageType.Warning);
        }
        else if (blobs > 0 && dots == 0 && FogFieldManager.FadedCount >= blobs)
        {
            // Distinguishing these two matters: they look identical from a dot count of zero and
            // the fixes are opposite ends of the system.
            EditorGUILayout.HelpBox(
                $"All {blobs} masses are outside the mask radius, so they paint nothing. Either " +
                "the mask is too small for where the map places them, or the wind is carrying " +
                "them out faster than slots refill.", MessageType.Warning);
        }
        else if (blobs > 0 && dots == 0)
        {
            EditorGUILayout.HelpBox(
                "Masses are inside the mask but lay down no dots. Their shape has no limbs and a " +
                "zero-thickness spine curve.", MessageType.Warning);
        }

        var sheet = Object.FindAnyObjectByType<FogSheetMesh>();
        if (sheet == null)
        {
            EditorGUILayout.HelpBox(
                "No FogSheetMesh in the scene, so nothing draws the field.", MessageType.Warning);
        }
        else if (sheet.BuiltSize > 0f && sheet.BuiltSize < mgr.SheetSize - 0.01f)
        {
            // The sheet has to cover the ARENA, not the painted window. The window is only a few
            // units across and travels with the boat, so comparing against it would pass almost
            // always and tell you nothing — while the boat sails off the edge of the sheet.
            EditorGUILayout.HelpBox(
                $"The fog sheet is {sheet.BuiltSize:0.#} u but the arena is {mgr.SheetSize:0.#} u. " +
                "The fog window travels with the boat, so anywhere the sheet does not reach has " +
                "nothing to draw on and the fog stops at a straight edge. Turn on Match Field " +
                "Coverage.", MessageType.Warning);
        }

        EditorGUILayout.Space(8);
        if (GUILayout.Button(new GUIContent("Capture 3-Frame Snapshot",
                "Write everything that decides what reaches the screen — extent, detail, texel " +
                "size, every mass and its dot sizes in texels — to FogSnapshot.txt beside the " +
                "project, and copy it to the clipboard."), GUILayout.Height(26)))
            mgr.CaptureSnapshot();

        EditorGUILayout.Space(4);
        if (GUILayout.Button(new GUIContent("Capture Fog Distribution",
                "Where the masses actually are: angle, radial density, nearest-neighbour spacing " +
                "and which side births are landing on."), GUILayout.Height(26)))
            mgr.CaptureDistribution();

        if (!string.IsNullOrEmpty(mgr.DistributionReport))
        {
            // A selectable text area rather than a console line: click in, Ctrl+A, Ctrl+C. The
            // console truncates and reformats, which loses the columns this is built out of.
            _reportScroll = EditorGUILayout.BeginScrollView(_reportScroll, GUILayout.Height(220));
            EditorGUILayout.TextArea(mgr.DistributionReport, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy")) EditorGUIUtility.systemCopyBuffer = mgr.DistributionReport;
            if (GUILayout.Button("Clear")) mgr.DistributionReport = "";
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.HelpBox(
            "The grid is built offscreen — nothing here draws to the screen. Fog only becomes " +
            "visible once a plane carrying FogSheet.mat sits just above the waterline. Until " +
            "then, select this object and check the gizmos: coverage square, cull radius, and a " +
            "disc per live blob.", MessageType.None);
    }
}
