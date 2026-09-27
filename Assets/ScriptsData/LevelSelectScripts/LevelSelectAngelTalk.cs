using UnityEngine;

/// <summary>
/// Talking to the angel on the level select map. She lands on an <see cref="AngelPerchPoint"/>
/// the designer put on a tower; this is what happens when you press the key beside it.
///
/// The map already has one way of interacting with things, so she uses it rather than bringing
/// her own: the <see cref="LevelSelectInteractor"/> owns the key, the prompt, the anchoring and
/// the held camera, exactly as it does for a poster. All that is left here is her part of it —
/// cutting to her camera, playing her talking animation, and stepping the lines she was given.
///
/// In a level she does all of that herself off her own talk key, because a level has no interact
/// system to lean on. Both routes end in the same place; this one simply lets the map keep one
/// key and one prompt for everything on it.
///
/// Sits beside the perch and a <see cref="LevelSelectInteractPoint"/>, all three written by the
/// Level Select Designer when the thing she stands on is generated.
/// </summary>
[RequireComponent(typeof(AngelPerchPoint))]
public class LevelSelectAngelTalk : LevelSelectInteractAction
{
    private AngelPerchPoint _perch;
    private AngelCompanion  _angel;
    private int             _line;   // which of her lines is on screen

    /// <summary>The perch this talks to, found beside it.</summary>
    public AngelPerchPoint Perch
    {
        get
        {
            if (_perch == null) _perch = GetComponent<AngelPerchPoint>();
            return _perch;
        }
    }

    // Resolved on demand, not cached for good: she is placed in the scene by hand and the map is
    // regenerated around her, so the one found on the first frame may not be the one standing
    // here later.
    private AngelCompanion Angel
    {
        get
        {
            if (_angel == null) _angel = FindFirstObjectByType<AngelCompanion>();
            return _angel;
        }
    }

    // Only offered while she is actually stood here. Without this the prompt would read "talk to
    // her" at an empty tower she left ten seconds ago.
    public override bool IsAvailable
    {
        get
        {
            var perch = Perch;
            var angel = Angel;
            return perch != null && perch.TalkEnabled && angel != null && angel.IsPerchedOn(perch);
        }
    }

    // The map keeps running while she talks: her lines fade in on the game clock, and a stopped
    // clock would hold them at nothing. The boat is still anchored and the view still held.
    public override bool PausesTime => false;

    // The glance goes up to her own look-at point, set on her prefab. She is placed by hand, so it
    // survives the map being generated around her. Without one, it goes to the perch she stands on.
    public override bool TryGetLookAt(out Vector3 worldPosition)
    {
        var angel = Angel;
        if (angel != null && angel.LookAtPoint != null)
        {
            worldPosition = angel.LookAtPoint.position;
            return true;
        }

        var perch = Perch;
        worldPosition = perch != null ? perch.PerchWorld : default;
        return perch != null;
    }

    public override bool Begin()
    {
        var angel = Angel;
        var perch = Perch;

        if (angel == null || perch == null) return false;
        if (!angel.BeginSceneTalk(perch)) return false;

        // Nothing to say is still a conversation — she turns to you and talks, there is just no
        // box. The same as a perch in a level with its talk text left empty.
        _line = 0;
        ShowLine();
        return true;
    }

    public override bool Step(bool advancePressed)
    {
        // She could go while you are reading — the map regenerated under her, or the angel taken
        // out of the scene. Close rather than hold the boat for something that is not there.
        if (Angel == null || Perch == null) return false;

        if (!advancePressed) return true;

        // One press is always "carry on", and the conversation ends off the back of the last
        // line — so the key never cuts away mid-sentence.
        _line++;
        if (_line >= Perch.TalkLines.Length) return false;

        ShowLine();
        return true;
    }

    public override void End()
    {
        HideLine();
        Angel?.EndSceneTalk();
    }

    // ─────────────────────────────────────────────
    // WHAT SHE SAYS
    // ─────────────────────────────────────────────

    // Through the game's own dialogue system where the scene has one, so her lines on the map
    // read exactly as they do in a level. The map has no dialogue controller of its own yet, so
    // the prompt strip stands in — it is already on screen, already in the right place, and the
    // interactor has just cleared it. Drop a DialogueTextController into the map and she uses
    // that instead, with nothing here to change.
    private void ShowLine()
    {
        var lines = Perch.TalkLines;
        if (_line < 0 || _line >= lines.Length) { HideLine(); return; }

        string line = lines[_line];

        var dialogue = DialogueTextController.Instance;
        if (dialogue != null) { dialogue.ShowHeld(line); return; }

        LevelSelectInteractPromptUI.Instance?.Show(line);
    }

    private void HideLine()
    {
        // HideAll, not Hide: Hide leaves the black panel up, since a timed sequence normally
        // lowers that itself at the end. Ending on the key has no such tail.
        var dialogue = DialogueTextController.Instance;
        if (dialogue != null) { dialogue.HideAll(); return; }

        LevelSelectInteractPromptUI.Instance?.Hide();
    }
}
