using System.Collections.Generic;
using UnityEngine;

namespace Spine.Unity.Examples
{
    public class SpineboyTargetController : MonoBehaviour
    {
        private const float MinCameraDepth = 0.01f;
        private const float MinArmLength = 0.0001f;
        private const float MinVectorSqrLength = 1e-4f;
        private const float MinGunCorrection = 0.01f;
        private const float MinGunWeight = 0.01f;
        private const float MaxToleranceDegrees = 9f;
        private const float FullyExtendedThreshold = 0.999f;

        [Header("References")]
        public SkeletonAnimation skeletonAnimation;
        public SpineboyBeginnerModel model;
        public SpineboyBeginnerView view;
        [SpineBone(dataField: "skeletonAnimation")]
        public string boneName = "crosshair";
        public Camera cam;

        [Header("High Aim (extended arm)")]
        [Min(0f)] public float characterHeightOverride;
        [Min(0.1f)] public float aimHeightMultiplier = 2f;
        [Range(0f, 0.3f)] public float heightHysteresisFraction = 0.03f;

        [Header("Bent Arm (reach limit)")]
        public bool limitReachWhenBent = true;
        [SpineBone(dataField: "skeletonAnimation")]
        public string shoulderBoneName = "front-upper-arm";
        [SpineBone(dataField: "skeletonAnimation")]
        public string forearmBoneName = "front-bracer";
        [Range(0.2f, 0.95f)] public float bentReachFraction = 0.65f;

        [Header("Gun Direction Correction")]
        public bool correctGunDirection = true;
        [SpineBone(dataField: "skeletonAnimation")]
        public string gunBoneName = "gun";
        [SpineBone(dataField: "skeletonAnimation")]
        public string barrelBoneName = "gun-tip";
        [Range(0f, 9f)] public float gunToleranceDegrees = 3f;

        [Header("Crosshair")]
        public bool hideCrosshair = true;

        [Header("Debug")]
        public bool logHeightOnStart = true;
        public bool showHeightLine = false;
        public string[] heightIgnoreSlotNameParts = new string[] { "crosshair", "portal", "muzzle" };

        private Bone _bone;
        private Bone _gunBone;
        private Bone _barrelBone;
        private Bone _shoulderBone;
        private Bone _forearmBone;
        private Slot[] _crosshairSlots;

        private Vector2 _cursorWorld;
        private bool _hasCursor;
        private bool _warnedArm;
        private bool _warnedGun;
        private float _gunBaseRotation;
        private bool _gunRotationModified;
        private float _cachedCharacterHeight;

        public float CharacterHeight => GetCharacterHeight();
        public float CursorHeightInCharacters { get; private set; }

        private float HeightMultiplier => model.movementSettings != null
            ? model.movementSettings.aimExtendedHeightMultiplier
            : aimHeightMultiplier;

        private float HysteresisFraction => model.movementSettings != null
            ? model.movementSettings.aimHeightHysteresisFraction
            : heightHysteresisFraction;

        private float ExtendedBlend
        {
            get
            {
                if (view != null) return view.AimExtendedWeight;
                return model != null && model.IsAimExtended ? 1f : 0f;
            }
        }

        private void OnValidate()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            if (skeletonAnimation == null) return;

            skeletonAnimation.UpdateLocal -= HandleUpdateLocal;
            skeletonAnimation.UpdateLocal += HandleUpdateLocal;
            skeletonAnimation.UpdateWorld -= HandleUpdateWorld;
            skeletonAnimation.UpdateWorld += HandleUpdateWorld;
            skeletonAnimation.UpdateComplete -= HandleUpdateComplete;
            skeletonAnimation.UpdateComplete += HandleUpdateComplete;
        }

        private void Start()
        {
            ResolveBones();
            if (logHeightOnStart && model != null) LogHeight();
        }

        private void OnDisable()
        {
            if (skeletonAnimation != null)
            {
                skeletonAnimation.UpdateLocal -= HandleUpdateLocal;
                skeletonAnimation.UpdateWorld -= HandleUpdateWorld;
                skeletonAnimation.UpdateComplete -= HandleUpdateComplete;
            }

            _bone = null;
            _gunBone = null;
            _barrelBone = null;
            _shoulderBone = null;
            _forearmBone = null;
        }

        private void ResolveReferences()
        {
            if (skeletonAnimation == null) skeletonAnimation = GetComponent<SkeletonAnimation>();
            if (model == null) model = GetComponent<SpineboyBeginnerModel>();
            if (view == null) view = GetComponent<SpineboyBeginnerView>();
            if (cam == null && model != null) cam = model.movementCamera;
            if (cam == null) cam = Camera.main;
        }

        private void ResolveBones()
        {
            if (skeletonAnimation == null || skeletonAnimation.Skeleton == null)
            {
                _bone = null;
                return;
            }

            Skeleton skeleton = skeletonAnimation.Skeleton;
            _bone = FindBone(skeleton, boneName);
            _gunBone = FindBone(skeleton, gunBoneName);
            _barrelBone = FindBone(skeleton, barrelBoneName);
            _shoulderBone = FindBone(skeleton, shoulderBoneName);
            _forearmBone = FindBone(skeleton, forearmBoneName);
            CacheCrosshairSlots(skeleton);

            if (_bone == null)
                Debug.LogWarning("SpineboyTargetController: target bone '" + boneName + "' was not found in the current skeleton.", this);

            WarnAboutMissingArmBones();
            WarnAboutMissingGunBones();
        }

        private static Bone FindBone(Skeleton skeleton, string name)
        {
            return string.IsNullOrEmpty(name) ? null : skeleton.FindBone(name);
        }

        private void WarnAboutMissingArmBones()
        {
            if (_warnedArm || !limitReachWhenBent) return;
            if (_shoulderBone != null && _forearmBone != null) return;

            _warnedArm = true;
            Debug.LogWarning("SpineboyTargetController: bent-arm reach limit disabled, bone '" + shoulderBoneName + "' or '" + forearmBoneName + "' not found.", this);
        }

        private void WarnAboutMissingGunBones()
        {
            if (_warnedGun || !correctGunDirection) return;
            if (_gunBone != null && _barrelBone != null) return;

            _warnedGun = true;
            Debug.LogWarning("SpineboyTargetController: gun correction disabled, bone '" + gunBoneName + "' or '" + barrelBoneName + "' not found.", this);
        }

        private void CacheCrosshairSlots(Skeleton skeleton)
        {
            List<Slot> slots = new List<Slot>();
            if (_bone != null)
            {
                foreach (Slot slot in skeleton.Slots)
                {
                    if (IsSameOrDescendant(slot.Bone, _bone)) slots.Add(slot);
                }
            }
            _crosshairSlots = slots.ToArray();
        }

        private void HideCrosshair()
        {
            if (!hideCrosshair || _crosshairSlots == null) return;

            for (int i = 0; i < _crosshairSlots.Length; i++)
            {
                if (_crosshairSlots[i].Attachment != null) _crosshairSlots[i].Attachment = null;
            }
        }

        private void HandleUpdateLocal(ISkeletonAnimation animated)
        {
            if (skeletonAnimation == null || skeletonAnimation.Skeleton == null) return;
            if (_bone == null) ResolveBones();

            HideCrosshair();
            if (_bone == null || cam == null) return;

            Skeleton skeleton = skeletonAnimation.Skeleton;
            Vector3 cursorWorld = GetCursorWorldPosition();
            Vector3 skeletonSpace = skeletonAnimation.transform.InverseTransformPoint(cursorWorld);

            _cursorWorld = new Vector2(skeletonSpace.x, skeletonSpace.y);
            _hasCursor = true;
            UpdateAimHeight(cursorWorld.y);

            if (IsCrosshairLocked()) return;

            Vector2 boneSpaceTarget = new Vector2(skeletonSpace.x * skeleton.ScaleX, skeletonSpace.y * skeleton.ScaleY);
            boneSpaceTarget = LimitReachWhenBent(boneSpaceTarget, skeleton);
            _bone.X = boneSpaceTarget.x;
            _bone.Y = boneSpaceTarget.y;

            if (_gunBone != null) _gunBaseRotation = _gunBone.Rotation;
        }

        private Vector3 GetCursorWorldPosition()
        {
            Vector3 mouse = Input.mousePosition;
            float depth = Mathf.Max(MinCameraDepth, Vector3.Dot(transform.position - cam.transform.position, cam.transform.forward));
            return cam.ScreenToWorldPoint(new Vector3(mouse.x, mouse.y, depth));
        }

        private bool IsCrosshairLocked()
        {
            return view != null && view.IsTurning && model != null && !model.IsAiming;
        }

        private Vector2 LimitReachWhenBent(Vector2 target, Skeleton skeleton)
        {
            if (!limitReachWhenBent || _shoulderBone == null || _forearmBone == null) return target;

            float armLength = _shoulderBone.Data.Length + _forearmBone.Data.Length;
            if (armLength <= MinArmLength) return target;

            float extended = ExtendedBlend;
            if (extended >= FullyExtendedThreshold) return target;

            Vector2 shoulder = new Vector2(_shoulderBone.WorldX * skeleton.ScaleX, _shoulderBone.WorldY * skeleton.ScaleY);
            Vector2 offset = target - shoulder;
            float distance = offset.magnitude;
            float reach = armLength * bentReachFraction;
            if (distance <= reach || distance < MinArmLength) return target;

            Vector2 bentTarget = shoulder + offset / distance * reach;
            return Vector2.Lerp(bentTarget, target, extended);
        }

        private void HandleUpdateWorld(ISkeletonAnimation animated)
        {
            if (!CanCorrectGun()) return;

            float weight = view != null ? Mathf.SmoothStep(0f, 1f, view.AimWeight) : 1f;
            if (weight <= MinGunWeight) return;

            float excess;
            if (!TryGetGunAngleExcess(out excess)) return;

            _gunBone.Rotation = _gunBaseRotation + excess * weight * GetMirrorSign(_gunBone.Parent);
            _gunRotationModified = true;
        }

        private void HandleUpdateComplete(ISkeletonAnimation animated)
        {
            if (!_gunRotationModified) return;

            _gunRotationModified = false;
            if (_gunBone != null) _gunBone.Rotation = _gunBaseRotation;
        }

        private bool CanCorrectGun()
        {
            return correctGunDirection
                && _gunBone != null
                && _barrelBone != null
                && _hasCursor
                && model != null
                && model.IsAiming;
        }

        private bool TryGetGunAngleExcess(out float excess)
        {
            excess = 0f;

            Vector2 barrel = new Vector2(_barrelBone.WorldX - _gunBone.WorldX, _barrelBone.WorldY - _gunBone.WorldY);
            Vector2 toCursor = new Vector2(_cursorWorld.x - _gunBone.WorldX, _cursorWorld.y - _gunBone.WorldY);
            if (barrel.sqrMagnitude < MinVectorSqrLength) return false;
            if (toCursor.sqrMagnitude < MinVectorSqrLength) return false;

            float barrelAngle = Mathf.Atan2(barrel.y, barrel.x) * Mathf.Rad2Deg;
            float cursorAngle = Mathf.Atan2(toCursor.y, toCursor.x) * Mathf.Rad2Deg;
            float delta = Mathf.DeltaAngle(barrelAngle, cursorAngle);

            float tolerance = Mathf.Clamp(gunToleranceDegrees, 0f, MaxToleranceDegrees);
            excess = delta - Mathf.Clamp(delta, -tolerance, tolerance);
            return Mathf.Abs(excess) >= MinGunCorrection;
        }

        private static float GetMirrorSign(Bone parent)
        {
            if (parent == null) return 1f;
            return parent.A * parent.D - parent.B * parent.C < 0f ? -1f : 1f;
        }

        private void UpdateAimHeight(float cursorWorldY)
        {
            if (model == null) return;

            float characterHeight = GetCharacterHeight();
            float verticalDistance = cursorWorldY - model.transform.position.y;
            CursorHeightInCharacters = verticalDistance / characterHeight;

            float threshold = characterHeight * HeightMultiplier;
            float hysteresis = characterHeight * HysteresisFraction;
            float limit = model.IsAimExtended ? threshold - hysteresis : threshold + hysteresis;

            model.SetAimExtended(verticalDistance >= limit);
        }

        private float GetCharacterHeight()
        {
            if (characterHeightOverride > 0.001f) return characterHeightOverride;
            if (_cachedCharacterHeight <= 0.001f) _cachedCharacterHeight = MeasureCharacterHeight();
            return _cachedCharacterHeight;
        }

        private float MeasureCharacterHeight()
        {
            float result = MeasureSetupPoseHeight();
            if (result <= 0.001f) result = MeasureRendererHeight();
            return result > 0.001f ? result : 1f;
        }

        private float MeasureSetupPoseHeight()
        {
            if (skeletonAnimation == null || skeletonAnimation.Skeleton == null) return 0f;

            Skeleton source = skeletonAnimation.Skeleton;
            Skeleton temp = new Skeleton(source.Data);
            temp.SetToSetupPose();
            HideNonBodySlots(temp);
            temp.UpdateWorldTransform();

            float x, y, width, height;
            float[] buffer = null;
            temp.GetBounds(out x, out y, out width, out height, ref buffer);
            if (height <= 0.0001f) return 0f;

            return height * Mathf.Abs(source.ScaleY) * Mathf.Abs(skeletonAnimation.transform.lossyScale.y);
        }

        private float MeasureRendererHeight()
        {
            if (skeletonAnimation == null) return 0f;

            Renderer rendererComponent = skeletonAnimation.GetComponent<Renderer>();
            if (rendererComponent == null) rendererComponent = skeletonAnimation.GetComponentInChildren<Renderer>();
            return rendererComponent != null ? rendererComponent.bounds.size.y : 0f;
        }

        private void HideNonBodySlots(Skeleton skeleton)
        {
            Bone crosshair = FindBone(skeleton, boneName);
            foreach (Slot slot in skeleton.Slots)
            {
                bool isCrosshair = crosshair != null && IsSameOrDescendant(slot.Bone, crosshair);
                if (isCrosshair || IsIgnoredForHeight(slot.Data.Name)) slot.Attachment = null;
            }
        }

        private bool IsIgnoredForHeight(string slotName)
        {
            if (heightIgnoreSlotNameParts == null || string.IsNullOrEmpty(slotName)) return false;

            for (int i = 0; i < heightIgnoreSlotNameParts.Length; i++)
            {
                string part = heightIgnoreSlotNameParts[i];
                if (!string.IsNullOrEmpty(part) && slotName.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private static bool IsSameOrDescendant(Bone candidate, Bone ancestor)
        {
            for (Bone current = candidate; current != null; current = current.Parent)
            {
                if (current == ancestor) return true;
            }
            return false;
        }

        private void LogHeight()
        {
            float height = GetCharacterHeight();
            float multiplier = HeightMultiplier;
            string source = characterHeightOverride > 0.001f ? " (override)" : " (measured)";

            Debug.Log("SpineboyTargetController: character height = " + height.ToString("F3") + " world units" + source +
                ", arm is extended above feet + " + (height * multiplier).ToString("F3") +
                " units (" + multiplier.ToString("0.##") + " heights).", this);
        }

        private void OnGUI()
        {
            if (!showHeightLine || !Application.isPlaying || model == null || cam == null) return;

            float height = GetCharacterHeight();
            Vector3 feet = model.transform.position;
            Vector3 screen = cam.WorldToScreenPoint(new Vector3(feet.x, feet.y + height * HeightMultiplier, feet.z));
            if (screen.z < 0f) return;

            float y = Screen.height - screen.y;
            string mode = model.IsAimExtended ? "EXTENDED" : "BENT";
            Color previousColor = GUI.color;

            GUI.color = model.IsAimExtended ? Color.green : Color.yellow;
            GUI.DrawTexture(new Rect(0f, y - 1f, Screen.width, 2f), Texture2D.whiteTexture);
            GUI.Label(new Rect(8f, y - 22f, 640f, 20f),
                HeightMultiplier.ToString("0.##") + " heights | height " + height.ToString("F3") +
                " | cursor at " + CursorHeightInCharacters.ToString("F2") + " heights | " + mode);
            GUI.color = previousColor;
        }

        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying || model == null) return;

            float height = GetCharacterHeight();
            Vector3 feet = model.transform.position;
            float thresholdY = feet.y + height * HeightMultiplier;

            Gizmos.color = model.IsAimExtended ? Color.green : Color.yellow;
            Gizmos.DrawLine(new Vector3(feet.x - 10f, thresholdY, feet.z), new Vector3(feet.x + 10f, thresholdY, feet.z));
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(feet, feet + Vector3.up * height);
        }
    }
}