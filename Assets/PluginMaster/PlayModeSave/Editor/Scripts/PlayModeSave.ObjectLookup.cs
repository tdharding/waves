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
        public static string[] GetLoadedScenePaths()
        {
#if UNITY_2022_2_OR_NEWER
            var countLoaded = UnityEngine.SceneManagement.SceneManager.loadedSceneCount;
#else
            var countLoaded = UnityEditor.SceneManagement.EditorSceneManager.loadedSceneCount;
#endif
            var loadedScenePaths = new string[countLoaded];
            for (int i = 0; i < countLoaded; i++)
                loadedScenePaths[i] = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).path;
            return loadedScenePaths;
        }

        private static System.Collections.Generic.Dictionary<ObjectDataKeyBase, GameObject>
            _objectDictionary = new System.Collections.Generic.Dictionary<ObjectDataKeyBase, GameObject>();

        public static GameObject FindObject(ObjectDataKeyBase key, bool findInHierarchy = true)
        {
            if (_objectDictionary.ContainsKey(key)) return _objectDictionary[key];
#if UNITY_6000_3_OR_NEWER
            var obj = UnityEditor.EditorUtility.EntityIdToObject(key.objId) as GameObject;
#else
            var obj = UnityEditor.EditorUtility.InstanceIDToObject(key.objId) as GameObject;
#endif
            if (obj != null)
            {
                _objectDictionary.Add(key, obj);
                return obj;
            }
            if (string.IsNullOrEmpty(key.globalObjId))
            {
                _objectDictionary.Add(key, null);
                return null;
            }
            if (UnityEditor.GlobalObjectId.TryParse(key.globalObjId, out UnityEditor.GlobalObjectId id))
            {
                obj = UnityEditor.GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as GameObject;
                if (obj != null)
                {
#if UNITY_6000_3_OR_NEWER
                    key.objId = obj.GetEntityId();
#else
                    key.objId = obj.GetInstanceID();
#endif
                    _objectDictionary.Add(key, obj);
                    return obj;
                }
            }
            if (!findInHierarchy)
            {
                _objectDictionary.Add(key, null);
                return null;
            }
            var sibPath = key.siblingPath;

            GameObject FindInScene(UnityEngine.SceneManagement.Scene targetScene)
            {
                GameObject obInScene = null;
                var rootObjs = targetScene.GetRootGameObjects();
                if (rootObjs.Length <= sibPath[0]) return null;
                var rootObj = rootObjs[sibPath[0]];
                var childTrans = rootObj.transform;
                if (childTrans.name != key.rootName) return null;
                if (sibPath.Length == 1) obInScene = childTrans.gameObject;
                else
                {
                    for (var depth = 1; depth < sibPath.Length; ++depth)
                    {
                        if (sibPath[depth] >= childTrans.childCount) return null;
                        childTrans = childTrans.GetChild(sibPath[depth]);
                        if (childTrans == null) return null;
                    }
                    obInScene = childTrans.gameObject;
                }
                return obInScene;
            }

            if (key.scenePath == "DontDestroyOnLoad")
            {
                for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; ++i)
                {
                    var loadedScene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                    var ddol_obj = FindInScene(loadedScene);
                    if (ddol_obj != null)
                    {
                        if (obj != null) _objectDictionary.Add(key, obj);
                        else _objectDictionary.Add(key, ddol_obj);
                        return ddol_obj;
                    }
                }
                _objectDictionary.Add(key, null);
                return null;
            }
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(key.scenePath);
            if (!scene.IsValid())
            {
                _objectDictionary.Add(key, null);
                return null;
            }
            obj = FindInScene(scene);
            _objectDictionary.Add(key, obj);
            return obj;
        }

        public static Component FindComponent(ComponentSaveDataKey key, out GameObject obj)
        {
            if (string.IsNullOrEmpty(key.objKey.globalObjId)) key.UpdateObjKey();
            obj = FindObject(key.objKey);

            if (obj == null) return null;
            Component comp = null;
            if (obj != null && key.compType != null && key.compIdx >= 0)
            {
                var comps = obj.GetComponents(key.compType);
                if (key.compIdx < comps.Length)
                    comp = comps[key.compIdx];
            }
            else
            {
#if UNITY_6000_3_OR_NEWER
                comp = UnityEditor.EditorUtility.EntityIdToObject(key.compId) as Component;
#else
                comp = UnityEditor.EditorUtility.InstanceIDToObject(key.compId) as Component;
#endif
            }
            if (comp == null)
            {
                if (UnityEditor.GlobalObjectId.TryParse(key.globalCompId, out UnityEditor.GlobalObjectId id))
                {
                    comp = UnityEditor.GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as Component;
                    if (comp != null)
                        if (comp.GetType() != key.compType) comp = null;
                        else
#if UNITY_6000_3_OR_NEWER
                            key.compId = comp.GetEntityId();
#else
                            key.compId = comp.GetInstanceID();
#endif
                }
            }
            if (comp == null)
            {
                var type = key.compType;
                var comps = obj.GetComponents(type);
                if (comps.Length <= key.compIdx) return null;
                comp = comps[key.compIdx];
                if (comp.name != key.objKey.objName) return null;
            }
            return comp;
        }
    }
}
#pragma warning restore UDR0001
