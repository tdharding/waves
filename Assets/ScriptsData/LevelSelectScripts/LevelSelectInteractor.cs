using UnityEngine;

/// <summary>
/// The boat's side of interacting: it watches the <see cref="LevelSelectInteractPoint"/>s on the
/// map, prompts for the nearest one in reach, and on the key runs what that point carries.
///
/// One of these in the level select scene. While something is running the boat is anchored and
/// its own anchor key is locked, so there is no sailing off from under a poster — and the
/// anchor is remembered rather than assumed, so closing one does not lift an anchor you had
/// dropped yourself. The same borrow the angel makes for a conversation in a level.
///
/// The camera is held facing as well as the boat held still, and the clock is stopped for as
/// long as it is up, so nothing on the map moves on behind what you are looking at.
/// </summary>
public class LevelSelectInteractor : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The boat. Left empty, the one in the scene is found at start.")]
    [SerializeField] private LevelSelectBoatControl boatControl;

    [Header("Input")]
    [Tooltip("Pressed in reach of a point to start it, and pressed again to carry it on or " +
             "close it.")]
    public KeyCode interactKey = KeyCode.E;

    // ── State ──────────────────────────────────────────────────────
    private LevelSelectInteractPoint _running;     // what is up right now, if anything
    private bool                     _anchorBefore; // was the boat anchored before it opened?

    /// <summary>Whether an interaction is up right now.</summary>
    public bool IsRunning => _running != null;

    private void Awake()
    {
        if (boatControl == null) boatControl = FindObjectOfType<LevelSelectBoatControl>();

        if (boatControl == null)
            Debug.LogWarning("[LevelSelectInteractor] No boat in the scene — nothing to measure " +
                             "reach from, so nothing will ever prompt.", this);
    }

    private void Update()
    {
        if (boatControl == null) return;

        // A paused map holds whatever is on screen: the key is not read, and a poster that is
        // up stays up rather than being closed behind the menu.
        if (PauseManager.IsPaused) return;

        // The intro drives the boat itself and a menu takes it away entirely — neither is a
        // time to be standing reading a poster.
        if (boatControl.IntroMode || boatControl.ControlsFrozen)
        {
            if (IsRunning) Close();
            Prompt(null);
            return;
        }

        bool pressed = Input.GetKeyDown(interactKey);

        if (IsRunning)
        {
            // Re-asserted every frame rather than set once: the pause menu puts the clock back
            // to 1 on its way out, and a poster that was up behind it is still up.
            Time.timeScale = 0f;

            StepRunning(pressed);
            return;
        }

        var nearest = Nearest();
        Prompt(nearest);

        if (pressed && nearest != null) Open(nearest);
    }

    private void OnDisable()
    {
        // Leaving the scene, or this being turned off, must not leave the boat anchored with a
        // poster on screen and no way to press it away.
        if (IsRunning) Close();
    }

    // ─────────────────────────────────────────────
    // WHAT IS IN REACH
    // ─────────────────────────────────────────────

    /// <summary>The point in reach whose middle the boat stands nearest, or none.</summary>
    private LevelSelectInteractPoint Nearest()
    {
        Vector3 boat    = boatControl.Position;
        LevelSelectInteractPoint best = null;
        float   bestDistance = float.MaxValue;

        var points = LevelSelectInteractPoint.All;
        for (int i = 0; i < points.Count; i++)
        {
            var point = points[i];
            if (point == null || !point.InReach(boat)) continue;

            float distance = point.FlatDistanceTo(boat);
            if (distance >= bestDistance) continue;

            best         = point;
            bestDistance = distance;
        }

        return best;
    }

    private void Prompt(LevelSelectInteractPoint point)
    {
        var ui = LevelSelectInteractPromptUI.Instance;
        if (ui == null) return;

        if (point == null) ui.Hide();
        else               ui.Show($"[{interactKey}] {point.prompt}");
    }

    // ─────────────────────────────────────────────
    // RUNNING ONE
    // ─────────────────────────────────────────────

    private void Open(LevelSelectInteractPoint point)
    {
        var action = point.Action;
        if (action == null || !action.Begin()) return;

        _running = point;

        // Remembered, not assumed — you may have anchored to look around before pressing.
        _anchorBefore = boatControl.IsAnchored;
        boatControl.SetAnchored(true);
        boatControl.SetKeyLocked(true);

        // The view is held too: turning about the boat while a poster is up would swing the
        // camera off whatever brought it up.
        boatControl.SetLookLocked(true);

        // The map holds its breath while you look: the water, the fish and everything else on
        // it stop where they are rather than carrying on behind what is on screen.
        Time.timeScale = 0f;

        // The prompt is what brought you here; it has no business sitting over the poster.
        Prompt(null);
    }

    private void StepRunning(bool pressed)
    {
        var action = _running.Action;
        if (action == null) { Close(); return; }

        if (!action.Step(pressed)) Close();
    }

    private void Close()
    {
        var point = _running;
        _running = null;

        point?.Action?.End();

        // The clock starts again — unless a menu is up, which has stopped it for its own
        // reasons and will start it again itself.
        if (!PauseManager.IsPaused) Time.timeScale = 1f;

        if (boatControl != null)
        {
            boatControl.SetKeyLocked(false);
            boatControl.SetLookLocked(false);
            boatControl.SetAnchored(_anchorBefore);
        }
    }
}
