using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Debug/test panel on the main menu.
/// Type a soul count, press New Game — save data is cleared then that many
/// random souls are injected before loading the next scene.
/// Wire the New Game button to OnNewGamePressed().
/// SoulData assets must live under a Resources/Souls/ folder.
/// </summary>
public class TestSoulInjectorUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_InputField soulCountInput;

    [Header("Scene")]
    [SerializeField] private string nextSceneName = "LevelSelect";

    [Header("Defaults")]
    [SerializeField] private int defaultSoulCount = 5;

    public void OnNewGamePressed()
    {
        int count = defaultSoulCount;

        if (soulCountInput != null && !string.IsNullOrWhiteSpace(soulCountInput.text))
            int.TryParse(soulCountInput.text, out count);

        count = Mathf.Max(0, count);

        GameProgressData.ClearAll();

        if (count > 0)
            InjectRandomSouls(count);

        Debug.Log($"[TestSoulInjector] New game — {count} soul(s) injected.");
        SceneManager.LoadScene(nextSceneName);
    }

    private static void InjectRandomSouls(int count)
    {
        SoulData[] allSouls = Resources.LoadAll<SoulData>("Souls");

        if (allSouls == null || allSouls.Length == 0)
        {
            Debug.LogWarning("[TestSoulInjector] No SoulData assets found in Resources/Souls/.");
            return;
        }

        // Only pick from unallocated souls (those not assigned in the Grid Designer)
        var pool = new System.Collections.Generic.List<SoulData>();
        foreach (var s in allSouls)
            if (!s.allocated) pool.Add(s);

        if (pool.Count == 0)
        {
            Debug.LogWarning("[TestSoulInjector] No unallocated SoulData assets found — using all souls.");
            pool = new System.Collections.Generic.List<SoulData>(allSouls);
        }

        // Shuffle and pick without replacement to avoid duplicate identities
        var ids  = new System.Collections.Generic.List<int>();
        var levelLocalIDs = new System.Collections.Generic.List<int>();

        count = Mathf.Min(count, pool.Count);
        for (int i = 0; i < count; i++)
        {
            int idx = Random.Range(i, pool.Count);
            (pool[i], pool[idx]) = (pool[idx], pool[i]); // swap
            ids.Add(pool[i].soulDataIdentity);
            levelLocalIDs.Add(-1); // -1 = not linked to any level cell, matches real soul collection
        }

        GameProgressData.SaveSoulsToBoat(ids, levelLocalIDs);
    }
}
