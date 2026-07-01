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
        #region Fields & Settings
        private static bool _autoApply = true;
        private static bool _saveEntireHierarchy = false;
        private static bool _includeChildren = true;
        private static System.Collections.Generic.List<string> _scenesToOpen = new System.Collections.Generic.List<string>();
        private static System.Collections.Generic.List<ComponentSaveDataKey> _componentsToBeDeleted
            = new System.Collections.Generic.List<ComponentSaveDataKey>();
        private static System.Collections.Generic.Dictionary<ObjectDataKey, SaveObject> _objData
            = new System.Collections.Generic.Dictionary<ObjectDataKey, SaveObject>();
        private static System.Collections.Generic.Dictionary<ComponentSaveDataKey, SaveDataValue> _compData
            = new System.Collections.Generic.Dictionary<ComponentSaveDataKey, SaveDataValue>();
        public static System.Collections.Generic.Dictionary<ComponentSaveDataKey, SaveDataValue> compData => _compData;
        private static ComponentSaveDataKey GetKey(Object comp)
            => new ComponentSaveDataKey(comp as Component);
        private static System.Collections.Generic.List<FullObjectData> _fullObjData
            = new System.Collections.Generic.List<FullObjectData>();
        private static System.Collections.Generic.List<ObjectDataKey> _objectsToBeDeleted
            = new System.Collections.Generic.List<ObjectDataKey>();
        private static bool _openingScenes = false;
        public enum SaveCommand { SAVE_NOW, SAVE_ON_EXITING_PLAY_MODE }
        #endregion

        #region Save Operations
        public static void Add(Component component, SaveCommand cmd, bool always, bool serialize)
        {
            if (IsCombinedOrInstance(component)) return;
            var scenePath = component.gameObject.scene.path;
            if (!_scenesToOpen.Contains(scenePath) && scenePath != "DontDestroyOnLoad") _scenesToOpen.Add(scenePath);
            var key = new ComponentSaveDataKey(component);
            if (always) PMSData.AlwaysSave(key);
            if (_compData.TryGetValue(key, out var existingSaveData))
            {
                if (serialize)
                {
                    var data = new UnityEditor.SerializedObject(component);
                    CopyPropertiesFromComponent(component, data, ref existingSaveData.objectReferences);
                    existingSaveData.serializedObj = data;
                }
                existingSaveData.saveCmd = cmd;
                _compData[key] = existingSaveData;
            }
            else
            {
                var data = serialize ? new UnityEditor.SerializedObject(component) : null;
                var saveData = serialize ? GetSaveDataValue(component, cmd, data) : new SaveDataValue(null, cmd, component);
                if (serialize) CopyPropertiesFromComponent(component, data, ref saveData.objectReferences);
                _compData.Add(key, saveData);
            }
            var saveObj = new SaveObject(component.transform, cmd, always, _includeChildren);
            saveObj.Update();
            var objKey = saveObj.objKey;

            if (_objData.ContainsKey(objKey)) _objData[objKey] = saveObj;
            else _objData.Add(objKey, saveObj);

            UnityEditor.EditorApplication.RepaintHierarchyWindow();
        }

        public static void Remove(Component comp)
        {
            if (comp == null) return;
            var compKey = new ComponentSaveDataKey(comp);
            if (!_compData.ContainsKey(compKey)) return;
            _compData.Remove(compKey);
            var objKey = compKey.objKey;
            if (!_objData.ContainsKey(objKey)) return;
            int compCount = _compData.Keys.Count(k => k.objKey == objKey);
            if (compCount != 0) return;
            _objData.Remove(objKey);
        }

        public static SaveDataValue GetSaveDataValue(Component component, SaveCommand cmd, UnityEditor.SerializedObject data)
        {
            if (component is SpriteRenderer)
            {
                var renderer = component as SpriteRenderer;
                return new SpriteRendererSaveDataValue(data, cmd, component, renderer.sortingOrder,
                    renderer.sortingLayerID);
            }
            else if (component is UnityEngine.U2D.SpriteShapeRenderer)
            {
                var renderer = component as UnityEngine.U2D.SpriteShapeRenderer;
                return new SpriteShapeRendererSaveDataValue(data, cmd, component, renderer.sortingOrder,
                    renderer.sortingLayerID);
            }
            else if (component is ParticleSystem)
            {
                var particleSystem = component as ParticleSystem;
                var renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
                return new ParticleSystemSaveDataValue(data, cmd, component, renderer.sortingOrder,
                    renderer.sortingLayerID);
            }
            else if (component is Cloth)
            {
                var cloth = component as Cloth;
                return new ClothSaveDataValue(data, cmd, component, cloth.coefficients.ToArray());
            }
            else if (component is UnityEngine.Rendering.SortingGroup)
            {
                var sortingGroup = component as UnityEngine.Rendering.SortingGroup;
                return new SortingGroupSaveDataValue(data, cmd, component,
                    sortingGroup.sortingOrder, sortingGroup.sortingLayerID);
            }
            else if (component is UnityEngine.Tilemaps.Tilemap)
            {
                var tilemap = component as UnityEngine.Tilemaps.Tilemap;
                return new TilemapSaveDataValue(data, cmd, tilemap);
            }
#if PMS_CINEMACHINE && !PMS_CINE_MACHINE_3_0_OR_NEWER
            else if (component is Cinemachine.CinemachineVirtualCamera)
            {
                var CMVCam = component as Cinemachine.CinemachineVirtualCamera;
                var CMCompDataList = new System.Collections.Generic.List<ICinamechineComponentBaseData>();
#if PMS_CINE_MACHINE_2_6_OR_NEWER
                var CM3PF = CMVCam.GetCinemachineComponent<Cinemachine.Cinemachine3rdPersonFollow>();
                if (CM3PF != null) CMCompDataList.Add(new Cinemachine3rdPersonFollowData(CM3PF));
#endif
                var CMFT = CMVCam.GetCinemachineComponent<Cinemachine.CinemachineFramingTransposer>();
                if (CMFT != null) CMCompDataList.Add(new CinemachineFramingTransposerData(CMFT));

                var CMHLT = CMVCam.GetCinemachineComponent<Cinemachine.CinemachineHardLockToTarget>();
                if (CMHLT != null) CMCompDataList.Add(new CinemachineHardLockToTargetData(CMHLT));

                var CMOT = CMVCam.GetCinemachineComponent<Cinemachine.CinemachineOrbitalTransposer>();
                if (CMOT != null) CMCompDataList.Add(new CinemachineOrbitalTransposerData(CMOT));

                var CMTD = CMVCam.GetCinemachineComponent<Cinemachine.CinemachineTrackedDolly>();
                if (CMTD != null) CMCompDataList.Add(new CinemachineTrackedDollyData(CMTD));

                var CMT = CMVCam.GetCinemachineComponent<Cinemachine.CinemachineTransposer>();
                if (CMT != null) CMCompDataList.Add(new CinemachineTransposerData(CMT));

                var CMGC = CMVCam.GetCinemachineComponent<Cinemachine.CinemachineGroupComposer>();
                if (CMGC != null) CMCompDataList.Add(new CinemachineGroupComposerData(CMGC));

                var CMC = CMVCam.GetCinemachineComponent<Cinemachine.CinemachineComposer>();
                if (CMC != null) CMCompDataList.Add(new CinemachineComposerData(CMC));

                var CMPOV = CMVCam.GetCinemachineComponent<Cinemachine.CinemachinePOV>();
                if (CMPOV != null) CMCompDataList.Add(new CinemachinePOVData(CMPOV));

                var CMSAFT = CMVCam.GetCinemachineComponent<Cinemachine.CinemachineSameAsFollowTarget>();
                if (CMSAFT != null) CMCompDataList.Add(new CinemachineSameAsFollowTargetData(CMSAFT));

                var CMBMCP = CMVCam.GetCinemachineComponent<Cinemachine.CinemachineBasicMultiChannelPerlin>();
                if (CMBMCP != null) CMCompDataList.Add(new CinemachineBasicMultiChannelPerlinData(CMBMCP));

                if (CMCompDataList.Count > 0)
                    return new CinemachineVirtualCameraSaveDataValue(data, cmd, CMVCam, CMCompDataList.ToArray());
                return new SaveDataValue(data, cmd, component);
            }
#endif
            return new SaveDataValue(data, cmd, component);
        }

        public static bool IsCombinedOrInstance(Component comp)
        {
            if (comp is MeshFilter)
            {
                var meshFilter = comp as MeshFilter;
                if (meshFilter.sharedMesh != null)
                    if (meshFilter.sharedMesh.name.Contains("Combined Mesh (root: scene)")
                        || meshFilter.sharedMesh.name.ToLower().Contains("instance")) return true;
            }
            else if (comp is Renderer)
            {
                var renderer = comp as Renderer;
                var materials = renderer.sharedMaterials;
                if (materials != null)
                    foreach (var mat in materials)
                        if (mat.name.ToLower().Contains("instance")) return true;
            }
            return false;
        }

        public static void AddAll()
        {

#if UNITY_6000_4_OR_NEWER
            var components = Object.FindObjectsByType<Component>();
#elif UNITY_2022_2_OR_NEWER
            var components = Object.FindObjectsByType<Component>(FindObjectsSortMode.None);
#else
            var components = Object.FindObjectsOfType<Component>();
#endif
            foreach (var comp in components) Add(comp, SaveCommand.SAVE_ON_EXITING_PLAY_MODE, false, true);
        }
        #endregion

        #region Apply Operations
        public static void Apply(Object comp, string[] propertyPaths = null)
        {
            var key = GetKey(comp);
            Apply(key, propertyPaths);
        }

        private static void Apply(ComponentSaveDataKey key, string[] propertyPaths = null)
        {
            if (!CompDataContainsKey(ref key)) return;
            GameObject obj;
            var comp = FindComponent(key, out obj);
            if (obj != null && key.objKey.ParentHasChanged(out Transform savedParent, out int savedSiblingIndex))
            {
                obj.transform.parent = savedParent;
                obj.transform.SetSiblingIndex(savedSiblingIndex);
            }
            if (WillBeDeleted(key.objKey, out ObjectDataKey delFoundKey)) return;
            if (comp == null && obj != null && !_componentsToBeDeleted.Contains(key))
            {
                comp = obj.AddComponent(_compData[key].componentType);
                var objKey = key.objKey;
                if (PMSData.ContainsFull(objKey, out ObjectDataKey objFoundKey)) PMSData.AlwaysSave(key);
            }
            if (comp == null) return;
            var value = _compData[key];
            var data = value.serializedObj;
            if (data == null) return;
            if (obj != null && _saveActiveState)
            {
                if (_objData.TryGetValue(key.objKey, out var saveObj))
                    obj.SetActive(saveObj.isActive);
            }
            _objData.Remove(key.objKey);
            CopyPropertiesFromData(comp, data, value.objectReferences, propertyPaths);
            if (value is SpriteRendererSaveDataValue)
            {
                var rendererData = value as SpriteRendererSaveDataValue;
                var renderer = comp as SpriteRenderer;
                renderer.sortingOrder = rendererData.sortingOrder;
                renderer.sortingLayerID = rendererData.sortingLayerID;
            }
            else if (value is SpriteShapeRendererSaveDataValue)
            {
                var rendererData = value as SpriteShapeRendererSaveDataValue;
                var renderer = comp as UnityEngine.U2D.SpriteShapeRenderer;
                renderer.sortingOrder = rendererData.sortingOrder;
                renderer.sortingLayerID = rendererData.sortingLayerID;
            }
            else if (value is ParticleSystemSaveDataValue)
            {
                var rendererData = value as ParticleSystemSaveDataValue;
                var particleSystem = comp as ParticleSystem;
                var renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
                renderer.sortingOrder = rendererData.sortingOrder;
                renderer.sortingLayerID = rendererData.sortingLayerID;
            }
            else if (value is ClothSaveDataValue)
            {
                var clothData = value as ClothSaveDataValue;
                var cloth = comp as Cloth;
                cloth.coefficients = clothData.coefficients;
            }
            else if (value is SortingGroupSaveDataValue)
            {
                var sortingGroupData = value as SortingGroupSaveDataValue;
                var sortingGroup = comp as UnityEngine.Rendering.SortingGroup;
                sortingGroup.sortingOrder = sortingGroupData.sortingOrder;
                sortingGroup.sortingLayerID = sortingGroupData.sortingLayerID;
            }
            else if (value is TilemapSaveDataValue)
            {
                var tilemapData = value as TilemapSaveDataValue;
                var tilemap = comp as UnityEngine.Tilemaps.Tilemap;
                tilemap.SetTilesBlock(tilemapData.tileBounds, tilemapData.tileArray);
            }
#if PMS_CINEMACHINE && !PMS_CINE_MACHINE_3_0_OR_NEWER
            else if (value is CinemachineVirtualCameraSaveDataValue)
            {
                var CMVCData = value as CinemachineVirtualCameraSaveDataValue;
                var CMVCam = comp as Cinemachine.CinemachineVirtualCamera;
#if PMS_CINE_MACHINE_2_6_OR_NEWER
                var CM3PF = CMVCam.GetCinemachineComponent<Cinemachine.Cinemachine3rdPersonFollow>();
                if (CM3PF != null)
                    if (CMVCData.GetCompData(out Cinemachine3rdPersonFollowData compData))
                        compData.SetCompValues(CM3PF);
#endif
                var CMFT = CMVCam.GetCinemachineComponent<Cinemachine.CinemachineFramingTransposer>();
                if (CMFT != null)
                    if (CMVCData.GetCompData(out CinemachineFramingTransposerData compData))
                        compData.SetCompValues(CMFT);

                var CMHLT = CMVCam.GetCinemachineComponent<Cinemachine.CinemachineHardLockToTarget>();
                if (CMHLT != null)
                    if (CMVCData.GetCompData(out CinemachineHardLockToTargetData compData))
                        compData.SetCompValues(CMHLT);

                var CMOT = CMVCam.GetCinemachineComponent<Cinemachine.CinemachineOrbitalTransposer>();
                if (CMOT != null)
                    if (CMVCData.GetCompData(out CinemachineOrbitalTransposerData compData))
                        compData.SetCompValues(CMOT);

                var CMTD = CMVCam.GetCinemachineComponent<Cinemachine.CinemachineTrackedDolly>();
                if (CMTD != null)
                    if (CMVCData.GetCompData(out CinemachineTrackedDollyData compData))
                        compData.SetCompValues(CMTD);

                var CMT = CMVCam.GetCinemachineComponent<Cinemachine.CinemachineTransposer>();
                if (CMT != null)
                    if (CMVCData.GetCompData(out CinemachineTransposerData compData))
                        compData.SetCompValues(CMT);

                var CMGC = CMVCam.GetCinemachineComponent<Cinemachine.CinemachineGroupComposer>();
                if (CMGC != null)
                    if (CMVCData.GetCompData(out CinemachineGroupComposerData compData))
                        compData.SetCompValues(CMGC);

                var CMC = CMVCam.GetCinemachineComponent<Cinemachine.CinemachineComposer>();
                if (CMC != null)
                    if (CMVCData.GetCompData(out CinemachineComposerData compData))
                        compData.SetCompValues(CMC);

                var CMPOV = CMVCam.GetCinemachineComponent<Cinemachine.CinemachinePOV>();
                if (CMPOV != null)
                    if (CMVCData.GetCompData(out CinemachinePOVData compData))
                        compData.SetCompValues(CMPOV);

                var CMSAFT = CMVCam.GetCinemachineComponent<Cinemachine.CinemachineSameAsFollowTarget>();
                if (CMSAFT != null)
                    if (CMVCData.GetCompData(out CinemachineSameAsFollowTargetData compData))
                        compData.SetCompValues(CMSAFT);

                var CMBMCP = CMVCam.GetCinemachineComponent<Cinemachine.CinemachineBasicMultiChannelPerlin>();
                if (CMBMCP != null)
                    if (CMVCData.GetCompData(out CinemachineBasicMultiChannelPerlinData compData))
                        compData.SetCompValues(CMBMCP);
            }
#endif
        }

        private static void ApplyAll()
        {
            _objectDictionary.Clear();
            var comIds = _compData.Keys.ToArray();
            foreach (var id in comIds) Apply(id);
            if (Create())
                foreach (var id in comIds) Apply(id);
            foreach (var id in comIds)
                if (!PMSData.Contains(id, out ComponentSaveDataKey foundKey)) CompDataRemoveKey(id);
            Delete();
            _objectsToBeDeleted.Clear();
            _componentsToBeDeleted.Clear();
            UnityEditor.EditorApplication.RepaintHierarchyWindow();
        }
        #endregion

        #region Create & Delete
        public static bool Create()
        {
            GameObject CreateObj(SaveObject saveObj, Transform parent, string scenePath)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(scenePath);
                if (scene.IsValid()) UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
                else return null;
                GameObject obj = saveObj.prefabPath != null
                        ? (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab
                        (UnityEditor.AssetDatabase.LoadMainAssetAtPath(saveObj.prefabPath))
                        : new GameObject(saveObj.name);
                obj.name = saveObj.name;
                if (_saveActiveState) obj.SetActive(saveObj.isActive);
                UnityEditor.Undo.RegisterCreatedObjectUndo(obj, "Save Object Created In Play Mode");
                obj.transform.parent = parent;
                obj.isStatic = saveObj.isStatic;
                obj.tag = saveObj.tag;
                obj.layer = saveObj.layer;
                obj.transform.SetSiblingIndex(saveObj.siblingIndex);

                var compToRemove = obj.GetComponents<Component>().ToList();
                foreach (var type in saveObj.compDataDictionary.Keys)
                {
                    var compDataList = saveObj.compDataDictionary[type];
                    var components = obj.GetComponents(type);
                    for (int i = 0; i < compDataList.Count; ++i)
                    {
                        var compData = compDataList[i];
                        var comp = obj.GetComponent(type);
                        if (comp == null || components.Length < i + 1) comp = obj.AddComponent(type);
                        if (components.Length > i) comp = components[i];
                        if (compToRemove.Contains(comp)) compToRemove.Remove(comp);
                        if (compData.serializedObj == null) continue;
                        CopyPropertiesFromData(comp, compData.serializedObj, compData.objectReferences);
                        if (compData is ClothSaveDataValue)
                        {
                            var clothData = compData as ClothSaveDataValue;
                            var cloth = comp as Cloth;
                            cloth.coefficients = clothData.coefficients.ToArray();
                        }
                    }
                }
                if (saveObj.compDataDictionary.Count > 0)
                    foreach (var comp in compToRemove) DestroyImmediate(comp);
                if (saveObj.always)
                {
                    var createdComponents = obj.GetComponentsInChildren<Component>();
                    foreach (var comp in createdComponents) Add(comp, SaveCommand.SAVE_NOW, true, saveObj.includeChildren);
                }

                if (!saveObj.includeChildren || saveObj.saveChildren == null) return obj;
                foreach (var child in saveObj.saveChildren)
                {
                    var prefabChild = obj.transform.Find(child.name);
                    if (_objData.ContainsKey(child.objKey)) continue;
                    if (prefabChild != null && prefabChild.transform.GetSiblingIndex() == child.siblingIndex) continue;
                    CreateObj(child, obj.transform, scenePath);
                }
                return obj;
            }

            var objDataClone = new System.Collections.Generic.Dictionary<ObjectDataKey, SaveObject>();
            foreach (var key in _objData.Keys)
            {
                if (objDataClone.ContainsKey(key)) continue;
                objDataClone.Add(key, _objData[key]);
            }
            bool objCreated = false;

            bool IsANestedPrefab(SaveObject saveObj)
            {
                if (saveObj.prefabPath == null) return false;
                if (saveObj.parentKey.isNull) return false;
                if (!objDataClone.ContainsKey(saveObj.parentKey)) return false;
                var parent = objDataClone[saveObj.parentKey];
                return parent.prefabPath != null;
            }

            foreach (var key in objDataClone.Keys)
            {
                if (WillBeDeleted(key, out ObjectDataKey TBDFoundKey)) continue;
                var root = FindObject(key, false);
                var data = objDataClone[key];
                if (root != null) continue;
                Transform rootParent = null;
                if (!data.isRoot)
                {
                    var rootParentObj = FindObject(data.parentKey);
                    if (rootParentObj == null) continue;
                    rootParent = rootParentObj.transform;
                }
                if (!IsANestedPrefab(data))
                    CreateObj(data, rootParent, key.scenePath);
                objCreated = true;
            }
            _objData.Clear();
            UnityEditor.EditorApplication.RepaintHierarchyWindow();
            return objCreated;
        }

        public static void Delete()
        {
            foreach (var objItem in _objectsToBeDeleted)
            {
                var obj = FindObject(objItem);
                if (obj == null) continue;
                UnityEditor.Undo.DestroyObjectImmediate(obj);
            }
            foreach (var item in _componentsToBeDeleted)
            {
                GameObject obj = null;
                var comp = FindComponent(item, out obj);
                if (comp == null) continue;
                UnityEditor.Undo.DestroyObjectImmediate(comp);
            }
            UnityEditor.EditorApplication.RepaintHierarchyWindow();
        }
        #endregion

        #region Initialization & Loading
        public static void UpdateFullObjects()
        {
            PMSData.Load();
            var alwaysSaveFull = PMSData.alwaysSaveFullArray;
            foreach (var item in alwaysSaveFull)
            {
                var obj = FindObject(item);
                if (obj == null)
                {
                    PMSData.RemoveFull(item);
                    continue;
                }
                var objKey = new ObjectDataKey(obj);
                var components = _includeChildren ? obj.GetComponentsInChildren<Component>() : obj.GetComponents<Component>();
                foreach (var comp in components) Add(comp, SaveCommand.SAVE_ON_EXITING_PLAY_MODE, true, true);
                AddFullObjectData(objKey, true);
            }
        }

        public static void LoadData()
        {
            if (!PMSData.Load()) return;
            foreach (var key in PMSData.alwaysSaveArray)
            {
                GameObject obj = null;
                var comp = FindComponent(key, out obj);
                if (obj == null || comp == null)
                {
                    if (string.IsNullOrEmpty(UnityEditor.AssetDatabase.AssetPathToGUID(key.objKey.scenePath)))
                        PMSData.Remove(key);
                    else
                    {
                        var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                        if (activeScene.path == key.objKey.scenePath) PMSData.Remove(key);
                    }
                    continue;
                }
                Add(comp, SaveCommand.SAVE_ON_EXITING_PLAY_MODE, true, Application.isPlaying);
            }
            UnityEditor.EditorApplication.RepaintHierarchyWindow();
        }

        public static void UpdateObjKeys()
        {
            if (!PMSData.Load(false)) return;
            foreach (var key in PMSData.alwaysSaveArray) key.UpdateObjKey();
            UnityEditor.EditorApplication.RepaintHierarchyWindow();
        }
        #endregion
    }
}
#pragma warning restore UDR0001
