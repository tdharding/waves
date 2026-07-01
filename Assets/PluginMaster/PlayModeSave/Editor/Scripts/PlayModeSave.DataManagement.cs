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
        #region Serialization
        public static int GetMaxPropCount(Component comp)
        {
#if PMS_URP && UNITY_2021_2_OR_NEWER
            if (comp is UnityEngine.Rendering.Universal.Light2D) return 4000;
#endif
            return 1000;
        }

        public static void CopyPropertiesFromComponent(Component comp, UnityEditor.SerializedObject data,
            ref System.Collections.Generic.Dictionary<string, ComponentSaveDataKey> refDict)
        {
            var maxPropCount = GetMaxPropCount(comp);
            var serializedObj = new UnityEditor.SerializedObject(comp);
            serializedObj.Update();

            int initialCapacity = System.Math.Min(32, maxPropCount / 4);
            refDict = new System.Collections.Generic.Dictionary<string, ComponentSaveDataKey>(initialCapacity);

            var prop = serializedObj.GetIterator();
            int propCount = 0;

            bool enterChildren = true;
            while (prop.Next(enterChildren) && propCount < maxPropCount)
            {
                enterChildren = prop.propertyType == UnityEditor.SerializedPropertyType.Generic;

                if (prop.name == "m_Script" || !prop.editable)
                    continue;

                if (prop.propertyType == UnityEditor.SerializedPropertyType.ObjectReference &&
                    prop.objectReferenceValue is Component component)
                {
                    refDict[prop.propertyPath] = new ComponentSaveDataKey(component);
                }

                data.CopyFromSerializedProperty(prop);
                propCount++;
            }
        }

        public static void CopyPropertiesFromData(Component comp, UnityEditor.SerializedObject data,
            System.Collections.Generic.Dictionary<string, ComponentSaveDataKey> refDict, string[] propertyPaths = null)
        {
            if (comp == null || data == null) return;

            var maxPropCount = GetMaxPropCount(comp);
            var serializedObj = new UnityEditor.SerializedObject(comp);
            var prop = data.GetIterator();
            int propCount = 0;

            var propertyPathsSet = propertyPaths != null && propertyPaths.Length > 0
                ? new System.Collections.Generic.HashSet<string>(propertyPaths)
                : null;

            bool hasRefs = refDict != null && refDict.Count > 0;

            while (prop.NextVisible(true) && propCount < maxPropCount)
            {
                if (propertyPathsSet != null && !propertyPathsSet.Contains(prop.propertyPath))
                {
                    propCount++;
                    continue;
                }

                var compProperty = serializedObj.FindProperty(prop.propertyPath);
                if (compProperty == null)
                {
                    propCount++;
                    continue;
                }

                if (prop.propertyType == UnityEditor.SerializedPropertyType.ObjectReference &&
                    prop.name != "m_Script" &&
                    hasRefs &&
                    refDict.TryGetValue(prop.propertyPath, out var compKey))
                {
                    var refComp = FindComponent(compKey, out GameObject obj);
                    compProperty.objectReferenceValue = refComp;
#if !UNITY_6000_3_OR_NEWER
                    compProperty.objectReferenceInstanceIDValue = refComp == null ? -1 : refComp.GetInstanceID();
#endif
                }
                else
                {
                    serializedObj.CopyFromSerializedProperty(prop);
                }

                propCount++;
            }
            serializedObj.ApplyModifiedProperties();
        }
        #endregion

        #region Data Management
        public static bool CompDataContainsKey(ref ComponentSaveDataKey key, bool update = false)
        {
            foreach (var compKey in _compData.Keys)
            {
                if (compKey == key)
                {
                    if (update) UpdateCompKey(compKey, key);
                    key = compKey;
                    return true;
                }
            }
            return false;
        }

        public static void CompDataRemoveKey(ComponentSaveDataKey key)
        {
            if (CompDataContainsKey(ref key))
            {
                var dataClone = new System.Collections.Generic.Dictionary<ComponentSaveDataKey, SaveDataValue>();
                foreach (var compKey in _compData.Keys)
                {
                    if (compKey == key) continue;
                    if (dataClone.ContainsKey(compKey)) continue;
                    dataClone.Add(compKey, _compData[compKey]);
                }
                _compData = dataClone;
            }
        }

        public static void UpdateCompKey(ComponentSaveDataKey oldKey, ComponentSaveDataKey newKey)
        {
            oldKey.objKey.objId = newKey.objKey.objId;
            oldKey.objKey.globalObjId = newKey.objKey.globalObjId;
            oldKey.compId = newKey.compId;
            oldKey.globalCompId = newKey.globalCompId;
        }

        public static void UpdateObjectDataKey(ObjectDataKey oldKey, ObjectDataKey newKey)
        {
            oldKey.objId = newKey.objId;
            oldKey.globalObjId = newKey.globalObjId;
        }

        public static bool FullObjectDataContains(ObjectDataKey objKey, out FullObjectData foundItem)
        {
            foundItem = null;
            foreach (var item in _fullObjData)
            {
                if (objKey == item.key)
                {
                    foundItem = item;
                    return true;
                }
            }
            return false;
        }

        public static void AddFullObjectData(ObjectDataKey objKey, bool always)
        {
            if (FullObjectDataContains(objKey, out FullObjectData foundItem))
            {
                foundItem.key.Copy(objKey);
                foundItem.always = always;
                return;
            }
            _fullObjData.Add(new FullObjectData(objKey, always));
        }

        public static void RemoveFullObjectData(ObjectDataKey objKey)
        {
            if (FullObjectDataContains(objKey, out FullObjectData foundItem)) _fullObjData.Remove(foundItem);
        }

        public static bool WillBeDeleted(ObjectDataKey objKey, out ObjectDataKey foundKey)
        {
            foundKey = null;
            foreach (var toBeDeleted in _objectsToBeDeleted)
            {
                if (toBeDeleted == objKey)
                {
                    foundKey = toBeDeleted;
                    return true;
                }
            }
            return false;
        }

        public static void ToBeDeleted(ObjectDataKey objKey)
        {
            if (WillBeDeleted(objKey, out ObjectDataKey foundKey))
            {
                foundKey.Copy(objKey);
            }
            else _objectsToBeDeleted.Add(objKey);
        }
        #endregion

    }
}
#pragma warning restore UDR0001
