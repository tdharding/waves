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

using System.Linq;
using UnityEngine;

namespace PluginMaster
{
    public partial class PlayModeSave : UnityEditor.EditorWindow
    {
        [UnityEditor.InitializeOnLoad]
        private static class ApplicationEventHandler
        {
            private static GameObject autoApplyFlag = null;
            private const string AUTO_APPLY_OBJECT_NAME = "PlayModeSave_AutoApply";
            private static Texture2D _icon = Resources.Load<Texture2D>("Save");
            private static string _currentScenePath = null;
            private static bool _loadData = true;
            private static bool _refreshDataBase = true;

            static ApplicationEventHandler()
            {
                UnityEditor.EditorApplication.playModeStateChanged += OnStateChanged;
#if UNITY_6000_4_OR_NEWER
                UnityEditor.EditorApplication.hierarchyWindowItemByEntityIdOnGUI += HierarchyItemCallback;
#else
                UnityEditor.EditorApplication.hierarchyWindowItemOnGUI += HierarchyItemCallback;
#endif
                UnityEditor.EditorApplication.hierarchyChanged += OnHierarchyChanged;
                UnityEditor.EditorApplication.hierarchyChanged += UpdateIconColorOnOnHierarchyChanged;
                UnityEditor.EditorApplication.projectChanged += UpdatePackageDefines;
                LoadPrefs();
                UnityEditor.SceneManagement.EditorSceneManager.sceneOpened += OnSceneOpened;
                UnityEngine.SceneManagement.SceneManager.sceneUnloaded += OnSceneUnloaded;
                UpdateObjKeys();
                UnityEditor.EditorApplication.RepaintHierarchyWindow();
                UpdatePackageDefines();
            }

            private static void UpdatePackageDefines()
            {
                UpdatePackageDefine("com.unity.render-pipelines.universal", "PMS_URP");
                UpdatePackageDefine("com.unity.cinemachine", "PMS_CINEMACHINE");
                UpdatePackageDefine("com.unity.cinemachine", "PMS_CINE_MACHINE_2_6_OR_NEWER", "2.6");
                UpdatePackageDefine("com.unity.cinemachine", "PMS_CINE_MACHINE_2_6_1_OR_NEWER", "2.6.1");
                UpdatePackageDefine("com.unity.cinemachine", "PMS_CINE_MACHINE_2_8_OR_NEWER", "2.8");
                UpdatePackageDefine("com.unity.cinemachine", "PMS_CINE_MACHINE_3_0_OR_NEWER", "3.0");
            }

            private static bool IsPackageInstalled(string packageId, string version = null)
            {
                if (!System.IO.File.Exists("Packages/manifest.json")) return false;
                var manifestJsonString = System.IO.File.ReadAllText("Packages/manifest.json");
                if (manifestJsonString.Contains(packageId))
                {
                    if (string.IsNullOrEmpty(version)) return true;
                    var regexMatches = System.Text.RegularExpressions.Regex.Matches(manifestJsonString,
                        "(?<=" + packageId + "\": \")(.*?)(?=\")");
                    if (regexMatches.Count == 0) return false;

                    int[] GetIntArray(string value)
                    {
                        var stringArray = value.Split('.');
                        if (stringArray.Length == 0) return new int[] { 1, 0 };
                        var intArray = new int[stringArray.Length];
                        for (int i = 0; i < intArray.Length; ++i) intArray[i] = int.Parse(stringArray[i]);
                        return intArray;
                    }

                    bool IsOlderThan(string value, string referenceValue)
                    {
                        var intArray = GetIntArray(referenceValue);
                        var otherIntArray = GetIntArray(value);
                        var minLength = Mathf.Min(intArray.Length, otherIntArray.Length);
                        for (int i = 0; i < minLength; ++i)
                        {
                            if (intArray[i] < otherIntArray[i]) return true;
                            else if (intArray[i] > otherIntArray[i]) return false;
                        }
                        return false;
                    }
                    var packageVersion = regexMatches[0].Value;
                    return !IsOlderThan(version, packageVersion);
                }
                return false;
            }

            private static void UpdatePackageDefine(string packageId, string define, string version = null)
            {
                var buildTargetGroup = UnityEditor.BuildPipeline.GetBuildTargetGroup
                    (UnityEditor.EditorUserBuildSettings.activeBuildTarget);
#if UNITY_2022_2_OR_NEWER
                var namedBuildTarget = UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(buildTargetGroup);
                var definesSCSV = UnityEditor.PlayerSettings.GetScriptingDefineSymbols(namedBuildTarget);
#else
                var definesSCSV = UnityEditor.PlayerSettings.GetScriptingDefineSymbolsForGroup(buildTargetGroup);
#endif
                if (IsPackageInstalled(packageId, version))
                {
                    if (definesSCSV.Contains(define)) return;
                    if (!string.IsNullOrEmpty(definesSCSV)) definesSCSV += ";";
                    definesSCSV += define;
                }
                else
                {
                    if (!definesSCSV.Contains(define)) return;
                    definesSCSV = definesSCSV.Replace(";" + define, string.Empty);
                    definesSCSV = definesSCSV.Replace(define, string.Empty);
                }
#if UNITY_2022_2_OR_NEWER
                UnityEditor.PlayerSettings.SetScriptingDefineSymbols(namedBuildTarget, definesSCSV);
#else
                UnityEditor.PlayerSettings.SetScriptingDefineSymbolsForGroup(buildTargetGroup, definesSCSV);
#endif
            }
            private static void OnHierarchyChanged()
            {
                var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                string activeScenePath = null;
                if (activeScene != null) activeScenePath = activeScene.path;
                if (!_loadData || _currentScenePath == activeScenePath) return;
                if (_currentScenePath != activeScenePath) _currentScenePath = activeScenePath;
                LoadData();
            }
            private static void UpdateIconColorOnOnHierarchyChanged()
            {
                UpdateIconColor();
                UnityEditor.EditorApplication.hierarchyChanged -= UpdateIconColorOnOnHierarchyChanged;
            }
            private static void OnSceneOpened(UnityEngine.SceneManagement.Scene scene,
                UnityEditor.SceneManagement.OpenSceneMode mode)
            {
                if (!_openingScenes)
                {
                    _scenesToOpen.Clear();
                    return;
                }
                _scenesToOpen.Remove(scene.path);

                if (_scenesToOpen.Count > 0) return;
                _openingScenes = false;
                if (_autoApply) ApplyAll();
            }

            private static void OnSceneUnloaded(UnityEngine.SceneManagement.Scene scene)
            {
                if (!Application.isPlaying) return;
                foreach (var data in _objData.Values)
                    if (data.objKey.scenePath == scene.path) data.unloadedScene = true;
            }
            private static void OnStateChanged(UnityEditor.PlayModeStateChange state)
            {
                if (state == UnityEditor.PlayModeStateChange.ExitingEditMode)
                {
                    PMSData.UpdateFull();
                    if (_autoApply)
                    {
                        autoApplyFlag = new GameObject(AUTO_APPLY_OBJECT_NAME);
                        autoApplyFlag.hideFlags = HideFlags.HideAndDontSave;
                    }
                    return;
                }
                if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
                {
                    if (_saveEntireHierarchy) AddAll();
                    foreach (var data in _compData)
                    {
                        data.Key.objKey.UpdateParentKeys();
                        if (data.Value.saveCmd == SaveCommand.SAVE_NOW) continue;
                        if (data.Value.serializedObj == null) continue;
                        if (data.Value.serializedObj.targetObject == null) _componentsToBeDeleted.Add(data.Key);
                        else data.Value.Update(data.Key.compId);
                    }

                    var objDataClone = _objData.ToDictionary(entry => entry.Key, entry => entry.Value);

                    foreach (var key in objDataClone.Keys)
                    {
                        key.UpdateParentKeys();
                        var data = objDataClone[key];
                        if (data.saveCmd == SaveCommand.SAVE_NOW)
                        {
                            continue;
                        }
                        if (data.objIsNull)
                        {
                            if (!data.unloadedScene) ToBeDeleted(key);
                            continue;
                        }

                        if (FullObjectDataContains(key, out FullObjectData foundItem))
                        {
                            var obj = FindObject(key);
                            if (obj == null)
                            {
                                var objKey = foundItem;
                                ToBeDeleted(key);
                                RemoveFullObjectData(key);
                                continue;
                            }
                            var components = obj.GetComponents<Component>();

                            foreach (var comp in components)
                                Add(comp, SaveCommand.SAVE_ON_EXITING_PLAY_MODE, data.always, true);
                            if (!data.always) RemoveFullObjectData(key);
                            if (_includeChildren)
                            {
                                var children = obj.GetComponentsInChildren<Transform>();
                                foreach (var child in children)
                                {
                                    var childObj = child.gameObject;
                                    if (childObj == obj) continue;
                                    var childComps = childObj.GetComponentsInChildren<Component>();
                                    foreach (var childComp in childComps)
                                    {
                                        Add(childComp, SaveCommand.SAVE_ON_EXITING_PLAY_MODE, data.always, true);
                                    }
                                    var objKey = new ObjectDataKey(childObj);
                                    AddFullObjectData(objKey, true);
                                    PMSData.AlwaysSaveFull(objKey);
                                }
                            }
                        }
                    }
                    return;
                }
                if (state == UnityEditor.PlayModeStateChange.EnteredPlayMode)
                {
                    _scenesToOpen.Clear();
                    _componentsToBeDeleted.Clear();
                    UpdateFullObjects();
                    return;
                }
                if (state == UnityEditor.PlayModeStateChange.EnteredEditMode)
                {
                    _showAplyButtons = true;
                    var openedSceneCount = UnityEngine.SceneManagement.SceneManager.sceneCount;
                    var openedScenes = new System.Collections.Generic.List<string>();
                    for (int i = 0; i < openedSceneCount; ++i)
                        openedScenes.Add(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).path);
                    bool applyAll = true;
                    _openingScenes = false;
                    var scenesToOpen = _scenesToOpen.Where(
                        scenePath => !openedScenes.Contains(scenePath)).ToArray();
                    foreach (var scenePath in scenesToOpen)
                    {
                        if (scenePath == "DontDestroyOnLoad" || scenePath == string.Empty) continue;
                        applyAll = false;
                        _openingScenes = true;
                        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath,
                            UnityEditor.SceneManagement.OpenSceneMode.Additive);
                    }
                    autoApplyFlag = GameObject.Find(AUTO_APPLY_OBJECT_NAME);
                    _autoApply = autoApplyFlag != null;
                    if (_autoApply)
                    {
                        DestroyImmediate(autoApplyFlag);
                        if (applyAll) PlayModeSave.ApplyAll();
                    }
                    _loadData = false;
                }
            }

            public static void UpdateIconColor()
            {
                if (_icon == null) _icon = Resources.Load<Texture2D>("Save");
                var iconColor = JsonUtility.FromJson<Color>(UnityEditor.EditorPrefs.GetString(ICON_COLOR_PREF,
                JsonUtility.ToJson(DEFAULT_ICON_COLOR)));
                var pixels = _icon.GetPixels();
                for (int i = 0; i < pixels.Length; ++i) if (pixels[i].a == 1f) pixels[i] = iconColor;
                _icon.SetPixels(pixels);
                _icon.Apply();
            }
#if UNITY_6000_4_OR_NEWER
            private static void HierarchyItemCallback(EntityId instanceID, Rect selectionRect)
#else
            private static void HierarchyItemCallback(int instanceID, Rect selectionRect)
#endif
            {
                if (!showSaveIndicator) return;
                if (_refreshDataBase)
                {
                    UnityEditor.AssetDatabase.Refresh();
                    _refreshDataBase = false;
                }
                var data = _compData;
                var keys = _compData.Keys.Where(k => k.objKey.objId == instanceID).ToArray();
                if (keys.Length == 0) return;
                if (_icon == null) UpdateIconColor();
                var rect = new Rect(selectionRect.xMax - 10, selectionRect.y + 2, 11, 11);
                GUI.Box(rect, _icon, GUIStyle.none);
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0
                    && rect.Contains(Event.current.mousePosition))
                {
#if UNITY_6000_3_OR_NEWER
                    var obj = UnityEditor.EditorUtility.EntityIdToObject(instanceID) as GameObject;
#else
                    var obj = UnityEditor.EditorUtility.InstanceIDToObject(instanceID) as GameObject;
#endif
                    var compNames = obj.name + " components to save: ";
                    foreach (var key in keys)
                    {
                        if (key.objKey.objId != instanceID) continue;
#if UNITY_6000_3_OR_NEWER
                        var comp = UnityEditor.EditorUtility.EntityIdToObject(key.compId) as Component;
#else
                        var comp = UnityEditor.EditorUtility.InstanceIDToObject(key.compId) as Component;
#endif
                        if (comp == null) continue;
                        compNames += comp.GetType().Name + ", ";
                    }
                    compNames = compNames.Remove(compNames.Length - 2);
                    ComponentSaveListWindow.Show(_compData, instanceID);
                    UnityEditor.EditorApplication.RepaintHierarchyWindow();
                }
            }
        }
    }
}
#pragma warning restore UDR0001
