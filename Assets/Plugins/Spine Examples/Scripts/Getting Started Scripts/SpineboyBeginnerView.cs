using UnityEngine;
using UnityEngine.Serialization;

namespace Spine.Unity.Examples
{
    [DefaultExecutionOrder(-10)]
    public class SpineboyBeginnerView : MonoBehaviour
    {
        private const int IdleTrack = 0;
        private const int WalkTrack = 1;
        private const int WalkOldTrack = 2;
        private const int WalkArmsTrack = 3;
        private const int RunTrack = 4;
        private const int RunOldTrack = 5;
        private const int RunArmsTrack = 6;
        private const int AimTrack = 7;
        private const int AimHighTrack = 8;
        private const int ShootTrack = 9;
        private const int TurnTrack = 10;

        private const float MinTurnScale = 0.12f;
        private const float MinClipTurnDuration = 0.05f;
        private const float MinFallbackTurnDuration = 0.04f;
        private const float MinAimBlendDuration = 0.01f;
        private const float OverlayTrackThreshold = 0.5f;

        [Header("Components")]
        public SpineboyBeginnerModel model;
        public SkeletonAnimation skeletonAnimation;

        [Header("Animations")]
        public AnimationReferenceAsset idle;
        public AnimationReferenceAsset walk;
        public AnimationReferenceAsset run;
        public AnimationReferenceAsset aim;
        public AnimationReferenceAsset aimHigh;
        public AnimationReferenceAsset shoot;
        public AnimationReferenceAsset turn;

        [Header("Track Mixing")]
        public MixBlend aimMixBlend = MixBlend.Add;
        public MixBlend aimHighMixBlend = MixBlend.Add;
        public MixBlend shootMixBlend = MixBlend.Replace;

        [Min(0.04f)] public float fallbackFlipDuration = 0.12f;
        public string turnFlipEventName = "turn";

        [Header("Spatial Phase Offsets (cycles)")]
        [Range(0f, 1f)] public float walkPhaseOffset;
        [Range(0f, 1f)] public float runPhaseOffset;
        public bool reverseLocomotionPhaseWhenMovingLeft = true;
        public bool reverseOnlyLegs = true;
        public bool useShortestRotation = true;
        public string[] armRootBoneNames = new string[] { "front-shoulder", "rear-shoulder", "front-upper-arm", "rear-upper-arm" };
        [Min(0f)] public float facingPhaseBlendTime = 0.18f;

        [Header("Effects (optional)")]
        public ParticleSystem gunParticles;

        [SerializeField, FormerlySerializedAs("aimWeight")] private float _aimWeight;

        private float _aimHeightWeight;
        private float _walkWeight;
        private float _walkWeightVelocity;
        private float _runWeight;
        private float _runWeightVelocity;

        private bool _displayedFacingLeft;
        private bool _phaseFacingLeft;
        private bool _oldPhaseFacingLeft;
        private float _phaseCrossfade = 1f;

        private Spine.Animation _walkLegs;
        private Spine.Animation _walkArms;
        private Spine.Animation _runLegs;
        private Spine.Animation _runArms;

        private bool _isTurning;
        private bool _isFallbackTurning;
        private bool _turnFlipped;
        private bool _turnRequestedFacingLeft;
        private float _turnDuration;
        private float _turnFlipTime;
        private float _turnEndTime;
        private float _fallbackTurnStartTime;
        private float _fallbackTurnScaleMagnitude;
        private bool _fallbackTurnStartFacingLeft;

        public float AimWeight => _aimWeight;
        public float AimExtendedWeight => Mathf.SmoothStep(0f, 1f, _aimHeightWeight);
        public bool IsTurning => _isTurning;
        public bool DisplayedFacingLeft => _displayedFacingLeft;

        private bool HasRequiredReferences => skeletonAnimation != null && model != null && model.movementSettings != null;

        private void OnValidate()
        {
            ResolveReferences();
        }

        private void Start()
        {
            ResolveReferences();
            if (skeletonAnimation == null || model == null)
            {
                Debug.LogError("SpineboyBeginnerView requires SpineboyBeginnerModel and SkeletonAnimation.", this);
                enabled = false;
                return;
            }

            if (aimHigh == null)
                Debug.LogWarning("SpineboyBeginnerView: 'aimHigh' is not assigned, so the extended arm pose cannot be shown.", this);

            model.ShootEvent += PlayShoot;
            skeletonAnimation.AnimationState.Event += HandleEvent;
            skeletonAnimation.BeforeApply += HandleBeforeApply;

            InitializeFacing();
            BuildSplitAnimations();
            InitializeTracks();
        }

        private void OnDestroy()
        {
            if (model != null)
            {
                model.ShootEvent -= PlayShoot;
                model.SetFacingTurnLocked(false);
            }

            if (skeletonAnimation != null)
            {
                skeletonAnimation.AnimationState.Event -= HandleEvent;
                skeletonAnimation.BeforeApply -= HandleBeforeApply;
            }
        }

        private void Update()
        {
            if (!HasRequiredReferences) return;

            SpineboyMovementSettings settings = model.movementSettings;
            UpdateLocomotion(settings);
            UpdateAim(settings);
            SyncFacingWhileAiming();
            UpdateTurning();
        }

        private void ResolveReferences()
        {
            if (model == null) model = GetComponent<SpineboyBeginnerModel>();
            if (skeletonAnimation == null) skeletonAnimation = GetComponent<SkeletonAnimation>();
        }

        private void InitializeFacing()
        {
            _displayedFacingLeft = skeletonAnimation.Skeleton.ScaleX < 0f;
            _phaseFacingLeft = _displayedFacingLeft;
            _oldPhaseFacingLeft = _displayedFacingLeft;
            model.ForceFacing(_displayedFacingLeft);
        }

        private void InitializeTracks()
        {
            EnsureTrack(IdleTrack, idle, 1f, 1f, MixBlend.Replace);
            EnsureTrack(WalkTrack, _walkLegs, 0f, 0f, MixBlend.Replace);
            EnsureTrack(WalkOldTrack, _walkLegs, 0f, 0f, MixBlend.Replace);
            EnsureTrack(WalkArmsTrack, _walkArms, 0f, 0f, MixBlend.Replace);
            EnsureTrack(RunTrack, _runLegs, 0f, 0f, MixBlend.Replace);
            EnsureTrack(RunOldTrack, _runLegs, 0f, 0f, MixBlend.Replace);
            EnsureTrack(RunArmsTrack, _runArms, 0f, 0f, MixBlend.Replace);
            EnsureTrack(AimTrack, aim, 0f, 1f, aimMixBlend);
            EnsureTrack(AimHighTrack, aimHigh, 0f, 1f, aimHighMixBlend);
        }

        private void UpdateLocomotion(SpineboyMovementSettings settings)
        {
            UpdateLocomotionWeights(settings);
            EnsureTrack(IdleTrack, idle, 1f, 1f, MixBlend.Replace);
            UpdatePhaseCrossfade();

            float oldShare = 1f - Mathf.SmoothStep(0f, 1f, _phaseCrossfade);
            ApplyLocomotionTracks(WalkTrack, WalkOldTrack, WalkArmsTrack, _walkLegs, _walkArms, _walkWeight, oldShare);
            ApplyLocomotionTracks(RunTrack, RunOldTrack, RunArmsTrack, _runLegs, _runArms, _runWeight, oldShare);
        }

        private void UpdateLocomotionWeights(SpineboyMovementSettings settings)
        {
            float speed = model.AbsoluteSpeed;
            float walkTarget = SmoothBlend(settings.idleToWalkStartSpeed, settings.idleToWalkEndSpeed, speed);
            float runTarget = SmoothBlend(settings.runAnimationBlendStartSpeed, settings.runAnimationBlendEndSpeed, speed);
            float smoothTime = Mathf.Max(0f, settings.locomotionBlendSmoothTime);

            _walkWeight = Mathf.SmoothDamp(_walkWeight, walkTarget, ref _walkWeightVelocity, smoothTime);
            _runWeight = Mathf.SmoothDamp(_runWeight, runTarget, ref _runWeightVelocity, smoothTime);
            _walkWeight = Mathf.Clamp01(_walkWeight);
            _runWeight = Mathf.Clamp01(_runWeight);
        }

        private void ApplyLocomotionTracks(int legsTrack, int oldLegsTrack, int armsTrack,
            Spine.Animation legs, Spine.Animation arms, float weight, float oldShare)
        {
            EnsureTrack(legsTrack, legs, weight, 0f, MixBlend.Replace);
            EnsureTrack(oldLegsTrack, legs, weight * oldShare, 0f, MixBlend.Replace);
            EnsureTrack(armsTrack, arms, weight, 0f, MixBlend.Replace);
        }

        private static float SmoothBlend(float start, float end, float value)
        {
            float t = Mathf.InverseLerp(start, Mathf.Max(end, start + 0.001f), value);
            return Mathf.SmoothStep(0f, 1f, t);
        }

        private void UpdateAim(SpineboyMovementSettings settings)
        {
            float blendStep = Time.deltaTime / Mathf.Max(MinAimBlendDuration, settings.aimBlendDuration);
            _aimWeight = Mathf.MoveTowards(_aimWeight, model.IsAiming ? 1f : 0f, blendStep);
            _aimHeightWeight = Mathf.MoveTowards(_aimHeightWeight, model.IsAimExtended ? 1f : 0f, blendStep);

            float aimEased = Mathf.SmoothStep(0f, 1f, _aimWeight);
            float heightEased = Mathf.SmoothStep(0f, 1f, _aimHeightWeight);
            float extendedAlpha = aimHigh != null ? aimEased * heightEased : 0f;
            float bentAlpha = aimEased - extendedAlpha;

            EnsureTrack(AimTrack, aim, bentAlpha, 1f, aimMixBlend);
            EnsureTrack(AimHighTrack, aimHigh, extendedAlpha, 1f, aimHighMixBlend);
        }

        private void SyncFacingWhileAiming()
        {
            if (!_isTurning && model.IsAiming) model.ForceFacing(_displayedFacingLeft);
        }

        private void UpdatePhaseCrossfade()
        {
            if (_displayedFacingLeft != _phaseFacingLeft) StartPhaseCrossfade();

            if (_phaseCrossfade < 1f)
                _phaseCrossfade = Mathf.Min(1f, _phaseCrossfade + Time.deltaTime / Mathf.Max(0.0001f, facingPhaseBlendTime));
        }

        private void StartPhaseCrossfade()
        {
            _oldPhaseFacingLeft = _phaseCrossfade < 0.5f ? _oldPhaseFacingLeft : _phaseFacingLeft;
            _phaseFacingLeft = _displayedFacingLeft;

            bool instant = facingPhaseBlendTime <= 0.0001f || !reverseLocomotionPhaseWhenMovingLeft;
            _phaseCrossfade = instant ? 1f : 0f;
        }

        private void HandleBeforeApply(ISkeletonAnimation animated)
        {
            if (!HasRequiredReferences) return;

            bool reverse = reverseLocomotionPhaseWhenMovingLeft;
            SetSpatialTrackTime(WalkTrack, walkPhaseOffset, reverse, _phaseFacingLeft);
            SetSpatialTrackTime(RunTrack, runPhaseOffset, reverse, _phaseFacingLeft);
            SetSpatialTrackTime(WalkOldTrack, walkPhaseOffset, reverse, _oldPhaseFacingLeft);
            SetSpatialTrackTime(RunOldTrack, runPhaseOffset, reverse, _oldPhaseFacingLeft);
            SetSpatialTrackTime(WalkArmsTrack, walkPhaseOffset, false, false);
            SetSpatialTrackTime(RunArmsTrack, runPhaseOffset, false, false);
        }

        private void SetSpatialTrackTime(int trackIndex, float phaseOffset, bool reverseWhenFacingLeft, bool facingLeftForPhase)
        {
            TrackEntry entry = skeletonAnimation.AnimationState.GetCurrent(trackIndex);
            if (entry == null || entry.Animation == null || entry.Animation.Duration <= 0f) return;

            entry.TimeScale = 0f;
            int cycles = Mathf.Max(1, model.movementSettings.cyclesAcrossScreen);
            float normalizedX = model.NormalizedScreenX;

            bool useLeftwardPhase = reverseWhenFacingLeft && facingLeftForPhase;
            float phaseCoordinate = useLeftwardPhase ? 1f - normalizedX : normalizedX;
            float phase = Mathf.Repeat(phaseCoordinate * cycles + phaseOffset, 1f);
            entry.TrackTime = phase * entry.Animation.Duration;
        }

        private void EnsureTrack(int trackIndex, AnimationReferenceAsset asset, float alpha, float timeScale, MixBlend mixBlend)
        {
            EnsureTrack(trackIndex, asset != null ? asset.Animation : null, alpha, timeScale, mixBlend);
        }

        private void EnsureTrack(int trackIndex, Spine.Animation animation, float alpha, float timeScale, MixBlend mixBlend)
        {
            if (animation == null || skeletonAnimation == null) return;

            AnimationState state = skeletonAnimation.AnimationState;
            TrackEntry current = state.GetCurrent(trackIndex);
            if (current == null || current.Animation != animation)
            {
                current = state.SetAnimation(trackIndex, animation, true);
                current.MixDuration = 0f;
            }

            float threshold = trackIndex == IdleTrack ? 0f : OverlayTrackThreshold;
            current.AttachmentThreshold = threshold;
            current.DrawOrderThreshold = threshold;
            current.ShortestRotation = useShortestRotation;
            current.TimeScale = timeScale;
            current.Alpha = Mathf.Clamp01(alpha);
            current.MixBlend = mixBlend;
        }

        private void BuildSplitAnimations()
        {
            _walkLegs = walk != null ? walk.Animation : null;
            _runLegs = run != null ? run.Animation : null;
            _walkArms = null;
            _runArms = null;

            if (!reverseOnlyLegs || skeletonAnimation == null || skeletonAnimation.Skeleton == null) return;

            bool[] armMask = BuildArmBoneMask(skeletonAnimation.Skeleton.Data);
            if (armMask == null)
            {
                Debug.LogWarning("SpineboyBeginnerView: none of the arm root bones (armRootBoneNames) exist in the skeleton. Reversing the whole clip.", this);
                return;
            }

            SplitAnimation(_walkLegs, armMask, out _walkLegs, out _walkArms);
            SplitAnimation(_runLegs, armMask, out _runLegs, out _runArms);
        }

        private bool[] BuildArmBoneMask(SkeletonData data)
        {
            if (armRootBoneNames == null || armRootBoneNames.Length == 0) return null;

            bool[] mask = new bool[data.Bones.Count];
            bool any = false;
            for (int i = 0; i < mask.Length; i++)
            {
                mask[i] = HasArmRootInHierarchy(data.Bones.Items[i]);
                any |= mask[i];
            }
            return any ? mask : null;
        }

        private bool HasArmRootInHierarchy(BoneData bone)
        {
            for (BoneData current = bone; current != null; current = current.Parent)
            {
                if (IsArmRootName(current.Name)) return true;
            }
            return false;
        }

        private bool IsArmRootName(string boneName)
        {
            for (int i = 0; i < armRootBoneNames.Length; i++)
            {
                if (armRootBoneNames[i] == boneName) return true;
            }
            return false;
        }

        private static void SplitAnimation(Spine.Animation source, bool[] armMask, out Spine.Animation legs, out Spine.Animation arms)
        {
            legs = source;
            arms = null;
            if (source == null) return;

            ExposedList<Timeline> legTimelines = new ExposedList<Timeline>();
            ExposedList<Timeline> armTimelines = new ExposedList<Timeline>();
            foreach (Timeline timeline in source.Timelines)
            {
                if (IsArmTimeline(timeline, armMask)) armTimelines.Add(timeline);
                else legTimelines.Add(timeline);
            }

            if (armTimelines.Count == 0 || legTimelines.Count == 0) return;

            legs = new Spine.Animation(source.Name + "_legs", legTimelines, source.Duration);
            arms = new Spine.Animation(source.Name + "_arms", armTimelines, source.Duration);
        }

        private static bool IsArmTimeline(Timeline timeline, bool[] armMask)
        {
            IBoneTimeline boneTimeline = timeline as IBoneTimeline;
            if (boneTimeline == null) return false;

            int index = boneTimeline.BoneIndex;
            return index >= 0 && index < armMask.Length && armMask[index];
        }

        private void UpdateTurning()
        {
            if (_isTurning)
            {
                if (_isFallbackTurning) UpdateFallbackTurn();
                else UpdateClipTurn();
                return;
            }

            if (model.IsAiming || model.facingLeft == _displayedFacingLeft) return;
            BeginTurn(model.facingLeft);
        }

        private void BeginTurn(bool faceLeft)
        {
            _turnRequestedFacingLeft = faceLeft;

            if (turn == null || turn.Animation == null) BeginFallbackTurn(faceLeft);
            else BeginClipTurn();
        }

        private void BeginClipTurn()
        {
            TrackEntry entry = skeletonAnimation.AnimationState.SetAnimation(TurnTrack, turn, false);
            entry.ShortestRotation = useShortestRotation;
            entry.MixDuration = 0f;
            entry.TimeScale = 1f;
            entry.Alpha = 1f;

            _turnDuration = Mathf.Max(MinClipTurnDuration, turn.Animation.Duration);
            _turnFlipTime = Time.time + _turnDuration * 0.5f;
            _turnEndTime = Time.time + _turnDuration;
            _turnFlipped = false;
            _isFallbackTurning = false;
            _isTurning = true;
            model.SetFacingTurnLocked(true);
        }

        private void UpdateClipTurn()
        {
            if (!_turnFlipped && Time.time >= _turnFlipTime)
            {
                ApplyFacing(_turnRequestedFacingLeft);
                _turnFlipped = true;
            }

            if (Time.time < _turnEndTime) return;

            skeletonAnimation.AnimationState.ClearTrack(TurnTrack);
            FinishTurn();
        }

        private void BeginFallbackTurn(bool faceLeft)
        {
            if (skeletonAnimation == null || skeletonAnimation.Skeleton == null) return;

            _turnRequestedFacingLeft = faceLeft;
            _fallbackTurnStartFacingLeft = _displayedFacingLeft;
            _fallbackTurnScaleMagnitude = Mathf.Max(0.0001f, Mathf.Abs(skeletonAnimation.Skeleton.ScaleX));
            _fallbackTurnStartTime = Time.time;
            _turnDuration = Mathf.Max(MinFallbackTurnDuration, fallbackFlipDuration);
            _isFallbackTurning = true;
            _isTurning = true;
            _turnFlipped = false;
            model.SetFacingTurnLocked(true);
        }

        private void UpdateFallbackTurn()
        {
            if (skeletonAnimation == null || skeletonAnimation.Skeleton == null)
            {
                FinishTurn();
                return;
            }

            float t = Mathf.Clamp01((Time.time - _fallbackTurnStartTime) / Mathf.Max(MinFallbackTurnDuration, _turnDuration));
            Skeleton skeleton = skeletonAnimation.Skeleton;

            if (t >= 1f)
            {
                skeleton.ScaleX = SignedTurnScale(_turnRequestedFacingLeft, _fallbackTurnScaleMagnitude);
                _displayedFacingLeft = _turnRequestedFacingLeft;
                FinishTurn();
                return;
            }

            bool secondHalf = t >= 0.5f;
            if (secondHalf && !_turnFlipped)
            {
                _displayedFacingLeft = _turnRequestedFacingLeft;
                _turnFlipped = true;
            }

            float magnitudeFactor = secondHalf
                ? Mathf.SmoothStep(0f, 1f, (t - 0.5f) * 2f)
                : 1f - Mathf.SmoothStep(0f, 1f, t * 2f);
            bool faceLeftForScale = secondHalf ? _turnRequestedFacingLeft : _fallbackTurnStartFacingLeft;

            skeleton.ScaleX = SignedTurnScale(faceLeftForScale, _fallbackTurnScaleMagnitude) * Mathf.Max(MinTurnScale, magnitudeFactor);
        }

        private static float SignedTurnScale(bool faceLeft, float magnitude)
        {
            return faceLeft ? -magnitude : magnitude;
        }

        private void FinishTurn()
        {
            _isTurning = false;
            _isFallbackTurning = false;
            if (model != null) model.SetFacingTurnLocked(false);
        }

        private void ApplyFacing(bool faceLeft)
        {
            if (skeletonAnimation == null || skeletonAnimation.Skeleton == null) return;

            float magnitude = Mathf.Max(0.0001f, Mathf.Abs(skeletonAnimation.Skeleton.ScaleX));
            skeletonAnimation.Skeleton.ScaleX = SignedTurnScale(faceLeft, magnitude);
            _displayedFacingLeft = faceLeft;
        }

        public void Turn(bool faceLeft)
        {
            if (model != null && model.IsAiming) return;
            if (faceLeft == _displayedFacingLeft || _isTurning) return;

            if (model != null) model.ForceFacing(faceLeft);
            BeginTurn(faceLeft);
        }

        private void HandleEvent(TrackEntry trackEntry, Spine.Event e)
        {
            if (IsTurnFlipEvent(e))
            {
                ApplyFacing(_turnRequestedFacingLeft);
                _turnFlipped = true;
            }
        }

        private bool IsTurnFlipEvent(Spine.Event e)
        {
            return _isTurning
                && !_isFallbackTurning
                && !_turnFlipped
                && !string.IsNullOrEmpty(turnFlipEventName)
                && e.Data != null
                && e.Data.Name == turnFlipEventName;
        }

        public void PlayShoot()
        {
            if (skeletonAnimation == null || shoot == null || shoot.Animation == null) return;

            AnimationState state = skeletonAnimation.AnimationState;
            TrackEntry shootEntry = state.SetAnimation(ShootTrack, shoot, false);
            shootEntry.ShortestRotation = useShortestRotation;
            shootEntry.MixBlend = shootMixBlend;
            shootEntry.AttachmentThreshold = 1f;
            shootEntry.MixDuration = 0f;
            shootEntry.TimeScale = 1f;
            state.AddEmptyAnimation(ShootTrack, 0.12f, 0.06f);

            if (gunParticles != null) gunParticles.Play();
        }
    }
}