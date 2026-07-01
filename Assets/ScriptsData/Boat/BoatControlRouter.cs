using UnityEngine;

public class BoatControlRouter : MonoBehaviour
{
    [Header("Refs")]
    public BoatMovement boatMovement;
    public SonarSystemController sonar;
    public FishingController fishing;

    [Header("Tool System")]
    public AbilityWheelUI abilityWheel;
    public BoatToolManager toolManager;
    public CatapultController catapult;
    public LureController lureController;
    public BoostController boostController;

    private bool spaceWasPressedAfterSonar = false;

    public void SetLureController(LureController controller)
    {
        lureController = controller;
    }

    void Update()
    {
        if (PauseManager.IsPaused)
            return;

        // --- ABILITY WHEEL ---
        if (Input.GetKeyDown(KeyCode.Tab)) abilityWheel.Show();
        if (Input.GetKeyUp(KeyCode.Tab))   abilityWheel.Hide();

        if (abilityWheel.IsOpen)
        {
            if (Input.GetKeyDown(KeyCode.LeftArrow))  abilityWheel.CycleLeft();
            if (Input.GetKeyDown(KeyCode.RightArrow)) abilityWheel.CycleRight();
            return; // suppress all other input while wheel is open
        }

        bool spaceHeld = Input.GetKey(KeyCode.Space);
        bool spaceDown = Input.GetKeyDown(KeyCode.Space);

        // --- CATAPULT TOOL ---
        if (toolManager.CurrentTool == BoatTool.Catapult)
        {
            fishing.SetFishingActive(false);
            boatMovement.SetBoosting(false);
            boatMovement.SetSonarSlow(false);
            spaceWasPressedAfterSonar = false;

            if (spaceDown) catapult.Fire();
            return;
        }

        // --- LURE TOOL ---
        if (toolManager.CurrentTool == BoatTool.Lure)
        {
            catapult.CancelArm();
            bool sonarForLure = sonar != null && sonar.IsSonarActive;

            if (sonarForLure)
            {
                boatMovement.SetBoosting(false);
                boatMovement.SetSonarSlow(true);
                if (spaceDown && lureController != null)
                    lureController.DropLure();
            }
            else
            {
                boatMovement.SetSonarSlow(false);
                boatMovement.SetBoosting(spaceHeld);
            }

            if (fishing.IsFishingActive)
                fishing.SetFishingActive(false);

            return;
        }

        // --- BOOST TOOL ---
        if (toolManager.CurrentTool == BoatTool.Boost)
        {
            catapult.CancelArm();
            if (fishing.IsFishingActive)
                fishing.SetFishingActive(false);

            boatMovement.SetSonarSlow(false);

            if (boostController != null)
                boostController.Tick(spaceHeld);
            else
                boatMovement.SetBoosting(spaceHeld);

            return;
        }

        // --- WHIRL SUCKER TOOL (existing logic) ---
        // Safe: cancel any partial catapult charge from a mid-game tool swap
        catapult.CancelArm();

        bool sonarActive = sonar != null && sonar.IsSonarActive;

        if (!sonarActive)
            spaceWasPressedAfterSonar = false;

        if (sonarActive && spaceDown)
            spaceWasPressedAfterSonar = true;

        if (sonarActive)
        {
            if (spaceHeld && spaceWasPressedAfterSonar && !fishing.IsFishingActive)
                fishing.StartFishing();
            else if (!spaceHeld && fishing.IsFishingActive)
                fishing.SetFishingActive(false);

            boatMovement.SetBoosting(false);
            boatMovement.SetSonarSlow(true);
        }
        else
        {
            if (fishing.IsFishingActive)
                fishing.SetFishingActive(false);

            boatMovement.SetSonarSlow(false);
            boatMovement.SetBoosting(spaceHeld);
        }
    }
}
