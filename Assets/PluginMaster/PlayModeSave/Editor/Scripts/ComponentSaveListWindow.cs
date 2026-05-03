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

using UnityEngine;
using System.Linq;

namespace PluginMaster
{
    public class ComponentSaveListWindow : UnityEditor.EditorWindow
    {
        private GameObject _targetObject;
        private (Component comp, PlayModeSave.SaveDataValue data)[] _components;
        private System.Collections.Generic.
            Dictionary<PlayModeSave.ComponentSaveDataKey, PlayModeSave.SaveDataValue> _compData;
        private static ComponentSaveListWindow _instance = null;

        private Texture _cancelIcon;
#if UNITY_6000_3_OR_NEWER
        public static void Show(System.Collections.Generic.
           Dictionary<PlayModeSave.ComponentSaveDataKey, PlayModeSave.SaveDataValue> compData, EntityId objId)
#else
        public static void Show(System.Collections.Generic.
            Dictionary<PlayModeSave.ComponentSaveDataKey, PlayModeSave.SaveDataValue> compData, int objId)
#endif
        {
            _instance = GetWindow<ComponentSaveListWindow>(utility: true, "Component Save List");
            _instance.UpdateComponentList(compData, objId);
            _instance.Show();
        }
#if UNITY_6000_3_OR_NEWER
        public static void Update(System.Collections.Generic.
           Dictionary<PlayModeSave.ComponentSaveDataKey, PlayModeSave.SaveDataValue> compData, EntityId objId)
#else
        public static void Update(System.Collections.Generic.
           Dictionary<PlayModeSave.ComponentSaveDataKey, PlayModeSave.SaveDataValue> compData, int objId)
#endif
        {
            if (_instance != null) _instance.UpdateComponentList(compData, objId);
        }
#if UNITY_6000_3_OR_NEWER
        private void UpdateComponentList(System.Collections.Generic.
            Dictionary<PlayModeSave.ComponentSaveDataKey, PlayModeSave.SaveDataValue> compData, EntityId objId)
#else
        private void UpdateComponentList(System.Collections.Generic.
            Dictionary<PlayModeSave.ComponentSaveDataKey, PlayModeSave.SaveDataValue> compData, int objId)
#endif
        {
            _compData = compData
                .Where(pair => pair.Key.objKey.objId == objId)
                .ToDictionary(pair => pair.Key, pair => pair.Value);

            PlayModeSave.ObjectDataKey objKey = null;
            if (_compData.Count > 0)
            {
                objKey = _compData.First().Key.objKey;
                _targetObject = PlayModeSave.FindObject(objKey, findInHierarchy: false);
            }
            else
            {
#if UNITY_6000_3_OR_NEWER
                _targetObject = UnityEditor.EditorUtility.EntityIdToObject(objId) as GameObject;
#else
                _targetObject = UnityEditor.EditorUtility.InstanceIDToObject(objId) as GameObject;
#endif
                objKey = new PlayModeSave.ObjectDataKey(_targetObject);
            }
            if (objKey == null) return;
            _targetObject = objKey != null
                ? PlayModeSave.FindObject(objKey, findInHierarchy: false)
                : null;
            if (_targetObject == null) return;

            _components = _targetObject != null
                ? _targetObject.GetComponents<Component>()
                    .Select(comp =>
                    {
#if UNITY_6000_3_OR_NEWER
                        var key = _compData.Keys.FirstOrDefault(k => k.compId == comp.GetEntityId());
#else
                        var key = _compData.Keys.FirstOrDefault(k => k.compId == comp.GetInstanceID());
#endif
                        PlayModeSave.SaveDataValue saveDataValue = null;
                        if (key != null) _compData.TryGetValue(key, out saveDataValue);
                        return (comp, saveDataValue);
                    })
                    .ToArray()
                : System.Array.Empty<(Component, PlayModeSave.SaveDataValue)>();
        }

        private void OnEnable()
        {
            _cancelIcon = Resources.Load<Texture>("Cancel");
        }

        private void OnGUI()
        {
            if (_targetObject == null || _components == null) return;

            UnityEditor.EditorGUILayout.LabelField($"Components of '{_targetObject.name}'",
                UnityEditor.EditorStyles.boldLabel);

            for (int i = 0; i < _components.Length; ++i)
            {
                var comp = _components[i];
                bool isSaved = comp.data != null && comp.data.saveCmd == PlayModeSave.SaveCommand.SAVE_NOW;
                bool isSavedOnExit = comp.data != null
                    && comp.data.saveCmd == PlayModeSave.SaveCommand.SAVE_ON_EXITING_PLAY_MODE;

                string statusText = "Not saved";
                var prevBgColor = GUI.backgroundColor;
                var prevContentColor = GUI.contentColor;
                var backgroundColor = GUI.backgroundColor;
                var contentColor = GUI.contentColor;
                if (isSaved || (isSavedOnExit && !Application.isPlaying))
                {
                    contentColor = backgroundColor = new Color(0.2f, 1f, 0.2f);
                    statusText = "Saved";
                }
                else if (isSavedOnExit)
                {
                    contentColor = backgroundColor = new Color(1f, 1f, 0.2f);
                    statusText = "To be saved when exit play mode";
                }

                using (new GUILayout.HorizontalScope("box"))
                {
                    UnityEditor.EditorGUILayout.LabelField(comp.comp.GetType().Name, GUILayout.Width(180));
                    GUI.contentColor = contentColor;
                    GUI.backgroundColor = backgroundColor;
                    UnityEditor.EditorGUILayout.LabelField(statusText, GUILayout.Width(220));
                    GUI.backgroundColor = prevBgColor;
                    GUI.contentColor = prevContentColor;
                    using (new UnityEditor.EditorGUI.DisabledGroupScope(!(isSaved || isSavedOnExit)))
                    {
                        GUIContent cancelContent = _cancelIcon != null
                            ? new GUIContent(_cancelIcon, "Cancel")
                            : new GUIContent("Cancel");
                        if (GUILayout.Button(cancelContent, GUILayout.Width(32), GUILayout.Height(18)))
                        {
                            PlayModeSave.Remove(comp.comp);
#if UNITY_6000_3_OR_NEWER
                            UpdateComponentList(PlayModeSave.compData, comp.comp.gameObject.GetEntityId());
#else
                            UpdateComponentList(PlayModeSave.compData, comp.comp.gameObject.GetInstanceID());
#endif
                            Repaint();
                        }
                    }
                    if (!Application.isPlaying)
                    {
                        using (new UnityEditor.EditorGUI.DisabledGroupScope(!(isSaved || isSavedOnExit)))
                        {
                            if (GUILayout.Button("Apply Changes ...", GUILayout.Width(140)))
                            {
                                GranularApplyWindow.Show(comp.comp);
                            }
                        }

                    }
                }
            }
        }
    }
}