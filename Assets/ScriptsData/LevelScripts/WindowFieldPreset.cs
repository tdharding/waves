using UnityEngine;

// One entry in the window-field pool: a baked snake-window sheet plus the two numbers the
// shader needs to read it — the world size of a cell and the sheet's grid dimensions.
// Buildings pick a preset at random from a Resources/WindowFields folder and apply it to
// their own renderer via a MaterialPropertyBlock, so different buildings can carry different
// window sheets (and cell sizes) off one shared material. Authored by the Window Field
// Generator (Bake as Preset).
[CreateAssetMenu(fileName = "WindowFieldPreset", menuName = "Waves/Window Field Preset")]
public class WindowFieldPreset : ScriptableObject
{
    [Tooltip("The baked field texture — R = mask, G = per-window id. Point/Repeat/uncompressed.")]
    public Texture2D fieldTexture;

    [Tooltip("World size of one field cell (feeds _WindowCellSize per building).")]
    public float cellSize = 0.37f;

    [Tooltip("Field size in cells (cols, rows) — feeds _WindowAtlasGrid per building.")]
    public Vector2 gridDims = new Vector2(32, 48);
}
