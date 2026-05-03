using UnityEngine;

public class BoatControlRouter : MonoBehaviour
{
    [Header("Refs")]
    public BoatMovement boatMovement;
    public SonarSystemController sonar;
    public FishingController fishing;

    private bool spaceWasPressedAfterSonar = false;

  void Update()
{
    if (PauseManager.IsPaused)
        return;

    bool spaceHeld = Input.GetKey(KeyCode.Space);
    bool spaceDown = Input.GetKeyDown(KeyCode.Space);
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