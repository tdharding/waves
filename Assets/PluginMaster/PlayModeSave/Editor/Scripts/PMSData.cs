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
    [System.Serializable]
    public class PMSData
    {
        private const string FILE_NAME = "PMSData";
        private const string RELATIVE_PATH = "/PluginMaster/PlayModeSave/Editor/Resources/";
        [SerializeField] private string _rootDirectory = null;
        [SerializeField]
        private System.Collections.Generic.List<ComponentSaveDataKey> _alwaysSave
            = new System.Collections.Generic.List<ComponentSaveDataKey>();
        [SerializeField]
        private System.Collections.Generic.List<ObjectDataKey> _alwaysSaveFull
            = new System.Collections.Generic.List<ObjectDataKey>();

        public static ComponentSaveDataKey[] alwaysSaveArray => instance._alwaysSave.ToArray();
        public static ObjectDataKey[] alwaysSaveFullArray => instance._alwaysSaveFull.ToArray();
        public static void AlwaysSave(ComponentSaveDataKey key)
        {
            if (instance._alwaysSave.Contains(key)) return;
            instance._alwaysSave.Add(key);
            Save();
        }
        public static void AlwaysSaveFull(ObjectDataKey data)
        {
            if (instance._alwaysSaveFull.Contains(data)) return;
            instance._alwaysSaveFull.Add(data);
            Save();
        }
        public static bool saveAfterLoading { get; set; }
        public static void Save(bool refreshDataBase = true)
        {
            if (string.IsNullOrEmpty(instance._rootDirectory)) instance._rootDirectory = Application.dataPath;
            var fullDirectoryPath = instance._rootDirectory + RELATIVE_PATH;
            var fullFilePath = fullDirectoryPath + FILE_NAME + ".txt";
            if (!System.IO.File.Exists(fullFilePath))
            {
                var directories = System.IO.Directory.GetDirectories(Application.dataPath,
                    "PluginMaster", System.IO.SearchOption.AllDirectories);
                if (directories.Length == 0) System.IO.Directory.CreateDirectory(fullDirectoryPath);
                else
                {
                    instance._rootDirectory = System.IO.Directory.GetParent(directories[0]).FullName;
                    fullDirectoryPath = instance._rootDirectory + RELATIVE_PATH;
                    fullFilePath = fullDirectoryPath + FILE_NAME + ".txt";
                }
                if (!System.IO.Directory.Exists(fullDirectoryPath))
                    System.IO.Directory.CreateDirectory(fullDirectoryPath);
            }
            var jsonString = JsonUtility.ToJson(instance);
            System.IO.File.WriteAllText(fullFilePath, jsonString);
            if (refreshDataBase) UnityEditor.AssetDatabase.Refresh();
        }
        public static void Remove(ComponentSaveDataKey item)
        {
            if (Contains(item, out ComponentSaveDataKey foundKey))
            {
                instance._alwaysSave.Remove(foundKey);
                Save();
            }
        }

        public static void RemoveFull(ObjectDataKey item)
        {
            if (ContainsFull(item, out ObjectDataKey foundKey))
            {
                instance._alwaysSaveFull.Remove(foundKey);
                Save();
            }
        }

        public static void UpdateFull()
        {
            var loadedScenePaths = PlayModeSave.GetLoadedScenePaths();
            var alwaysSaveFull = alwaysSaveFullArray;
            var removed = false;
            foreach (var item in alwaysSaveFull)
            {
                if (!loadedScenePaths.Contains(item.scenePath)) continue;
                var obj = PlayModeSave.FindObject(item, false);
                if (obj == null)
                {
                    removed = true;
                    instance._alwaysSaveFull.Remove(item);
                }
            }
            var alwaysSaveComponents = alwaysSaveArray;
            foreach (var item in alwaysSaveComponents)
            {
                if (!loadedScenePaths.Contains(item.objKey.scenePath)) continue;
                var obj = PlayModeSave.FindObject(item.objKey, false);
                if (obj == null)
                {
                    removed = true;
                    instance._alwaysSave.Remove(item);
                }
            }
            if (removed) Save();
        }
        public static bool Load(bool refreshDataBase = true)
        {
            var jsonTextAsset = Resources.Load<TextAsset>(FILE_NAME);
            if (jsonTextAsset == null) return false;
            _instance = JsonUtility.FromJson<PMSData>(jsonTextAsset.text);
            _loaded = true;
            if (saveAfterLoading) Save(refreshDataBase);
            return true;
        }

        public static bool Contains(ComponentSaveDataKey key, out ComponentSaveDataKey foundKey)
        {
            foundKey = null;
            if (!_loaded) Load();
            foreach (var compKey in instance._alwaysSave)
            {
                if (compKey == key)
                {
                    foundKey = compKey;
                    return true;
                }
            }
            return false;
        }

        public static bool ContainsFull(ObjectDataKey key, out ObjectDataKey foundKey)
        {
            foundKey = null;
            if (!_loaded) Load();
            foreach (var objKey in instance._alwaysSaveFull)
            {
                if (objKey == key)
                {
                    foundKey = objKey;
                    return true;
                }
            }
            return false;

        }
        private static PMSData _instance = null;
        private static bool _loaded = false;
        private PMSData() { }
        private static PMSData instance
        {
            get
            {
                if (_instance == null) _instance = new PMSData();
                return _instance;
            }
        }
    }
}
#pragma warning restore UDR0001
