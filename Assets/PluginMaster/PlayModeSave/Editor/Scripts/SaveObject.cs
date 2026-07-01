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
    public class SaveObject
    {
        public PlayModeSave.SaveCommand saveCmd;
        public string name = null;
        public string tag = null;
        public int layer = 0;
        public bool isStatic = false;
        public bool always = false;
        public bool includeChildren = false;
        public System.Collections.Generic.List<SaveObject> saveChildren = null;
        public System.Collections.Generic.Dictionary<System.Type, System.Collections.Generic.List<SaveDataValue>>
            compDataDictionary
            = new System.Collections.Generic.Dictionary<System.Type, System.Collections.Generic.List<SaveDataValue>>();
        public bool isRoot = true;
        public ObjectDataKey parentKey;
        public string prefabPath = null;
        public int siblingIndex = -1;
        private GameObject _obj = null;
        public ObjectDataKey objKey;
        public bool unloadedScene = false;
        public bool isActive = true;
        public bool objIsNull => _obj == null;
        public SaveObject(Transform transform, PlayModeSave.SaveCommand cmd, bool always, bool includeChildren)
        {
            this.always = always;
            this.includeChildren = includeChildren;
            _obj = transform.gameObject;
            var prefabRoot = UnityEditor.PrefabUtility.GetNearestPrefabInstanceRoot(_obj);
            if (prefabRoot == _obj)
                prefabPath = UnityEditor.PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(_obj);
            name = _obj.name;
            tag = _obj.tag;
            layer = _obj.layer;
            isStatic = _obj.isStatic;
            isRoot = transform.parent == null;
            var parent = isRoot ? null : transform.parent.gameObject;
            parentKey = new ObjectDataKey(parent);
            objKey = new ObjectDataKey(transform.gameObject);
            siblingIndex = _obj.transform.GetSiblingIndex();
            saveCmd = cmd;
            isActive = _obj.activeSelf;
        }

        public System.Type[] types => compDataDictionary.Keys.ToArray();

        public void Update()
        {
            if (_obj == null) return;
            ClearCompDataDictionary();
            var components = _obj.GetComponents<Component>();
            int blockSize = 10;
            for (int i = 0; i < components.Length; i += blockSize)
            {
                int endIndex = System.Math.Min(i + blockSize, components.Length);
                for (int j = i; j < endIndex; j++)
                {
                    if (components[j] == null) continue;
                    AddComponentSaveData(components[j]);
                }
                if (i + blockSize < components.Length && i % 50 == 0)
                    System.Threading.Thread.Sleep(1);
            }
            UpdateChildren();
        }

        private void ClearCompDataDictionary()
        {
            foreach (var list in compDataDictionary.Values)
                list.Clear();
            compDataDictionary.Clear();
        }

        private void AddComponentSaveData(Component comp)
        {
            var type = comp.GetType();
            if (!compDataDictionary.TryGetValue(type, out var saveList))
            {
                saveList = new System.Collections.Generic.List<SaveDataValue>();
                compDataDictionary[type] = saveList;
            }
            var saveData = CreateSaveDataValue(comp);
            if (saveData != null)
                saveList.Add(saveData);
        }

        private SaveDataValue CreateSaveDataValue(Component comp)
        {
            UnityEditor.SerializedObject data = new UnityEditor.SerializedObject(comp);

            if (comp is SpriteRenderer renderer)
            {
                var saveData = new SpriteRendererSaveDataValue(data, saveCmd, renderer,
                    renderer.sortingOrder, renderer.sortingLayerID);
                PlayModeSave.CopyPropertiesFromComponent(comp, data, ref saveData.objectReferences);
                return saveData;
            }
            else if (comp is UnityEngine.U2D.SpriteShapeRenderer spriteShapeRenderer)
            {
                var saveData = new SpriteShapeRendererSaveDataValue(data, saveCmd,
                    spriteShapeRenderer, spriteShapeRenderer.sortingOrder, spriteShapeRenderer.sortingLayerID);
                PlayModeSave.CopyPropertiesFromComponent(comp, data, ref saveData.objectReferences);
                return saveData;
            }
            else if (comp is ParticleSystem particleSystem)
            {
                var rendererPS = particleSystem.GetComponent<ParticleSystemRenderer>();
                var saveData = new ParticleSystemSaveDataValue(data, saveCmd, rendererPS,
                    rendererPS.sortingOrder, rendererPS.sortingLayerID);
                PlayModeSave.CopyPropertiesFromComponent(comp, data, ref saveData.objectReferences);
                return saveData;
            }
            else if (comp is Cloth cloth)
            {
                var saveData = new ClothSaveDataValue(data, saveCmd, comp, cloth.coefficients);
                PlayModeSave.CopyPropertiesFromComponent(comp, data, ref saveData.objectReferences);
                return saveData;
            }
            else if (comp is UnityEngine.Rendering.SortingGroup sortingGroup)
            {
                var saveData = new SortingGroupSaveDataValue(data, saveCmd, sortingGroup,
                    sortingGroup.sortingOrder, sortingGroup.sortingLayerID);
                PlayModeSave.CopyPropertiesFromComponent(comp, data, ref saveData.objectReferences);
                return saveData;
            }
            else if (comp is UnityEngine.Tilemaps.Tilemap tilemap)
            {
                var saveData = new TilemapSaveDataValue(data, saveCmd, tilemap);
                PlayModeSave.CopyPropertiesFromComponent(comp, data, ref saveData.objectReferences);
                return saveData;
            }
#if PMS_CINEMACHINE && !PMS_CINE_MACHINE_3_0_OR_NEWER
                else if (comp is Cinemachine.CinemachineVirtualCamera)
                {
                    var CMVCam = comp as Cinemachine.CinemachineVirtualCamera;
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
                    {
                        var saveData = new CinemachineVirtualCameraSaveDataValue(data, saveCmd,
                            CMVCam, CMCompDataList.ToArray());
                        PlayModeSave.CopyPropertiesFromComponent(comp, data, ref saveData.objectReferences);
                        return saveData;
                    }
                    else
                    {
                        var saveData = new SaveDataValue(data, saveCmd, comp);
                        PlayModeSave.CopyPropertiesFromComponent(comp, data, ref saveData.objectReferences);
                        return saveData;
                    }
                }
#endif
            else
            {
                var saveData = new SaveDataValue(data, saveCmd, comp);
                PlayModeSave.CopyPropertiesFromComponent(comp, data, ref saveData.objectReferences);
                return saveData;
            }
        }

        private void UpdateChildren()
        {
            int childCount = _obj.transform.childCount;
            if (childCount > 0)
            {
                if (saveChildren == null || saveChildren.Count != childCount)
                    saveChildren = new System.Collections.Generic.List<SaveObject>(childCount);
                else
                    saveChildren.Clear();

                for (int i = 0; i < childCount; ++i)
                {
                    var child = _obj.transform.GetChild(i);
                    if (child == _obj.transform) continue;
                    var saveChild = new SaveObject(child, saveCmd, always, includeChildren);
                    saveChildren.Add(saveChild);
                }
            }
            else
            {
                saveChildren = null;
            }
        }
    }

    public class FullObjectData
    {
        public ObjectDataKey key = null;
        public bool always = false;
        public FullObjectData(ObjectDataKey key, bool always) => (this.key, this.always) = (key, always);
    }
}
#pragma warning restore UDR0001
