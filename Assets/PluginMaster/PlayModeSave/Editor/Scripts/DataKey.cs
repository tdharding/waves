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
using System.Linq;


namespace PluginMaster
{
    [System.Serializable]
    public class ObjectDataKeyBase : ISerializationCallbackReceiver, System.IEquatable<ObjectDataKeyBase>
    {
#if UNITY_6000_3_OR_NEWER
            [SerializeField] private EntityId _objId = EntityId.None;
#else
        [SerializeField] private int _objId = -1;
#endif
        [SerializeField] private string _globalObjId = null;
        [SerializeField] private string _scenePath = null;
        [SerializeField] private int[] _siblingPath = null;
        [SerializeField] private string _rootName = null;
        [SerializeField] private string _objName = null;
        private bool _resolveObjIdPending = true;
#if UNITY_6000_3_OR_NEWER
            public EntityId objId
#else
        public int objId
#endif
        {
            get
            {
                if (_resolveObjIdPending) ResolveObjectId();
                return _objId;
            }
            set => _objId = value;
        }
        public string globalObjId { get => _globalObjId; set => _globalObjId = value; }
        public string scenePath => _scenePath;
        public int[] siblingPath => _siblingPath;
        public string rootName => _rootName;
        public string objName => _objName;
        protected void NullInitialization()
        {
#if UNITY_6000_3_OR_NEWER
                _objId = EntityId.None;
#else
            _objId = -1;
#endif
            _globalObjId = null;
            _scenePath = null;
        }
        public ObjectDataKeyBase(GameObject gameObject)
        {
            if (gameObject == null)
            {
                NullInitialization();
                return;
            }
#if UNITY_6000_3_OR_NEWER
                _objId = gameObject.GetEntityId();
#else
            _objId = gameObject.GetInstanceID();
#endif
            _globalObjId = UnityEditor.GlobalObjectId.GetGlobalObjectIdSlow(gameObject).ToString();
            _scenePath = gameObject.scene.path;
            var sibPath = new System.Collections.Generic.List<int>();
            var parent = gameObject.transform;
            do
            {
                sibPath.Insert(0, parent.GetSiblingIndex());
                if (parent.parent == null) _rootName = parent.name;
                parent = parent.parent;
            } while (parent != null);
            _siblingPath = sibPath.ToArray();
            _objName = gameObject.name;
        }
#if UNITY_6000_3_OR_NEWER
            public ObjectDataKeyBase(EntityId objId, string globalObjId, string scenePath)
#else
        public ObjectDataKeyBase(int objId, string globalObjId, string scenePath)
#endif
        {
            _objId = objId;
            _globalObjId = globalObjId;
            _scenePath = scenePath;
        }

        public void UpdateSibPath()
        {
            var sibPath = new System.Collections.Generic.List<int>();
            var gameObject = PlayModeSave.FindObject(this);
            if (gameObject == null) return;
            var parent = gameObject.transform;
            do
            {
                sibPath.Insert(0, parent.GetSiblingIndex());
                if (parent.parent == null) _rootName = parent.name;
                parent = parent.parent;
            } while (parent != null);
            _siblingPath = sibPath.ToArray();
            _objName = gameObject.name;
        }

        public bool isNull => _globalObjId == null;

        public override int GetHashCode() => _globalObjId?.GetHashCode() ?? 0;
        public virtual bool Equals(ObjectDataKeyBase other)
        {
            if (other is null) return false;
            return _globalObjId == other._globalObjId;
        }
        public override bool Equals(object obj) => obj is ObjectDataKeyBase other && this.Equals(other);

        public virtual bool HierarchyEquals(ObjectDataKeyBase other)
        {
            if (other is null) return false;
            if (_objId == other._objId) return true;
            if (_globalObjId == other._globalObjId) return true;
            if (_scenePath != other._scenePath) return false;
            if (_rootName != other._rootName) return false;
            if (_objName != other._objName) return false;
            return Enumerable.SequenceEqual(_siblingPath, other._siblingPath);
        }
        public static bool operator ==(ObjectDataKeyBase lhs, ObjectDataKeyBase rhs)
        {
            if (ReferenceEquals(lhs, rhs)) return true;
            if (ReferenceEquals(lhs, null) || ReferenceEquals(rhs, null)) return false;
            return lhs.Equals(rhs);
        }
        public static bool operator !=(ObjectDataKeyBase lhs, ObjectDataKeyBase rhs) => !lhs.Equals(rhs);

        public void Copy(ObjectDataKeyBase other)
        {
            _objId = other._objId;
            _globalObjId = other._globalObjId;
            _scenePath = other._scenePath;
        }

        public void OnBeforeSerialize() { }

        public void OnAfterDeserialize()
        {
            _resolveObjIdPending = true;
            PMSData.saveAfterLoading = true;
        }

        public void ResolveObjectId()
        {
            _resolveObjIdPending = false;
            if (UnityEditor.GlobalObjectId.TryParse(_globalObjId, out UnityEditor.GlobalObjectId id))
            {
                var obj = UnityEditor.GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as GameObject;
                if (obj == null) return;
#if UNITY_6000_3_OR_NEWER
                    _objId = obj.GetEntityId();
#else
                _objId = obj.GetInstanceID();
#endif
            }
        }
    }


    [System.Serializable]
    public class ObjectDataKey : ObjectDataKeyBase
    {
        [SerializeField]
        private System.Collections.Generic.List<ObjectDataKeyBase> _parentKeys
            = new System.Collections.Generic.List<ObjectDataKeyBase>();
        public ObjectDataKey(GameObject gameObject) : base(gameObject)
        {
            if (gameObject == null)
            {
                NullInitialization();
                return;
            }
            var parent = gameObject.transform;
            do
            {
                parent = parent.parent;
                if (parent != null) _parentKeys.Add(new ObjectDataKeyBase(parent.gameObject));
            } while (parent != null);
        }

        public void UpdateParentKeys()
        {
            var obj = PlayModeSave.FindObject(this);
            _parentKeys.Clear();
            if (obj == null) return;
            var parent = obj.transform;
            do
            {
                parent = parent.parent;
                if (parent != null) _parentKeys.Add(new ObjectDataKeyBase(parent.gameObject));
            } while (parent != null);
            UpdateSibPath();
        }

        public bool ParentHasChanged(out Transform savedParent, out int savedSiblingIndex)
        {
            savedParent = null;
            savedSiblingIndex = 0;
            var obj = PlayModeSave.FindObject(this);
            var currentParent = obj == null ? null : obj.transform.parent;

            if (_parentKeys.Count > 0)
            {
                var firstParentkey = _parentKeys[0];
                var firstSavedParentObj = PlayModeSave.FindObject(firstParentkey);
                savedParent = firstSavedParentObj == null ? null : firstSavedParentObj.transform;
            }
            else if (currentParent == null)
                return false;
            savedSiblingIndex = (siblingPath != null && siblingPath.Length > 0) ? siblingPath.Last() : 0;
            foreach (var parentKey in _parentKeys)
            {
                var savedParentObj = PlayModeSave.FindObject(parentKey);
                var savedParentTransform = savedParentObj == null ? null : savedParentObj.transform;
                if (savedParentTransform != currentParent) return true;
                if (currentParent == null) break;
                currentParent = currentParent.parent;
            }
            if (savedParent == null) return true;
            return false;
        }

        public override bool HierarchyEquals(ObjectDataKeyBase other)
        {
            var baseEquals = base.Equals(other);
            if (!baseEquals) return false;
            var otherKey = other as ObjectDataKey;
            if (otherKey == null) return baseEquals;
            return Enumerable.SequenceEqual(_parentKeys, otherKey._parentKeys);
        }
    }

    [System.Serializable]
    public class ComponentSaveDataKey : ISerializationCallbackReceiver, System.IEquatable<ComponentSaveDataKey>
    {
        [SerializeField] private ObjectDataKey _objkey = null;
#if UNITY_6000_3_OR_NEWER
            [SerializeField] private EntityId _compId = EntityId.None;
#else
        [SerializeField] private int _compId = -1;
#endif
        [SerializeField] private string _globalCompId = null;
        [SerializeField] private int _compIdx = -1;
        [SerializeField] private string _compTypeName = null;
        [SerializeField] private string _globalObjId = null;
        private bool _resolveCompIdPending = true;
        public ObjectDataKey objKey
        {
            get
            {
                UpdateObjKey();
                return _objkey;
            }
        }
#if UNITY_6000_3_OR_NEWER
            public EntityId compId
#else
        public int compId
#endif
        {
            get
            {
                if (_resolveCompIdPending) ResolveCompId();
                return _compId;
            }
            set => _compId = value;
        }
        public string globalCompId { get => _globalCompId; set => _globalCompId = value; }
        public int compIdx => _compIdx;
        public System.Type compType => System.Type.GetType(_compTypeName, false);



        public ComponentSaveDataKey(Component component)
        {
            _objkey = new ObjectDataKey(component.gameObject);
#if UNITY_6000_3_OR_NEWER
                _compId = component.GetEntityId();
#else
            _compId = component.GetInstanceID();
#endif
            _globalCompId = UnityEditor.GlobalObjectId.GetGlobalObjectIdSlow(component).ToString();
            var compType = component.GetType();
            _compTypeName = compType.AssemblyQualifiedName;
            var comps = component.gameObject.GetComponents(compType);
            _compIdx = System.Array.IndexOf(comps, component);

        }

        public void UpdateObjKey()
        {
            var objKeyUninitialized = _objkey is null;
            if (!objKeyUninitialized)
                if (string.IsNullOrEmpty(_objkey.globalObjId)) objKeyUninitialized = true;
            if (objKeyUninitialized && !string.IsNullOrEmpty(_globalObjId))
            {
                if (UnityEditor.GlobalObjectId.TryParse(_globalObjId, out UnityEditor.GlobalObjectId id))
                {
                    var obj = UnityEditor.GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as GameObject;
                    if (obj == null) return;
                    _objkey = new ObjectDataKey(obj);
                }
            }
        }

        public void OnBeforeSerialize() { }
        public void OnAfterDeserialize()
        {
            _resolveCompIdPending = true;
            PMSData.saveAfterLoading = true;
        }
        public void ResolveCompId()
        {
            UpdateObjKey();
            _resolveCompIdPending = false;
            if (UnityEditor.GlobalObjectId.TryParse(_globalCompId, out UnityEditor.GlobalObjectId compId))
            {
                var comp = UnityEditor.GlobalObjectId.GlobalObjectIdentifierToObjectSlow(compId) as Component;
                if (comp == null) return;
#if UNITY_6000_3_OR_NEWER
                    _compId = comp.GetEntityId();
#else
                _compId = comp.GetInstanceID();
#endif
            }
        }


        public override int GetHashCode() => (_objkey.globalObjId, _globalCompId).GetHashCode();
        public bool Equals(ComponentSaveDataKey other)
        {
            if (other is null) return false;
            return (_objkey == other._objkey && (_globalCompId == other._globalCompId || _compId == other._compId));
        }

        public bool HierarchyEquals(ComponentSaveDataKey other)
        {
            return (_objkey.HierarchyEquals(other._objkey)
                && (_globalCompId == other._globalCompId || _compId == other._compId));
        }

        public override bool Equals(object obj) => obj is ComponentSaveDataKey other && this.Equals(other);
        public static bool operator ==(ComponentSaveDataKey lhs, ComponentSaveDataKey rhs)
        {
            if (ReferenceEquals(lhs, rhs)) return true;
            if (ReferenceEquals(lhs, null) || ReferenceEquals(rhs, null)) return false;
            return lhs.Equals(rhs);
        }

        public static bool operator !=(ComponentSaveDataKey lhs, ComponentSaveDataKey rhs)
        {
            return !(lhs == rhs);
        }
    }
}
#pragma warning restore UDR0001
