/*
Copyright (c) Omar Duarte
Unauthorized copying of this file, via any medium is strictly prohibited.
Writen by Omar Duarte.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/
#pragma warning disable UDR0001

using UnityEngine;


namespace PluginMaster
{
    public partial class PlayModeSave : UnityEditor.EditorWindow
    {
        [UnityEditor.MenuItem("Tools/Plugin Master/" + TOOL_NAME, false, int.MaxValue)]
        private static void ShowWindow() => GetWindow<PlayModeSave>(TOOL_NAME);

        private const string SAVE_ENTIRE_HIERARCHY_PREF = "PLAY_MODE_SAVE_saveEntireHierarchy";
        private const string AUTO_APPLY_PREF = "PLAY_MODE_SAVE_autoApply";
        private const string INCLUDE_CHILDREN_PREF = "PLAY_MODE_SAVE_includeChildren";
        private const string ICON_COLOR_PREF = "PLAY_MODE_SAVE_iconColor";
        private const string SHOW_SAVE_INDICATOR_PREF = "PLAY_MODE_SAVE_showSaveIndicator";
        private const string SAVE_ACTIVE_STATE_PREF = "PLAY_MODE_SAVE_saveActiveState";
        private static bool _showAplyButtons = true;

        private static readonly Color DEFAULT_ICON_COLOR = new Color(1f, 0.2f, 0.4f, 1f);
        public static Color _iconColor = DEFAULT_ICON_COLOR;
        private static bool _showSaveIndicator = true;
        private static bool _saveActiveState = true;
        public static bool showSaveIndicator
        {
            get => _showSaveIndicator;
            set
            {
                if (value == _showSaveIndicator) return;
                _showSaveIndicator = value;
                UnityEditor.EditorPrefs.SetBool(SHOW_SAVE_INDICATOR_PREF, _showSaveIndicator);
                UnityEditor.EditorApplication.RepaintHierarchyWindow();
            }
        }
        private static void LoadPrefs()
        {
            _saveEntireHierarchy = UnityEditor.EditorPrefs.GetBool(SAVE_ENTIRE_HIERARCHY_PREF, false);
            _autoApply = UnityEditor.EditorPrefs.GetBool(AUTO_APPLY_PREF, true);
            _includeChildren = UnityEditor.EditorPrefs.GetBool(INCLUDE_CHILDREN_PREF, true);
            _iconColor = JsonUtility.FromJson<Color>(UnityEditor.EditorPrefs.GetString(ICON_COLOR_PREF,
                JsonUtility.ToJson(DEFAULT_ICON_COLOR)));
            _showSaveIndicator = UnityEditor.EditorPrefs.GetBool(SHOW_SAVE_INDICATOR_PREF, true);
            _saveActiveState = UnityEditor.EditorPrefs.GetBool(SAVE_ACTIVE_STATE_PREF, true);
        }

        private void OnEnable() => LoadPrefs();

        private void OnDisable()
        {
            UnityEditor.EditorPrefs.SetBool(SAVE_ENTIRE_HIERARCHY_PREF, _saveEntireHierarchy);
            UnityEditor.EditorPrefs.SetBool(AUTO_APPLY_PREF, _autoApply);
            UnityEditor.EditorPrefs.SetBool(INCLUDE_CHILDREN_PREF, _includeChildren);
            UnityEditor.EditorPrefs.SetString(ICON_COLOR_PREF, JsonUtility.ToJson(_iconColor));
            UnityEditor.EditorPrefs.SetBool(SAVE_ACTIVE_STATE_PREF, _saveActiveState);
        }

        private static bool ValidateHierarchyMenu(bool validateIsPlaying)
        {
            var selection = GetSelection();
            if (selection == null || selection.Length == 0) return false;
            if (!validateIsPlaying) return true;
            return Application.isPlaying;
        }
        [UnityEditor.MenuItem("GameObject/- Play Mode Save/Save all selected objects now", true, 10000)]
        private static bool ValidateSaveAllComponentsNowMenu(UnityEditor.MenuCommand command)
            => ValidateHierarchyMenu(validateIsPlaying: true);

        [UnityEditor.MenuItem("GameObject/- Play Mode Save/Save all selected objects now", false, 10000)]
        private static void SaveAllComponentsNowMenu(UnityEditor.MenuCommand command)
            => SaveSelection(SaveCommand.SAVE_NOW, false);
        [UnityEditor.MenuItem("GameObject/- Play Mode Save/Save all selected objects when exiting play mode", true, 10001)]
        private static bool ValidateSaveAllComponentsWhenExitPlayModeMenu(UnityEditor.MenuCommand command)
            => ValidateHierarchyMenu(validateIsPlaying: true);

        [UnityEditor.MenuItem("GameObject/- Play Mode Save/Save all selected objects when exiting play mode", false, 10001)]
        private static void SaveAllComponentsWhenExitPlayModeMenu(UnityEditor.MenuCommand command)
            => SaveSelection(SaveCommand.SAVE_ON_EXITING_PLAY_MODE, false);

        [UnityEditor.MenuItem("GameObject/- Play Mode Save/Always save all selected objects when exiting play mode",
            true, 10002)]
        private static bool ValidateAlwaysSaveAllComponentsWhenExitPlayModeMenu(UnityEditor.MenuCommand command)
            => ValidateHierarchyMenu(validateIsPlaying: false);

        [UnityEditor.MenuItem("GameObject/- Play Mode Save/Always save all selected objects when exiting play mode",
            false, 10002)]
        private static void AlwaysSaveAllComponentsWhenExitPlayModeMenu(UnityEditor.MenuCommand command)
            => SaveSelection(SaveCommand.SAVE_ON_EXITING_PLAY_MODE, true);

        [UnityEditor.MenuItem("GameObject/- Play Mode Save/Remove all selected objects from save list",
            true, 10003)]
        private static bool ValidateRemoveAllComponentsFromSaveListMenu(UnityEditor.MenuCommand command)
            => ValidateHierarchyMenu(validateIsPlaying: false);

        [UnityEditor.MenuItem("GameObject/- Play Mode Save/Remove all selected objects from save list",
            false, 10003)]
        private static void RemoveAllComponentsFromSaveListMenu(UnityEditor.MenuCommand command)
            => RemoveFromSaveList(includeChildren: true);

        private static GameObject[] GetSelection()
            => UnityEditor.Selection.GetFiltered<GameObject>(UnityEditor.SelectionMode.Editable
                | UnityEditor.SelectionMode.ExcludePrefab | UnityEditor.SelectionMode.TopLevel);

        private static void SaveSelection(SaveCommand cmd, bool always)
        {
            var selection = GetSelection();
            if (selection.Length == 0) return;

            System.Collections.Generic.List<Component> allComponentsToProcess
                = new System.Collections.Generic.List<Component>(1000);

            foreach (var obj in selection)
            {
                var components = _includeChildren ? obj.GetComponentsInChildren<Component>()
                    : obj.GetComponents<Component>();

                foreach (var comp in components)
                {
                    if (comp != null && !IsCombinedOrInstance(comp))
                        allComponentsToProcess.Add(comp);
                }

                if (cmd == SaveCommand.SAVE_ON_EXITING_PLAY_MODE)
                {
                    var objKey = new ObjectDataKey(obj);
                    AddFullObjectData(objKey, always);
                    if (always) PMSData.AlwaysSaveFull(objKey);
                }
            }
            ProcessComponentBatch(allComponentsToProcess, cmd, always);
        }

        private static void ProcessComponentBatch(System.Collections.Generic.List<Component> components, SaveCommand cmd, bool always)
        {
            var scenePaths = new System.Collections.Generic.HashSet<string>();
            foreach (var comp in components)
            {
                if (comp == null) continue;
                var scenePath = comp.gameObject.scene.path;
                if (!string.IsNullOrEmpty(scenePath) && scenePath != "DontDestroyOnLoad")
                    scenePaths.Add(scenePath);
            }

            foreach (var scenePath in scenePaths)
            {
                if (!_scenesToOpen.Contains(scenePath))
                    _scenesToOpen.Add(scenePath);
            }

            foreach (var comp in components)
            {
                Add(comp, cmd, always, true);
            }
        }

        private static void RemoveFromSaveList(bool includeChildren)
        {
            var selection = UnityEditor.Selection.GetFiltered<GameObject>(UnityEditor.SelectionMode.Editable
               | UnityEditor.SelectionMode.ExcludePrefab | UnityEditor.SelectionMode.TopLevel);
            if (selection.Length == 0) return;
            foreach (var obj in selection)
            {
                var components = includeChildren ? obj.GetComponentsInChildren<Component>()
               : obj.GetComponents<Component>();
                foreach (var comp in components)
                {
                    var key = new ComponentSaveDataKey(comp);
                    PMSData.Remove(key);
                    CompDataRemoveKey(key);
                }
                var objKey = new ObjectDataKey(obj);
                RemoveFullObjectData(objKey);
                PMSData.RemoveFull(objKey);
                if (includeChildren)
                {
                    for (int i = 0; i < obj.transform.childCount; ++i)
                    {
                        var child = obj.transform.GetChild(i);
                        objKey = new ObjectDataKey(child.gameObject);
                        RemoveFullObjectData(objKey);
                        PMSData.RemoveFull(objKey);
                    }
                }
            }
        }

        private void OnGUI()
        {
            if (!Application.isPlaying)
            {
                using (new UnityEditor.EditorGUILayout.VerticalScope(UnityEditor.EditorStyles.helpBox))
                {
                    using (var check = new UnityEditor.EditorGUI.ChangeCheckScope())
                    {
                        var label = "Auto-Apply All Changes When Exiting Play Mode";
                        _autoApply = UnityEditor.EditorGUILayout.ToggleLeft(label, _autoApply);
                        if (check.changed) UnityEditor.EditorPrefs.SetBool(AUTO_APPLY_PREF, _autoApply);
                    }
                    if (!_autoApply)
                    {
                        if (_compData.Count == 0 && !_showAplyButtons)
                            UnityEditor.EditorGUILayout.LabelField("Nothing to apply");
                        else if (_compData.Count > 0 && _showAplyButtons)
                        {
                            using (new UnityEditor.EditorGUILayout.HorizontalScope())
                            {
                                if (GUILayout.Button("Apply All Changes"))
                                {
                                    ApplyAll();
                                    _showAplyButtons = false;
                                }
                                GUILayout.FlexibleSpace();
                            }
                        }
                    }
                }
            }
            var selection = UnityEditor.Selection.GetFiltered<GameObject>(UnityEditor.SelectionMode.Editable
                | UnityEditor.SelectionMode.ExcludePrefab | UnityEditor.SelectionMode.TopLevel);
            if (selection.Length > 0)
            {
                using (new UnityEditor.EditorGUILayout.VerticalScope(UnityEditor.EditorStyles.helpBox))
                {
                    using (new GUILayout.HorizontalScope())
                    {
                        GUILayout.Label("Execute for all selected objects: ");
                        GUILayout.FlexibleSpace();
                        _includeChildren = UnityEditor.EditorGUILayout.ToggleLeft("Include children",
                            _includeChildren, GUILayout.Width(109));
                    }
                    _saveActiveState = UnityEditor.EditorGUILayout.ToggleLeft("Save active state", _saveActiveState);
                    if (Application.isPlaying)
                    {
                        if (GUILayout.Button("Save all components now"))
                            SaveSelection(SaveCommand.SAVE_NOW, false);
                        if (GUILayout.Button("Save all components when exiting play mode"))
                            SaveSelection(SaveCommand.SAVE_ON_EXITING_PLAY_MODE, false);
                    }
                    if (GUILayout.Button("Always save all components when exiting play mode"))
                        SaveSelection(SaveCommand.SAVE_ON_EXITING_PLAY_MODE, true);
                    if (GUILayout.Button("Remove all components from save list"))
                    {
                        foreach (var obj in selection)
                        {
                            var components = _includeChildren ? obj.GetComponentsInChildren<Component>()
                           : obj.GetComponents<Component>();
                            foreach (var comp in components)
                            {
                                var key = new ComponentSaveDataKey(comp);
                                PMSData.Remove(key);
                                CompDataRemoveKey(key);
                            }
                            var objKey = new ObjectDataKey(obj);
                            RemoveFullObjectData(objKey);
                            PMSData.RemoveFull(objKey);
                            if (_includeChildren)
                            {
                                for (int i = 0; i < obj.transform.childCount; ++i)
                                {
                                    var child = obj.transform.GetChild(i);
                                    objKey = new ObjectDataKey(child.gameObject);
                                    RemoveFullObjectData(objKey);
                                    PMSData.RemoveFull(objKey);
                                }
                            }
                        }
                    }
                }
            }
            using (new UnityEditor.EditorGUILayout.VerticalScope(UnityEditor.EditorStyles.helpBox))
            {
                using (var check = new UnityEditor.EditorGUI.ChangeCheckScope())
                {
                    _saveEntireHierarchy = UnityEditor.EditorGUILayout.ToggleLeft
                        ("Save The Entire Hierarchy When Exiting Play Mode", _saveEntireHierarchy);
                    if (check.changed) UnityEditor.EditorPrefs.SetBool(SAVE_ENTIRE_HIERARCHY_PREF, _saveEntireHierarchy);
                    if (_saveEntireHierarchy)
                    {
                        UnityEditor.EditorGUILayout.HelpBox("NOT RECOMMENDED. Enabling this option in large scenes " +
                            "can cause a long delay when saving and applying changes.", UnityEditor.MessageType.Warning);
                    }
                }
            }
            using (new UnityEditor.EditorGUILayout.VerticalScope(UnityEditor.EditorStyles.helpBox))
            {
                showSaveIndicator = UnityEditor.EditorGUILayout.ToggleLeft("Show Save Indicator", showSaveIndicator);
                if (showSaveIndicator)
                {
                    using (var check = new UnityEditor.EditorGUI.ChangeCheckScope())
                    {
                        var color = UnityEditor.EditorGUILayout.ColorField(new GUIContent("Icon Color:"),
                            _iconColor, true, false, false);
                        if (check.changed)
                        {
                            _iconColor = color;
                            UnityEditor.EditorPrefs.SetString(ICON_COLOR_PREF, JsonUtility.ToJson(_iconColor));
                            ApplicationEventHandler.UpdateIconColor();
                        }
                    }
                }
            }
        }
    }
}
#pragma warning restore UDR0001
