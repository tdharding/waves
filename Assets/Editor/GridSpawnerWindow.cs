using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class GridSpawnerWindow : EditorWindow
{
    GridData grid;
    Transform plane;

    List<GameObject> prefabs = new List<GameObject>();

    bool applyRotationOffset;
    bool createParent = true;
    string parentName = "SpawnedGrid";

    [MenuItem("Tools/Waves/Grid Spawner")]
    public static void Open()
    {
        GetWindow<GridSpawnerWindow>("Grid Spawner");
    }

    void OnEnable()
    {
        if (prefabs.Count == 0)
        {
            prefabs.Add(null);
            prefabs.Add(null);
            prefabs.Add(null);
        }
    }

    private void OnGUI()
    {
        grid = (GridData)EditorGUILayout.ObjectField("Grid Data", grid, typeof(GridData), false);
        plane = (Transform)EditorGUILayout.ObjectField("Plane Transform", plane, typeof(Transform), true);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Prefab Slots", EditorStyles.boldLabel);

        for (int i = 0; i < prefabs.Count; i++)
        {
            prefabs[i] = (GameObject)EditorGUILayout.ObjectField(
                $"Slot {i + 1}",
                prefabs[i],
                typeof(GameObject),
                false
            );
        }

        if (GUILayout.Button("+ Add Slot"))
            prefabs.Add(null);

        EditorGUILayout.Space();
        applyRotationOffset = EditorGUILayout.Toggle("Apply -90° X Rotation", applyRotationOffset);
        createParent = EditorGUILayout.Toggle("Create Parent Object", createParent);

        if (createParent)
            parentName = EditorGUILayout.TextField("Parent Name", parentName);

        EditorGUILayout.Space();

        if (GUILayout.Button("SPAWN", GUILayout.Height(40)))
            if (grid && plane) SpawnGrid();
    }

    void SpawnGrid()
    {
        Renderer r = plane.GetComponent<Renderer>();
        if (!r) return;

        Bounds b = r.bounds;
        float tileX = b.size.x / GridData.GridSize;
        float tileZ = b.size.z / GridData.GridSize;

        Vector3 origin = b.min;

        // Waterline is authored on the level itself now (Grid Designer > Arena).
        float baselineY = grid != null ? grid.waterlineY : plane.position.y;
        Quaternion baselineRot = Quaternion.identity;

        Transform parent = null;

        if (createParent)
        {
            GameObject root = new GameObject(parentName);
            Undo.RegisterCreatedObjectUndo(root, "Create Spawn Parent");
            root.transform.position = plane.position;
            root.transform.rotation = plane.rotation;
            parent = root.transform;
        }

        for (int y = 0; y < GridData.GridSize; y++)
        {
            int flippedY = GridData.GridSize - 1 - y;

            for (int x = 0; x < GridData.GridSize; x++)
            {
                int index = flippedY * GridData.GridSize + x;
                int cell = grid.cells[index];

                if (cell <= 0 || cell > prefabs.Count) continue;

                GameObject prefab = prefabs[cell - 1];
                if (!prefab) continue;

                var   baselineAlign = prefab.GetComponentInChildren<PrefabBaselineAlignment>();
                float contactOffsetY = baselineAlign != null ? baselineAlign.transform.position.y : 0f;
                Vector3 pos = new Vector3(
                    origin.x + x * tileX + tileX * 0.5f,
                    baselineY - contactOffsetY,
                    origin.z + y * tileZ + tileZ * 0.5f
                );

                bool align = baselineAlign != null;
                Quaternion rot = align ? baselineRot : plane.rotation;
                if (applyRotationOffset)
                    rot *= Quaternion.Euler(-90, 0, 0);

                GameObject obj = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                Undo.RegisterCreatedObjectUndo(obj, "Spawn Grid Object");

                obj.transform.SetPositionAndRotation(pos, rot);
                if (parent) obj.transform.SetParent(parent);
            }
        }
    }
}
