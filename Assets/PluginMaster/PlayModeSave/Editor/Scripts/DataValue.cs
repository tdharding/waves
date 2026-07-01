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
    public class SaveDataValue
    {
        public UnityEditor.SerializedObject serializedObj;
        public PlayModeSave.SaveCommand saveCmd;
        public System.Type componentType;
#if UNITY_6000_3_OR_NEWER
            public EntityId compId;
#else
        public int compId;
#endif
        public string globalCompId;
        public System.Collections.Generic.Dictionary<string, ComponentSaveDataKey> objectReferences = null;

        public SaveDataValue(UnityEditor.SerializedObject serializedObj, PlayModeSave.SaveCommand saveCmd,
            Component component)
        {
            this.serializedObj = serializedObj;

            this.saveCmd = saveCmd;
            this.componentType = component.GetType();
#if UNITY_6000_3_OR_NEWER
                compId = component.GetEntityId();
#else
            compId = component.GetInstanceID();
#endif
            globalCompId = UnityEditor.GlobalObjectId.GetGlobalObjectIdSlow(component).ToString();
        }
#if UNITY_6000_3_OR_NEWER
            public virtual void Update(EntityId componentId)
#else
        public virtual void Update(int componentId)
#endif
        {
            if (serializedObj == null || serializedObj.targetObject == null) return;
            serializedObj.Update();
        }
    }

    public class SpriteRendererSaveDataValue : SaveDataValue
    {
        public int sortingOrder;
        public int sortingLayerID;
        public SpriteRendererSaveDataValue(UnityEditor.SerializedObject serializedObj,
            PlayModeSave.SaveCommand saveCmd,
            Component component, int sortingOrder, int sortingLayerID) : base(serializedObj, saveCmd, component)
            => (this.sortingOrder, this.sortingLayerID) = (sortingOrder, sortingLayerID);
#if UNITY_6000_3_OR_NEWER
            public override void Update(EntityId componentId)
#else
        public override void Update(int componentId)
#endif
        {
            base.Update(componentId);
#if UNITY_6000_3_OR_NEWER
                var spriteRenderer = UnityEditor.EditorUtility.EntityIdToObject(componentId) as SpriteRenderer;
#else
            var spriteRenderer = UnityEditor.EditorUtility.InstanceIDToObject(componentId) as SpriteRenderer;
#endif
            if (spriteRenderer == null) return;
            sortingOrder = spriteRenderer.sortingOrder;
            sortingLayerID = spriteRenderer.sortingLayerID;
        }
    }

    public class SpriteShapeRendererSaveDataValue : SaveDataValue
    {
        public int sortingOrder;
        public int sortingLayerID;
        public SpriteShapeRendererSaveDataValue(UnityEditor.SerializedObject serializedObj,
            PlayModeSave.SaveCommand saveCmd,
            Component component, int sortingOrder, int sortingLayerID) : base(serializedObj, saveCmd, component)
            => (this.sortingOrder, this.sortingLayerID) = (sortingOrder, sortingLayerID);
#if UNITY_6000_3_OR_NEWER
            public override void Update(EntityId componentId)
#else
        public override void Update(int componentId)
#endif
        {
            base.Update(componentId);
#if UNITY_6000_3_OR_NEWER
                var spriteRenderer = UnityEditor.EditorUtility.EntityIdToObject(componentId)
                    as UnityEngine.U2D.SpriteShapeRenderer;
#else
            var spriteRenderer = UnityEditor.EditorUtility.InstanceIDToObject(componentId)
                as UnityEngine.U2D.SpriteShapeRenderer;
#endif
            sortingOrder = spriteRenderer.sortingOrder;
            sortingLayerID = spriteRenderer.sortingLayerID;
        }
    }

    public class ParticleSystemSaveDataValue : SaveDataValue
    {
        public int sortingOrder;
        public int sortingLayerID;
        public ParticleSystemSaveDataValue(UnityEditor.SerializedObject serializedObj, PlayModeSave.SaveCommand saveCmd,
            Component component, int sortingOrder, int sortingLayerID) : base(serializedObj, saveCmd, component)
            => (this.sortingOrder, this.sortingLayerID) = (sortingOrder, sortingLayerID);

#if UNITY_6000_3_OR_NEWER
            public override void Update(EntityId componentId)
#else
        public override void Update(int componentId)
#endif
        {
            base.Update(componentId);
#if UNITY_6000_3_OR_NEWER
                var particleSystem = UnityEditor.EditorUtility.EntityIdToObject(componentId) as ParticleSystem;
#else
            var particleSystem = UnityEditor.EditorUtility.InstanceIDToObject(componentId) as ParticleSystem;
#endif
            var spriteRenderer = particleSystem.GetComponent<ParticleSystemRenderer>();
            sortingOrder = spriteRenderer.sortingOrder;
            sortingLayerID = spriteRenderer.sortingLayerID;
        }
    }

    public class ClothSaveDataValue : SaveDataValue
    {
        public ClothSkinningCoefficient[] coefficients;
        public ClothSaveDataValue(UnityEditor.SerializedObject serializedObj,
            PlayModeSave.SaveCommand saveCmd, Component component,
            ClothSkinningCoefficient[] coefficients) : base(serializedObj, saveCmd, component)
            => (this.coefficients) = (coefficients);

#if UNITY_6000_3_OR_NEWER
            public override void Update(EntityId componentId)
#else
        public override void Update(int componentId)
#endif
        {
            base.Update(componentId);
#if UNITY_6000_3_OR_NEWER
                var cloth = UnityEditor.EditorUtility.EntityIdToObject(componentId) as Cloth;
#else
            var cloth = UnityEditor.EditorUtility.InstanceIDToObject(componentId) as Cloth;
#endif
            coefficients = cloth.coefficients.ToArray();
        }
    }

    public class SortingGroupSaveDataValue : SaveDataValue
    {
        public int sortingOrder;
        public int sortingLayerID;
        public SortingGroupSaveDataValue(UnityEditor.SerializedObject serializedObj, PlayModeSave.SaveCommand saveCmd,
            Component component, int sortingOrder, int sortingLayerID) : base(serializedObj, saveCmd, component)
            => (this.sortingOrder, this.sortingLayerID) = (sortingOrder, sortingLayerID);

#if UNITY_6000_3_OR_NEWER
            public override void Update(EntityId componentId)
#else
        public override void Update(int componentId)
#endif
        {
            base.Update(componentId);
#if UNITY_6000_3_OR_NEWER
                var sortingGroup = UnityEditor.EditorUtility.EntityIdToObject(componentId)
                   as UnityEngine.Rendering.SortingGroup;
#else
            var sortingGroup = UnityEditor.EditorUtility.InstanceIDToObject(componentId)
                as UnityEngine.Rendering.SortingGroup;
#endif
            sortingOrder = sortingGroup.sortingOrder;
            sortingLayerID = sortingGroup.sortingLayerID;
        }
    }

    public class TilemapSaveDataValue : SaveDataValue
    {
        public UnityEngine.Tilemaps.TileBase[] tileArray;
        public BoundsInt tileBounds;
        public TilemapSaveDataValue(UnityEditor.SerializedObject serializedObj,
            PlayModeSave.SaveCommand saveCmd, Component component)
            : base(serializedObj, saveCmd, component)
        {
            var tilemap = component as UnityEngine.Tilemaps.Tilemap;
            if (tilemap == null) return;
            tileBounds = tilemap.cellBounds;
            tileArray = tilemap.GetTilesBlock(tileBounds).ToArray();
        }

#if UNITY_6000_3_OR_NEWER
            public override void Update(EntityId componentId)
#else
        public override void Update(int componentId)
#endif
        {
            base.Update(componentId);
#if UNITY_6000_3_OR_NEWER
                var tilemap = UnityEditor.EditorUtility.EntityIdToObject(componentId) as UnityEngine.Tilemaps.Tilemap;
#else
            var tilemap = UnityEditor.EditorUtility.InstanceIDToObject(componentId) as UnityEngine.Tilemaps.Tilemap;
#endif
            tileBounds = tilemap.cellBounds;
            tileArray = tilemap.GetTilesBlock(tileBounds).ToArray();
        }
    }

#if PMS_CINEMACHINE && !PMS_CINE_MACHINE_3_0_OR_NEWER
        public interface ICinamechineComponentBaseData
        {
            void SetCompValues(Cinemachine.CinemachineComponentBase target);
        }
#if PMS_CINE_MACHINE_2_6_OR_NEWER
        public struct Cinemachine3rdPersonFollowData : ICinamechineComponentBaseData
        {
            public Vector3 Damping;
            public Vector3 ShoulderOffset;
            public float VerticalArmLength;
            public float CameraSide;
            public float CameraDistance;
            public LayerMask CameraCollisionFilter;
            public string IgnoreTag;
            public float CameraRadius;
#if PMS_CINE_MACHINE_2_8_OR_NEWER
            public float DampingIntoCollision;
            public float DampingFromCollision;
#endif
            public Cinemachine3rdPersonFollowData(Cinemachine.Cinemachine3rdPersonFollow source)
            {
                Damping = source.Damping;
                ShoulderOffset = source.ShoulderOffset;
                VerticalArmLength = source.VerticalArmLength;
                CameraSide = source.CameraSide;
                CameraDistance = source.CameraDistance;
                CameraCollisionFilter = source.CameraCollisionFilter;
                IgnoreTag = source.IgnoreTag;
                CameraRadius = source.CameraRadius;
#if PMS_CINE_MACHINE_2_8_OR_NEWER
                DampingIntoCollision = source.DampingIntoCollision;
                DampingFromCollision = source.DampingFromCollision;
#endif
            }
            public void SetCompValues(Cinemachine.CinemachineComponentBase target)
            {
                var castedTarget = target as Cinemachine.Cinemachine3rdPersonFollow;
                if (castedTarget == null) return;
                castedTarget.Damping = Damping;
                castedTarget.ShoulderOffset = ShoulderOffset;
                castedTarget.VerticalArmLength = VerticalArmLength;
                castedTarget.CameraSide = CameraSide;
                castedTarget.CameraDistance = CameraDistance;
                castedTarget.CameraCollisionFilter = CameraCollisionFilter;
                castedTarget.IgnoreTag = IgnoreTag;
                castedTarget.CameraRadius = CameraRadius;
#if PMS_CINE_MACHINE_2_8_OR_NEWER
                castedTarget.DampingIntoCollision = DampingIntoCollision;
                castedTarget.DampingFromCollision = DampingFromCollision;
#endif
            }
        }
#endif
        public struct CinemachineFramingTransposerData : ICinamechineComponentBaseData
        {
#if PMS_CINE_MACHINE_2_6_1_OR_NEWER
            public Vector3 m_TrackedObjectOffset;
#endif
            public float m_LookaheadTime;
            public float m_LookaheadSmoothing;
            public bool m_LookaheadIgnoreY;
            public float m_XDamping;
            public float m_YDamping;
            public float m_ZDamping;
#if PMS_CINE_MACHINE_2_6_OR_NEWER
            public bool m_TargetMovementOnly;
#endif
            public float m_ScreenX;
            public float m_ScreenY;
            public float m_CameraDistance;
            public float m_DeadZoneWidth;
            public float m_DeadZoneHeight;
            public float m_DeadZoneDepth;
            public bool m_UnlimitedSoftZone;
            public float m_SoftZoneWidth;
            public float m_SoftZoneHeight;
            public float m_BiasX;
            public float m_BiasY;
            public bool m_CenterOnActivate;
            public Cinemachine.CinemachineFramingTransposer.FramingMode m_GroupFramingMode;
            public Cinemachine.CinemachineFramingTransposer.AdjustmentMode m_AdjustmentMode;
            public float m_GroupFramingSize;
            public float m_MaxDollyIn;
            public float m_MaxDollyOut;
            public float m_MinimumDistance;
            public float m_MaximumDistance;
            public float m_MinimumFOV;
            public float m_MaximumFOV;
            public float m_MinimumOrthoSize;
            public float m_MaximumOrthoSize;
            public CinemachineFramingTransposerData(Cinemachine.CinemachineFramingTransposer source)
            {
#if PMS_CINE_MACHINE_2_6_1_OR_NEWER
                m_TrackedObjectOffset = source.m_TrackedObjectOffset;
#endif
                m_LookaheadTime = source.m_LookaheadTime;
                m_LookaheadSmoothing = source.m_LookaheadSmoothing;
                m_LookaheadIgnoreY = source.m_LookaheadIgnoreY;
                m_XDamping = source.m_XDamping;
                m_YDamping = source.m_YDamping;
                m_ZDamping = source.m_ZDamping;
#if PMS_CINE_MACHINE_2_6_OR_NEWER
                m_TargetMovementOnly = source.m_TargetMovementOnly;
#endif
                m_ScreenX = source.m_ScreenX;
                m_ScreenY = source.m_ScreenY;
                m_CameraDistance = source.m_CameraDistance;
                m_DeadZoneWidth = source.m_DeadZoneWidth;
                m_DeadZoneHeight = source.m_DeadZoneHeight;
                m_DeadZoneDepth = source.m_DeadZoneDepth;
                m_UnlimitedSoftZone = source.m_UnlimitedSoftZone;
                m_SoftZoneWidth = source.m_SoftZoneWidth;
                m_SoftZoneHeight = source.m_SoftZoneHeight;
                m_BiasX = source.m_BiasX;
                m_BiasY = source.m_BiasY;
                m_CenterOnActivate = source.m_CenterOnActivate;
                m_GroupFramingMode = source.m_GroupFramingMode;
                m_AdjustmentMode = source.m_AdjustmentMode;
                m_GroupFramingSize = source.m_GroupFramingSize;
                m_MaxDollyIn = source.m_MaxDollyIn;
                m_MaxDollyOut = source.m_MaxDollyOut;
                m_MinimumDistance = source.m_MinimumDistance;
                m_MaximumDistance = source.m_MaximumDistance;
                m_MinimumFOV = source.m_MinimumFOV;
                m_MaximumFOV = source.m_MaximumFOV;
                m_MinimumOrthoSize = source.m_MinimumOrthoSize;
                m_MaximumOrthoSize = source.m_MaximumOrthoSize;
            }
            public void SetCompValues(Cinemachine.CinemachineComponentBase target)
            {
                var castedTarget = target as Cinemachine.CinemachineFramingTransposer;
                if (castedTarget == null) return;
#if PMS_CINE_MACHINE_2_6_1_OR_NEWER
                castedTarget.m_TrackedObjectOffset = m_TrackedObjectOffset;
#endif
                castedTarget.m_LookaheadTime = m_LookaheadTime;
                castedTarget.m_LookaheadSmoothing = m_LookaheadSmoothing;
                castedTarget.m_LookaheadIgnoreY = m_LookaheadIgnoreY;
                castedTarget.m_XDamping = m_XDamping;
                castedTarget.m_YDamping = m_YDamping;
                castedTarget.m_ZDamping = m_ZDamping;
#if PMS_CINE_MACHINE_2_6_OR_NEWER
                castedTarget.m_TargetMovementOnly = m_TargetMovementOnly;
#endif
                castedTarget.m_ScreenX = m_ScreenX;
                castedTarget.m_ScreenY = m_ScreenY;
                castedTarget.m_CameraDistance = m_CameraDistance;
                castedTarget.m_DeadZoneWidth = m_DeadZoneWidth;
                castedTarget.m_DeadZoneHeight = m_DeadZoneHeight;
                castedTarget.m_DeadZoneDepth = m_DeadZoneDepth;
                castedTarget.m_UnlimitedSoftZone = m_UnlimitedSoftZone;
                castedTarget.m_SoftZoneWidth = m_SoftZoneWidth;
                castedTarget.m_SoftZoneHeight = m_SoftZoneHeight;
                castedTarget.m_BiasX = m_BiasX;
                castedTarget.m_BiasY = m_BiasY;
                castedTarget.m_CenterOnActivate = m_CenterOnActivate;
                castedTarget.m_GroupFramingMode = m_GroupFramingMode;
                castedTarget.m_AdjustmentMode = m_AdjustmentMode;
                castedTarget.m_GroupFramingSize = m_GroupFramingSize;
                castedTarget.m_MaxDollyIn = m_MaxDollyIn;
                castedTarget.m_MaxDollyOut = m_MaxDollyOut;
                castedTarget.m_MinimumDistance = m_MinimumDistance;
                castedTarget.m_MaximumDistance = m_MaximumDistance;
                castedTarget.m_MinimumFOV = m_MinimumFOV;
                castedTarget.m_MaximumFOV = m_MaximumFOV;
                castedTarget.m_MinimumOrthoSize = m_MinimumOrthoSize;
                castedTarget.m_MaximumOrthoSize = m_MaximumOrthoSize;
            }
        }
        public struct CinemachineHardLockToTargetData : ICinamechineComponentBaseData
        {
            public float m_Damping;
            public CinemachineHardLockToTargetData(Cinemachine.CinemachineHardLockToTarget source)
            {
                m_Damping = source.m_Damping;
            }
            public void SetCompValues(Cinemachine.CinemachineComponentBase target)
            {
                var castedTarget = target as Cinemachine.CinemachineHardLockToTarget;
                if (castedTarget == null) return;
                castedTarget.m_Damping = m_Damping;
            }
        }
        public struct CinemachineOrbitalTransposerData : ICinamechineComponentBaseData
        {
            public Cinemachine.CinemachineTransposer.BindingMode m_BindingMode;
            public Vector3 m_FollowOffset;
            public float m_XDamping;
            public float m_YDamping;
            public float m_ZDamping;
            public Cinemachine.CinemachineTransposer.AngularDampingMode m_AngularDampingMode;
            public float m_PitchDamping;
            public float m_YawDamping;
            public float m_RollDamping;
            public float m_AngularDamping;

            public Cinemachine.CinemachineOrbitalTransposer.Heading m_Heading;
            public Cinemachine.AxisState.Recentering m_RecenterToTargetHeading;
            public Cinemachine.AxisState m_XAxis;
            public CinemachineOrbitalTransposerData(Cinemachine.CinemachineOrbitalTransposer source)
            {
                m_BindingMode = source.m_BindingMode;
                m_FollowOffset = source.m_FollowOffset;
                m_XDamping = source.m_XDamping;
                m_YDamping = source.m_YDamping;
                m_ZDamping = source.m_ZDamping;
                m_AngularDampingMode = source.m_AngularDampingMode;
                m_PitchDamping = source.m_PitchDamping;
                m_YawDamping = source.m_YawDamping;
                m_RollDamping = source.m_RollDamping;
                m_AngularDamping = source.m_AngularDamping;
                m_Heading = source.m_Heading;
                m_RecenterToTargetHeading = source.m_RecenterToTargetHeading;
                m_XAxis = source.m_XAxis;
            }
            public void SetCompValues(Cinemachine.CinemachineComponentBase target)
            {
                var castedTarget = target as Cinemachine.CinemachineOrbitalTransposer;
                if (castedTarget == null) return;
                castedTarget.m_BindingMode = m_BindingMode;
                castedTarget.m_FollowOffset = m_FollowOffset;
                castedTarget.m_XDamping = m_XDamping;
                castedTarget.m_YDamping = m_YDamping;
                castedTarget.m_ZDamping = m_ZDamping;
                castedTarget.m_AngularDampingMode = m_AngularDampingMode;
                castedTarget.m_PitchDamping = m_PitchDamping;
                castedTarget.m_YawDamping = m_YawDamping;
                castedTarget.m_RollDamping = m_RollDamping;
                castedTarget.m_AngularDamping = m_AngularDamping;
                castedTarget.m_Heading = m_Heading;
                castedTarget.m_RecenterToTargetHeading = m_RecenterToTargetHeading;
                castedTarget.m_XAxis = m_XAxis;
            }
        }
        public struct CinemachineTrackedDollyData : ICinamechineComponentBaseData
        {
            public Cinemachine.CinemachinePathBase m_Path;
            public float m_PathPosition;
            public Cinemachine.CinemachinePathBase.PositionUnits m_PositionUnits;
            public Vector3 m_PathOffset;
            public float m_XDamping;
            public float m_YDamping;
            public float m_ZDamping;
            public Cinemachine.CinemachineTrackedDolly.CameraUpMode m_CameraUp;
            public float m_PitchDamping;
            public float m_YawDamping;
            public float m_RollDamping;
            public Cinemachine.CinemachineTrackedDolly.AutoDolly m_AutoDolly;
            public CinemachineTrackedDollyData(Cinemachine.CinemachineTrackedDolly source)
            {
                m_Path = source.m_Path;
                m_PathPosition = source.m_PathPosition;
                m_PositionUnits = source.m_PositionUnits;
                m_PathOffset = source.m_PathOffset;
                m_XDamping = source.m_XDamping;
                m_YDamping = source.m_YDamping;
                m_ZDamping = source.m_ZDamping;
                m_CameraUp = source.m_CameraUp;
                m_PitchDamping = source.m_PitchDamping;
                m_YawDamping = source.m_YawDamping;
                m_RollDamping = source.m_RollDamping;
                m_AutoDolly = source.m_AutoDolly;
            }

            public void SetCompValues(Cinemachine.CinemachineComponentBase target)
            {
                var castedTarget = target as Cinemachine.CinemachineTrackedDolly;
                if (castedTarget == null) return;
                castedTarget.m_Path = m_Path;
                castedTarget.m_PathPosition = m_PathPosition;
                castedTarget.m_PositionUnits = m_PositionUnits;
                castedTarget.m_PathOffset = m_PathOffset;
                castedTarget.m_XDamping = m_XDamping;
                castedTarget.m_YDamping = m_YDamping;
                castedTarget.m_ZDamping = m_ZDamping;
                castedTarget.m_CameraUp = m_CameraUp;
                castedTarget.m_PitchDamping = m_PitchDamping;
                castedTarget.m_YawDamping = m_YawDamping;
                castedTarget.m_RollDamping = m_RollDamping;
                castedTarget.m_AutoDolly = m_AutoDolly;
            }
        }
        public struct CinemachineTransposerData : ICinamechineComponentBaseData
        {
            public Cinemachine.CinemachineTransposer.BindingMode m_BindingMode;
            public Vector3 m_FollowOffset;
            public float m_XDamping;
            public float m_YDamping;
            public float m_ZDamping;
            public Cinemachine.CinemachineTransposer.AngularDampingMode m_AngularDampingMode;
            public float m_PitchDamping;
            public float m_YawDamping;
            public float m_RollDamping;
            public float m_AngularDamping;

            public CinemachineTransposerData(Cinemachine.CinemachineTransposer source)
            {
                m_BindingMode = source.m_BindingMode;
                m_FollowOffset = source.m_FollowOffset;
                m_XDamping = source.m_XDamping;
                m_YDamping = source.m_YDamping;
                m_ZDamping = source.m_ZDamping;
                m_AngularDampingMode = source.m_AngularDampingMode;
                m_PitchDamping = source.m_PitchDamping;
                m_YawDamping = source.m_YawDamping;
                m_RollDamping = source.m_RollDamping;
                m_AngularDamping = source.m_AngularDamping;
            }
            public void SetCompValues(Cinemachine.CinemachineComponentBase target)
            {
                var castedTarget = target as Cinemachine.CinemachineTransposer;
                if (castedTarget == null) return;
                castedTarget.m_BindingMode = m_BindingMode;
                castedTarget.m_FollowOffset = m_FollowOffset;
                castedTarget.m_XDamping = m_XDamping;
                castedTarget.m_YDamping = m_YDamping;
                castedTarget.m_ZDamping = m_ZDamping;
                castedTarget.m_AngularDampingMode = m_AngularDampingMode;
                castedTarget.m_PitchDamping = m_PitchDamping;
                castedTarget.m_YawDamping = m_YawDamping;
                castedTarget.m_RollDamping = m_RollDamping;
                castedTarget.m_AngularDamping = m_AngularDamping;
            }
        }
        public class CinemachineComposerData : ICinamechineComponentBaseData
        {
            public Vector3 m_TrackedObjectOffset;
            public float m_LookaheadTime;
            public float m_LookaheadSmoothing;
            public bool m_LookaheadIgnoreY;
            public float m_HorizontalDamping;
            public float m_VerticalDamping;
            public float m_ScreenX;
            public float m_ScreenY;
            public float m_DeadZoneWidth;
            public float m_DeadZoneHeight;
            public float m_SoftZoneWidth;
            public float m_SoftZoneHeight;
            public float m_BiasX;
            public float m_BiasY;
            public bool m_CenterOnActivate;

            protected void Initialize(Cinemachine.CinemachineComposer source)
            {
                m_TrackedObjectOffset = source.m_TrackedObjectOffset;
                m_LookaheadTime = source.m_LookaheadTime;
                m_LookaheadSmoothing = source.m_LookaheadSmoothing;
                m_LookaheadIgnoreY = source.m_LookaheadIgnoreY;
                m_HorizontalDamping = source.m_HorizontalDamping;
                m_VerticalDamping = source.m_VerticalDamping;
                m_ScreenX = source.m_ScreenX;
                m_ScreenY = source.m_ScreenY;
                m_DeadZoneWidth = source.m_DeadZoneWidth;
                m_DeadZoneHeight = source.m_DeadZoneHeight;
                m_SoftZoneWidth = source.m_SoftZoneWidth;
                m_SoftZoneHeight = source.m_SoftZoneHeight;
                m_BiasX = source.m_BiasX;
                m_BiasY = source.m_BiasY;
                m_CenterOnActivate = source.m_CenterOnActivate;
            }
            public CinemachineComposerData(Cinemachine.CinemachineComposer source) => Initialize(source);
            public virtual void SetCompValues(Cinemachine.CinemachineComponentBase target)
            {
                var castedTarget = target as Cinemachine.CinemachineComposer;
                if (castedTarget == null) return;
                castedTarget.m_TrackedObjectOffset = m_TrackedObjectOffset;
                castedTarget.m_LookaheadTime = m_LookaheadTime;
                castedTarget.m_LookaheadSmoothing = m_LookaheadSmoothing;
                castedTarget.m_LookaheadIgnoreY = m_LookaheadIgnoreY;
                castedTarget.m_HorizontalDamping = m_HorizontalDamping;
                castedTarget.m_VerticalDamping = m_VerticalDamping;
                castedTarget.m_ScreenX = m_ScreenX;
                castedTarget.m_ScreenY = m_ScreenY;
                castedTarget.m_DeadZoneWidth = m_DeadZoneWidth;
                castedTarget.m_DeadZoneHeight = m_DeadZoneHeight;
                castedTarget.m_SoftZoneWidth = m_SoftZoneWidth;
                castedTarget.m_SoftZoneHeight = m_SoftZoneHeight;
                castedTarget.m_BiasX = m_BiasX;
                castedTarget.m_BiasY = m_BiasY;
                castedTarget.m_CenterOnActivate = m_CenterOnActivate;
            }
        }
        public class CinemachineGroupComposerData : CinemachineComposerData
        {
            public float m_GroupFramingSize;
            public Cinemachine.CinemachineGroupComposer.FramingMode m_FramingMode;
            public float m_FrameDamping;
            public Cinemachine.CinemachineGroupComposer.AdjustmentMode m_AdjustmentMode;
            public float m_MaxDollyIn;
            public float m_MaxDollyOut;
            public float m_MinimumDistance;
            public float m_MinimumFOV;
            public float m_MaximumFOV;
            public float m_MinimumOrthoSize;
            public float m_MaximumOrthoSize;
            public CinemachineGroupComposerData(Cinemachine.CinemachineGroupComposer source) : base(source)
            {
                m_GroupFramingSize = source.m_GroupFramingSize;
                m_FramingMode = source.m_FramingMode;
                m_FrameDamping = source.m_FrameDamping;
                m_AdjustmentMode = source.m_AdjustmentMode;
                m_MaxDollyIn = source.m_MaxDollyIn;
                m_MaxDollyOut = source.m_MaxDollyOut;
                m_MinimumDistance = source.m_MinimumDistance;
                m_MinimumFOV = source.m_MinimumFOV;
                m_MaximumFOV = source.m_MaximumFOV;
                m_MinimumOrthoSize = source.m_MinimumOrthoSize;
                m_MaximumOrthoSize = source.m_MaximumOrthoSize;
            }

            public override void SetCompValues(Cinemachine.CinemachineComponentBase target)
            {
                var castedTarget = target as Cinemachine.CinemachineGroupComposer;
                if (castedTarget == null) return;
                base.SetCompValues(target);
                castedTarget.m_GroupFramingSize = m_GroupFramingSize;
                castedTarget.m_FramingMode = m_FramingMode;
                castedTarget.m_FrameDamping = m_FrameDamping;
                castedTarget.m_AdjustmentMode = m_AdjustmentMode;
                castedTarget.m_MaxDollyIn = m_MaxDollyIn;
                castedTarget.m_MaxDollyOut = m_MaxDollyOut;
                castedTarget.m_MinimumDistance = m_MinimumDistance;
                castedTarget.m_MinimumFOV = m_MinimumFOV;
                castedTarget.m_MaximumFOV = m_MaximumFOV;
                castedTarget.m_MinimumOrthoSize = m_MinimumOrthoSize;
                castedTarget.m_MaximumOrthoSize = m_MaximumOrthoSize;
            }
        }
        public struct CinemachinePOVData : ICinamechineComponentBaseData
        {
            public Cinemachine.CinemachinePOV.RecenterTargetMode m_RecenterTarget;
            public Cinemachine.AxisState m_VerticalAxis;
            public Cinemachine.AxisState.Recentering m_VerticalRecentering;
            public Cinemachine.AxisState m_HorizontalAxis;
            public Cinemachine.AxisState.Recentering m_HorizontalRecentering;

            public CinemachinePOVData(Cinemachine.CinemachinePOV source)
            {
                m_RecenterTarget = source.m_RecenterTarget;
                m_VerticalAxis = source.m_VerticalAxis;
                m_VerticalRecentering = source.m_VerticalRecentering;
                m_HorizontalAxis = source.m_HorizontalAxis;
                m_HorizontalRecentering = source.m_HorizontalRecentering;
            }
            public void SetCompValues(Cinemachine.CinemachineComponentBase target)
            {
                var castedTarget = target as Cinemachine.CinemachinePOV;
                if (castedTarget == null) return;
                castedTarget.m_RecenterTarget = m_RecenterTarget;
                castedTarget.m_VerticalAxis = m_VerticalAxis;
                castedTarget.m_VerticalRecentering = m_VerticalRecentering;
                castedTarget.m_HorizontalAxis = m_HorizontalAxis;
                castedTarget.m_HorizontalRecentering = m_HorizontalRecentering;
            }
        }
        public struct CinemachineSameAsFollowTargetData : ICinamechineComponentBaseData
        {
            public float m_Damping;
            public CinemachineSameAsFollowTargetData(Cinemachine.CinemachineSameAsFollowTarget source)
            {
                m_Damping = source.m_Damping;
            }
            public void SetCompValues(Cinemachine.CinemachineComponentBase target)
            {
                var castedTarget = target as Cinemachine.CinemachineSameAsFollowTarget;
                if (castedTarget == null) return;
                castedTarget.m_Damping = m_Damping;
            }
        }
        public struct CinemachineBasicMultiChannelPerlinData : ICinamechineComponentBaseData
        {
            public Cinemachine.NoiseSettings m_NoiseProfile;
            public Vector3 m_PivotOffset;
            public float m_AmplitudeGain;
            public float m_FrequencyGain;
            public CinemachineBasicMultiChannelPerlinData(Cinemachine.CinemachineBasicMultiChannelPerlin source)
            {
                m_NoiseProfile = source.m_NoiseProfile;
                m_PivotOffset = source.m_PivotOffset;
                m_AmplitudeGain = source.m_AmplitudeGain;
                m_FrequencyGain = source.m_FrequencyGain;
            }
            public void SetCompValues(Cinemachine.CinemachineComponentBase target)
            {
                var castedTarget = target as Cinemachine.CinemachineBasicMultiChannelPerlin;
                if (castedTarget == null) return;
                castedTarget.m_NoiseProfile = m_NoiseProfile;
                castedTarget.m_PivotOffset = m_PivotOffset;
                castedTarget.m_AmplitudeGain = m_AmplitudeGain;
                castedTarget.m_FrequencyGain = m_FrequencyGain;
            }
        }
        public class CinemachineVirtualCameraSaveDataValue : SaveDataValue
        {
            public ICinamechineComponentBaseData[] CMCompData;
            public CinemachineVirtualCameraSaveDataValue(UnityEditor.SerializedObject serializedObj,
            PlayModeSave.SaveCommand saveCmd,
                Component component, ICinamechineComponentBaseData[] CMCompData)
                : base(serializedObj, saveCmd, component) => this.CMCompData = CMCompData;
#if PMS_CINE_MACHINE_2_6_OR_NEWER
            private bool GetCompData(Cinemachine.CinemachineVirtualCamera CMVCam, out Cinemachine3rdPersonFollowData compData)
            {
                compData = new Cinemachine3rdPersonFollowData();
                var CMComp = CMVCam.GetCinemachineComponent<Cinemachine.Cinemachine3rdPersonFollow>();
                if (CMComp == null) return false;
                compData = new Cinemachine3rdPersonFollowData(CMComp);
                return true;
            }

            public bool GetCompData(out Cinemachine3rdPersonFollowData compData)
            {
                compData = new Cinemachine3rdPersonFollowData();
                foreach (var data in CMCompData)
                    if (data is Cinemachine3rdPersonFollowData)
                    {
                        compData = (Cinemachine3rdPersonFollowData)data;
                        return true;
                    }
                return false;
            }
#endif
            private bool GetCompData(Cinemachine.CinemachineVirtualCamera CMVCam,
                out CinemachineFramingTransposerData compData)
            {
                compData = new CinemachineFramingTransposerData();
                var CMComp = CMVCam.GetCinemachineComponent<Cinemachine.CinemachineFramingTransposer>();
                if (CMComp == null) return false;
                compData = new CinemachineFramingTransposerData(CMComp);
                return true;
            }
            public bool GetCompData(out CinemachineFramingTransposerData compData)
            {
                compData = new CinemachineFramingTransposerData();
                foreach (var data in CMCompData)
                    if (data is CinemachineFramingTransposerData)
                    {
                        compData = (CinemachineFramingTransposerData)data;
                        return true;
                    }
                return false;
            }
            private bool GetCompData(Cinemachine.CinemachineVirtualCamera CMVCam,
                out CinemachineHardLockToTargetData compData)
            {
                compData = new CinemachineHardLockToTargetData();
                var CMComp = CMVCam.GetCinemachineComponent<Cinemachine.CinemachineHardLockToTarget>();
                if (CMComp == null) return false;
                compData = new CinemachineHardLockToTargetData(CMComp);
                return true;
            }
            public bool GetCompData(out CinemachineHardLockToTargetData compData)
            {
                compData = new CinemachineHardLockToTargetData();
                foreach (var data in CMCompData)
                    if (data is CinemachineHardLockToTargetData)
                    {
                        compData = (CinemachineHardLockToTargetData)data;
                        return true;
                    }
                return false;
            }
            private bool GetCompData(Cinemachine.CinemachineVirtualCamera CMVCam,
                out CinemachineOrbitalTransposerData compData)
            {
                compData = new CinemachineOrbitalTransposerData();
                var CMComp = CMVCam.GetCinemachineComponent<Cinemachine.CinemachineOrbitalTransposer>();
                if (CMComp == null) return false;
                compData = new CinemachineOrbitalTransposerData(CMComp);
                return true;
            }
            public bool GetCompData(out CinemachineOrbitalTransposerData compData)
            {
                compData = new CinemachineOrbitalTransposerData();
                foreach (var data in CMCompData)
                    if (data is CinemachineOrbitalTransposerData)
                    {
                        compData = (CinemachineOrbitalTransposerData)data;
                        return true;
                    }
                return false;
            }
            private bool GetCompData(Cinemachine.CinemachineVirtualCamera CMVCam, out CinemachineTrackedDollyData compData)
            {
                compData = new CinemachineTrackedDollyData();
                var CMComp = CMVCam.GetCinemachineComponent<Cinemachine.CinemachineTrackedDolly>();
                if (CMComp == null) return false;
                compData = new CinemachineTrackedDollyData(CMComp);
                return true;
            }
            public bool GetCompData(out CinemachineTrackedDollyData compData)
            {
                compData = new CinemachineTrackedDollyData();
                foreach (var data in CMCompData)
                    if (data is CinemachineTrackedDollyData)
                    {
                        compData = (CinemachineTrackedDollyData)data;
                        return true;
                    }
                return false;
            }
            private bool GetCompData(Cinemachine.CinemachineVirtualCamera CMVCam, out CinemachineTransposerData compData)
            {
                compData = new CinemachineTransposerData();
                var CMComp = CMVCam.GetCinemachineComponent<Cinemachine.CinemachineTransposer>();
                if (CMComp == null) return false;
                compData = new CinemachineTransposerData(CMComp);
                return true;
            }
            public bool GetCompData(out CinemachineTransposerData compData)
            {
                compData = new CinemachineTransposerData();
                foreach (var data in CMCompData)
                    if (data is CinemachineTransposerData)
                    {
                        compData = (CinemachineTransposerData)data;
                        return true;
                    }
                return false;
            }
            private bool GetCompData(Cinemachine.CinemachineVirtualCamera CMVCam, out CinemachineComposerData compData)
            {
                compData = null;
                var CMComp = CMVCam.GetCinemachineComponent<Cinemachine.CinemachineComposer>();
                if (CMComp == null) return false;
                compData = new CinemachineComposerData(CMComp);
                return true;
            }
            public bool GetCompData(out CinemachineComposerData compData)
            {
                compData = null;
                foreach (var data in CMCompData)
                    if (data is CinemachineComposerData)
                    {
                        compData = (CinemachineComposerData)data;
                        return true;
                    }
                return false;
            }
            private bool GetCompData(Cinemachine.CinemachineVirtualCamera CMVCam, out CinemachineGroupComposerData compData)
            {
                compData = null;
                var CMComp = CMVCam.GetCinemachineComponent<Cinemachine.CinemachineGroupComposer>();
                if (CMComp == null) return false;
                compData = new CinemachineGroupComposerData(CMComp);
                return true;
            }
            public bool GetCompData(out CinemachineGroupComposerData compData)
            {
                compData = null;
                foreach (var data in CMCompData)
                    if (data is CinemachineGroupComposerData)
                    {
                        compData = (CinemachineGroupComposerData)data;
                        return true;
                    }
                return false;
            }
            private bool GetCompData(Cinemachine.CinemachineVirtualCamera CMVCam, out CinemachinePOVData compData)
            {
                compData = new CinemachinePOVData();
                var CMComp = CMVCam.GetCinemachineComponent<Cinemachine.CinemachinePOV>();
                if (CMComp == null) return false;
                compData = new CinemachinePOVData(CMComp);
                return true;
            }
            public bool GetCompData(out CinemachinePOVData compData)
            {
                compData = new CinemachinePOVData();
                foreach (var data in CMCompData)
                    if (data is CinemachinePOVData)
                    {
                        compData = (CinemachinePOVData)data;
                        return true;
                    }
                return false;
            }
            private bool GetCompData(Cinemachine.CinemachineVirtualCamera CMVCam,
                out CinemachineSameAsFollowTargetData compData)
            {
                compData = new CinemachineSameAsFollowTargetData();
                var CMComp = CMVCam.GetCinemachineComponent<Cinemachine.CinemachineSameAsFollowTarget>();
                if (CMComp == null) return false;
                compData = new CinemachineSameAsFollowTargetData(CMComp);
                return true;
            }
            public bool GetCompData(out CinemachineSameAsFollowTargetData compData)
            {
                compData = new CinemachineSameAsFollowTargetData();
                foreach (var data in CMCompData)
                    if (data is CinemachineSameAsFollowTargetData)
                    {
                        compData = (CinemachineSameAsFollowTargetData)data;
                        return true;
                    }
                return false;
            }
            private bool GetCompData(Cinemachine.CinemachineVirtualCamera CMVCam,
                out CinemachineBasicMultiChannelPerlinData compData)
            {
                compData = new CinemachineBasicMultiChannelPerlinData();
                var CMComp = CMVCam.GetCinemachineComponent<Cinemachine.CinemachineBasicMultiChannelPerlin>();
                if (CMComp == null) return false;
                compData = new CinemachineBasicMultiChannelPerlinData(CMComp);
                return true;
            }
            public bool GetCompData(out CinemachineBasicMultiChannelPerlinData compData)
            {
                compData = new CinemachineBasicMultiChannelPerlinData();
                foreach (var data in CMCompData)
                    if (data is CinemachineBasicMultiChannelPerlinData)
                    {
                        compData = (CinemachineBasicMultiChannelPerlinData)data;
                        return true;
                    }
                return false;
            }
#if UNITY_6000_3_OR_NEWER
            public override void Update(EntityId componentId)
#else
            public override void Update(int componentId)
#endif
            {
                base.Update(componentId);
#if UNITY_6000_3_OR_NEWER
                var CMVCam = UnityEditor.EditorUtility.EntityIdToObject(componentId)
                    as Cinemachine.CinemachineVirtualCamera;
#else
                var CMVCam = UnityEditor.EditorUtility.InstanceIDToObject(componentId)
                    as Cinemachine.CinemachineVirtualCamera;
#endif
                System.Collections.Generic.List<ICinamechineComponentBaseData> compDataList
                    = new System.Collections.Generic.List<ICinamechineComponentBaseData>();
#if PMS_CINE_MACHINE_2_6_OR_NEWER
                var CM3PF = CMVCam.GetCinemachineComponent<Cinemachine.Cinemachine3rdPersonFollow>();
                if (CM3PF != null)
                    if (GetCompData(CMVCam, out Cinemachine3rdPersonFollowData compData)) compDataList.Add(compData);
#endif
                var CMFT = CMVCam.GetCinemachineComponent<Cinemachine.CinemachineFramingTransposer>();
                if (CMFT != null)
                    if (GetCompData(CMVCam, out CinemachineFramingTransposerData compData)) compDataList.Add(compData);

                var CMHLT = CMVCam.GetCinemachineComponent<Cinemachine.CinemachineHardLockToTarget>();
                if (CMHLT != null)
                    if (GetCompData(CMVCam, out CinemachineHardLockToTargetData compData)) compDataList.Add(compData);

                var CMOT = CMVCam.GetCinemachineComponent<Cinemachine.CinemachineOrbitalTransposer>();
                if (CMOT != null)
                    if (GetCompData(CMVCam, out CinemachineOrbitalTransposerData compData)) compDataList.Add(compData);

                var CMTD = CMVCam.GetCinemachineComponent<Cinemachine.CinemachineTrackedDolly>();
                if (CMTD != null)
                    if (GetCompData(CMVCam, out CinemachineTrackedDollyData compData)) compDataList.Add(compData);

                var CMT = CMVCam.GetCinemachineComponent<Cinemachine.CinemachineTransposer>();
                if (CMT != null)
                    if (GetCompData(CMVCam, out CinemachineTransposerData compData)) compDataList.Add(compData);

                var CMC = CMVCam.GetCinemachineComponent<Cinemachine.CinemachineComposer>();
                if (CMC != null)
                    if (GetCompData(CMVCam, out CinemachineComposerData compData)) compDataList.Add(compData);

                var CMGC = CMVCam.GetCinemachineComponent<Cinemachine.CinemachineGroupComposer>();
                if (CMGC != null)
                    if (GetCompData(CMVCam, out CinemachineGroupComposerData compData)) compDataList.Add(compData);

                var CMPOV = CMVCam.GetCinemachineComponent<Cinemachine.CinemachinePOV>();
                if (CMPOV != null)
                    if (GetCompData(CMVCam, out CinemachinePOVData compData)) compDataList.Add(compData);

                var CMSAFT = CMVCam.GetCinemachineComponent<Cinemachine.CinemachineSameAsFollowTarget>();
                if (CMSAFT != null)
                    if (GetCompData(CMVCam, out CinemachineSameAsFollowTargetData compData)) compDataList.Add(compData);

                var CMBMCP = CMVCam.GetCinemachineComponent<Cinemachine.CinemachineBasicMultiChannelPerlin>();
                if (CMBMCP != null)
                    if (GetCompData(CMVCam, out CinemachineBasicMultiChannelPerlinData compData)) compDataList.Add(compData);
                CMCompData = compDataList.ToArray();
            }
        }
#endif

}
#pragma warning restore UDR0001
