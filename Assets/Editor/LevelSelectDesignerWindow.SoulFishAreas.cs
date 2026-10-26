using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Soul Fish Areas: a pool ticked to hold souls. Its water fills with soul juice and the souls
/// listed here circle in it — round the ring between the island and the pool's edge, or round the
/// middle of an open bowl. TravellingSoulFishController reads it straight off the designer data,
/// so nothing needs generating for it.
///
/// The slots work as a route's origin pool does: a soul placed here is claimed on its SoulData,
/// so no level or route can take it too.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    // As the route slots: a slot-count change waits for the next Layout event.
    private int    _pendingAreaSlotCount = -1;
    private string _pendingAreaPoolId;

    private void DrawPoolSoulFishAreaProps(LevelSelectDesignerData.DesignerPool pool)
    {
        pool.soulFishAreaSouls ??= new List<SoulData>();
        var souls = pool.soulFishAreaSouls;

        EditorGUI.BeginChangeCheck();
        bool on = EditorGUILayout.Toggle(
            new GUIContent("Soul Fish Area", "Fill this pool with soul juice, with the souls below circling in " +
                                             "its water."),
            pool.soulFishArea);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Soul Fish Area");
            pool.soulFishArea = on;
            MarkDirty();
        }
        if (!pool.soulFishArea) return;

        if (Event.current.type == EventType.Layout &&
            _pendingAreaSlotCount >= 0 && _pendingAreaPoolId == pool.nodeId)
        {
            Undo.RecordObject(_data, "Soul Fish Area Slots");
            while (souls.Count < _pendingAreaSlotCount) souls.Add(null);
            while (souls.Count > _pendingAreaSlotCount)
            {
                ReleaseSoul(souls[souls.Count - 1]);
                souls.RemoveAt(souls.Count - 1);
            }
            _pendingAreaSlotCount = -1;
            _pendingAreaPoolId    = null;
            _allSouls             = null;
            MarkDirty();
        }

        EditorGUI.indentLevel++;
        EditorGUILayout.LabelField($"Souls in the pool ({CountAssigned(souls)}/{souls.Count})",
                                   EditorStyles.miniBoldLabel);

        int newCount = Mathf.Max(0, EditorGUILayout.DelayedIntField("Slots", souls.Count));
        if (newCount != souls.Count)
        {
            _pendingAreaSlotCount = newCount;
            _pendingAreaPoolId    = pool.nodeId;
            Repaint();
        }

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(EditorGUI.indentLevel * 15f);
        if (GUILayout.Button("Auto-Assign", EditorStyles.miniButton))
            AutoAssignAreaSouls(pool);
        if (GUILayout.Button("Release All", EditorStyles.miniButton))
            ReleaseAreaSouls(pool);
        EditorGUILayout.EndHorizontal();

        for (int i = 0; i < souls.Count; i++)
        {
            var picked = (SoulData)EditorGUILayout.ObjectField($"  {i + 1}", souls[i], typeof(SoulData), false);
            if (picked != souls[i])
            {
                Undo.RecordObject(_data, "Soul Fish Area Soul");
                ReleaseSoul(souls[i]);
                ClaimAreaSoul(picked, pool);
                souls[i] = picked;
                MarkDirty();
                _allSouls = null;
            }
        }

        if (souls.Count > 0)
            EditorGUILayout.LabelField("The water draws 32 fish at most, shared with the river and the tanks.",
                                       EditorStyles.miniLabel);
        EditorGUI.indentLevel--;
    }

    private static int CountAssigned(List<SoulData> souls)
    {
        int n = 0;
        foreach (var soul in souls) if (soul != null) n++;
        return n;
    }

    /// <summary>Marks a soul as taken by this pool — named after its node, as a route's are after the route.</summary>
    private static void ClaimAreaSoul(SoulData soul, LevelSelectDesignerData.DesignerPool pool)
    {
        if (soul == null) return;
        Undo.RecordObject(soul, "Allocate Soul To Soul Fish Area");
        soul.allocated          = true;
        soul.allocatedToLevelID = $"soulfisharea:{pool.nodeId}";
        EditorUtility.SetDirty(soul);
    }

    /// <summary>Fills empty slots from souls nothing has claimed yet, as a route's Auto-Assign does.</summary>
    private void AutoAssignAreaSouls(LevelSelectDesignerData.DesignerPool pool)
    {
        var souls = pool.soulFishAreaSouls;
        int empty = souls.Count - CountAssigned(souls);
        if (empty == 0)
        {
            EditorUtility.DisplayDialog("Auto-Assign Souls",
                souls.Count == 0
                    ? "This pool has no slots yet. Set 'Slots' to the number of souls that should circle "
                      + "in it, then Auto-Assign fills them."
                    : "Every slot in this pool is already filled. Add slots, or Release All first.",
                "OK");
            return;
        }

        EnsureSoulCache();
        var free = new Queue<SoulData>();
        if (_allSouls != null)
            foreach (var soul in _allSouls)
                if (soul != null && !soul.allocated) free.Enqueue(soul);

        if (free.Count == 0)
        {
            EditorUtility.DisplayDialog("Auto-Assign Souls",
                "No unallocated souls available. Free some with the Game Tester tool's soul cleanup.", "OK");
            return;
        }

        Undo.RecordObject(_data, "Auto-Assign Soul Fish Area");
        int assigned = 0;
        for (int i = 0; i < souls.Count && free.Count > 0; i++)
        {
            if (souls[i] != null) continue;
            var soul = free.Dequeue();
            ClaimAreaSoul(soul, pool);
            souls[i] = soul;
            assigned++;
        }

        MarkDirty();
        // The claims live on the soul assets, not the designer's working clone — written out now,
        // as a route's are.
        AssetDatabase.SaveAssets();
        _allSouls = null;

        Debug.Log($"[LevelSelectDesigner] Assigned {assigned} soul(s) to the Soul Fish Area on '{pool.nodeId}'. " +
                  $"{free.Count} unallocated remaining.");
        if (assigned < empty)
            EditorUtility.DisplayDialog("Auto-Assign Souls",
                $"Filled {assigned} of {empty} empty slot(s) — the unallocated pile ran out.", "OK");
    }

    /// <summary>Empties the pool's slots and hands its souls back to the unallocated pile.</summary>
    private void ReleaseAreaSouls(LevelSelectDesignerData.DesignerPool pool)
    {
        var souls = pool.soulFishAreaSouls;
        Undo.RecordObject(_data, "Release Soul Fish Area");
        int released = 0;
        for (int i = 0; i < souls.Count; i++)
        {
            if (souls[i] == null) continue;
            ReleaseSoul(souls[i]);
            souls[i] = null;
            released++;
        }

        MarkDirty();
        AssetDatabase.SaveAssets();
        _allSouls = null;
        Debug.Log($"[LevelSelectDesigner] Released {released} soul(s) from the Soul Fish Area on '{pool.nodeId}'.");
    }
}
