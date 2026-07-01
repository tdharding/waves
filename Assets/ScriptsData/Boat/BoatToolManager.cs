using UnityEngine;

public enum BoatTool { WhirlSucker, Catapult, Lure, Boost }

public class BoatToolManager : MonoBehaviour
{
    [Header("Refs")]
    public GameObject boatCatapultRoot;
    public GameObject boatBoostRoot;
    public GameObject crossMastRoot;
    public BoatMovement boatMovement;
    public LureController lureController;
    public BoostController boostController;

    public BoatTool CurrentTool { get; private set; } = BoatTool.WhirlSucker;

    [Header("Unlocks")]
    public bool whirlUnlocked    = true;
    public bool catapultUnlocked = true;
    public bool lureUnlocked     = true;
    public bool boostUnlocked    = true;

    [Header("Lure")]
    public int amountOfLures = 3;

#if UNITY_EDITOR
    public static bool EditorOverrideWhirl     = true;
    public static bool EditorOverrideCatapult  = true;
    public static bool EditorOverrideLure      = true;
    public static bool EditorOverrideBoost     = true;
#endif

    public bool IsToolUnlocked(BoatTool tool)
    {
        switch (tool)
        {
            case BoatTool.WhirlSucker: return whirlUnlocked;
            case BoatTool.Catapult:    return catapultUnlocked;
            case BoatTool.Lure:        return lureUnlocked && (lureController == null || lureController.HasLureAvailable);
            case BoatTool.Boost:       return boostUnlocked;
            default: return false;
        }
    }

    private void Start()
    {
#if UNITY_EDITOR
        whirlUnlocked    = EditorOverrideWhirl;
        catapultUnlocked = EditorOverrideCatapult;
        lureUnlocked     = EditorOverrideLure;
        boostUnlocked    = EditorOverrideBoost;
#endif
        if (lureController != null)
        {
            lureController.InitStock(amountOfLures);
            lureController.OnLuresExhausted += OnLuresExhausted;
        }

        ApplyTool(CurrentTool);
    }

    public void SetLureController(LureController controller)
    {
        if (lureController != null)
            lureController.OnLuresExhausted -= OnLuresExhausted;

        lureController = controller;
        lureController.InitStock(amountOfLures);
        lureController.OnLuresExhausted += OnLuresExhausted;
        ApplyTool(CurrentTool);
    }

    private void OnDestroy()
    {
        if (lureController != null)
            lureController.OnLuresExhausted -= OnLuresExhausted;
    }

    private void OnLuresExhausted()
    {
        if (CurrentTool == BoatTool.Lure)
            SetTool(BoatTool.WhirlSucker);
        else
            ApplyTool(CurrentTool); // refresh root visibility even if tool didn't change
    }

    public void SetTool(BoatTool tool)
    {
        if (tool == CurrentTool) return;
        CurrentTool = tool;
        ApplyTool(tool);
        Debug.Log($"[BoatToolManager] Tool selected: {tool}");
    }

    private void ApplyTool(BoatTool tool)
    {
        bool catapultActive = tool == BoatTool.Catapult;
        bool lureActive     = tool == BoatTool.Lure;
        bool boostActive    = tool == BoatTool.Boost;

        if (boatCatapultRoot != null)
            boatCatapultRoot.SetActive(catapultActive);

        if (boatBoostRoot != null)
            boatBoostRoot.SetActive(boostActive);

        if (crossMastRoot != null)
            crossMastRoot.SetActive(!catapultActive && !lureActive && !boostActive);

        if (boatMovement != null)
            boatMovement.SetCatapultSlow(catapultActive);

        if (lureController != null)
            lureController.SetLoadedLureVisible(lureActive && lureController.HasLureAvailable);

        if (boostController != null)
            boostController.SetActive(boostActive);
    }
}
