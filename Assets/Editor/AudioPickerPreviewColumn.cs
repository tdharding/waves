using System.Reflection;
using UnityEditor;
using UnityEditor.Search;
using UnityEngine;
using UnityEngine.UIElements;

// A ▶ button column for Unity's Search picker (the "Select Audio Clip" window).
// Add it from the picker's column "+" menu: Selectors > AudioPreview.
// The first time, it comes in already formatted as the play button; if it ever shows
// as plain text, right-click its header > Column Format > Audio Preview.
[InitializeOnLoad]
static class AudioPickerPreviewColumn
{
    const string ColumnFormat = "AudioPreview";
    const string ColumnPath   = "Selectors/AudioPreview";

    static Button s_PlayingButton;
    static string s_PlayingItemId;

    static AudioPickerPreviewColumn()
    {
        // Search remembers each column's format under this key. Seed it once so a newly
        // added AudioPreview column arrives as the play button rather than an empty text cell.
        string key = "Search.Column.provider." + ColumnPath;
        if (!EditorPrefs.HasKey(key))
            EditorPrefs.SetString(key, ColumnFormat);
    }

    // The column's value — only there so the column shows up in the "+" menu.
    [SearchSelector("AudioPreview", provider: "asset")]
    static object SelectAudioPreview(SearchItem item) => string.Empty;

    [SearchColumnProvider(ColumnFormat)]
    static void InitializeColumn(SearchColumn column)
    {
        column.cellCreator = _ => CreateButton();
        column.binder      = BindButton;
    }

    static VisualElement CreateButton()
    {
        var button = new Button { text = "▶" };
        button.style.width    = 24;
        button.style.height   = 16;
        button.style.marginTop = button.style.marginBottom = 0;
        button.style.paddingTop = button.style.paddingBottom = 0;
        button.clicked += () => OnClicked(button);
        button.RegisterCallback<DetachFromPanelEvent>(_ =>
        {
            // Picker closed while this one was playing.
            if (s_PlayingButton == button) Stop();
        });
        return button;
    }

    static void BindButton(SearchColumnEventArgs args, VisualElement ve)
    {
        var button = (Button)ve;
        button.userData = args.item;

        // Rows get recycled while scrolling, so the playing state follows the item, not the button.
        bool playing = args.item != null && args.item.id == s_PlayingItemId;
        if (playing)                     s_PlayingButton = button;
        else if (s_PlayingButton == button) s_PlayingButton = null;
        button.text = playing ? "■" : "▶";
    }

    static void OnClicked(Button button)
    {
        var item = button.userData as SearchItem;
        if (item == null) return;

        if (item.id == s_PlayingItemId)
        {
            Stop();
            return;
        }

        var clip = item.ToObject<AudioClip>();
        if (clip == null) return;

        Stop();
        PlayClip(clip);
        s_PlayingItemId = item.id;
        s_PlayingButton = button;
        button.text     = "■";

        // Flip back to ▶ when the clip finishes on its own.
        button.schedule.Execute(() =>
        {
            if (s_PlayingItemId == item.id && !IsClipPlaying())
                Stop();
        }).Every(200).Until(() => s_PlayingItemId != item.id);
    }

    static void Stop()
    {
        StopAllClips();
        if (s_PlayingButton != null) s_PlayingButton.text = "▶";
        s_PlayingButton = null;
        s_PlayingItemId = null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AUDIO PREVIEW — same editor-only player the Live Tuner uses
    // ─────────────────────────────────────────────────────────────────────────

    static readonly System.Type AudioUtilType =
        typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");

    static void PlayClip(AudioClip clip)
    {
        if (AudioUtilType == null) return;
        MethodInfo method =
            AudioUtilType.GetMethod("PlayPreviewClip", BindingFlags.Static | BindingFlags.Public) ??
            AudioUtilType.GetMethod("PlayClip",        BindingFlags.Static | BindingFlags.Public);

        if (method == null) return;
        var parms = method.GetParameters();
        if (parms.Length >= 3)
            method.Invoke(null, new object[] { clip, 0, false });
        else if (parms.Length == 1)
            method.Invoke(null, new object[] { clip });
    }

    static bool IsClipPlaying()
    {
        if (AudioUtilType == null) return false;
        MethodInfo method =
            AudioUtilType.GetMethod("IsPreviewClipPlaying", BindingFlags.Static | BindingFlags.Public);
        return method != null && (bool)method.Invoke(null, null);
    }

    static void StopAllClips()
    {
        if (AudioUtilType == null) return;
        MethodInfo method =
            AudioUtilType.GetMethod("StopAllPreviewClips", BindingFlags.Static | BindingFlags.Public) ??
            AudioUtilType.GetMethod("StopAllClips",        BindingFlags.Static | BindingFlags.Public);
        method?.Invoke(null, null);
    }
}
