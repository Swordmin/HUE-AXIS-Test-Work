using System;
using UnityEngine;

namespace Spine.Unity.Examples
{
    public enum SpineBeginnerBodyState
    {
        Idle,
        Walking,
        Running,
        Jumping
    }

    [SelectionBase]
    [DefaultExecutionOrder(-50)]
    public class SpineboyBeginnerModel : MonoBehaviour
    {
        private const float DefaultScreenPadding = 0.02f;
        private const float DefaultVerticalFraction = 0.28f;
        private const float MinFollowSmoothTime = 0.05f;
        private const float MaxFrameTime = 0.1f;
        private const float SubstepsPerSecond = 120f;
        private const int MaxSubsteps = 12;

        [Header("Current State")]
        public SpineBeginnerBodyState state = SpineBeginnerBodyState.Idle;
        public bool facingLeft;
        public float currentSpeed;

        [Header("Movement Settings")]
        public SpineboyMovementSettings movementSettings;
        public Camera movementCamera;
        public bool placeAtLowerThirdOnStart = true;

        [Header("Balance")]
        [Min(0.01f)] public float shootInterval = 0.12f;

        [field: SerializeField] public bool IsAiming { get; private set; }

        private float _lastShootTime;
        private float _targetX;
        private bool _hasTarget;
        private bool _hasFacingCandidate;
        private bool _facingCandidateLeft;
        private float _facingCandidateSince;
        private float _nextAllowedFacingChangeTime;
        private bool _facingTurnLocked;

        public event Action ShootEvent;
        public event Action StartAimEvent;
        public event Action StopAimEvent;
        public event Action<bool> AimHeightChanged;

        public bool IsAimExtended { get; private set; }
        public float HorizontalVelocity => currentSpeed;
        public float AbsoluteSpeed => Mathf.Abs(currentSpeed);
        public float NormalizedScreenX => GetNormalizedScreenX(transform.position.x);

        private float ScreenPadding => movementSettings != null ? movementSettings.horizontalScreenPadding : DefaultScreenPadding;
        private float VerticalFraction => movementSettings != null ? movementSettings.verticalScreenFraction : DefaultVerticalFraction;

        private void Awake()
        {
            if (movementCamera == null) movementCamera = Camera.main;
            if (ShouldPlaceAtLowerThird()) PlaceAtLowerThird();

            _targetX = transform.position.x;
            currentSpeed = 0f;
            _hasTarget = true;
        }

        private void Update()
        {
            if (movementCamera == null) movementCamera = Camera.main;
            if (movementSettings == null) return;

            StepHorizontalMovement(Time.deltaTime);
            UpdateFacingFromVelocity();
            UpdateLocomotionState();
        }

        public void SetTargetFromScreen(Vector3 screenPosition, Camera cameraOverride = null)
        {
            Camera cam = cameraOverride != null ? cameraOverride : movementCamera;
            if (cam == null) cam = Camera.main;
            if (cam == null) return;

            movementCamera = cam;
            float depth = GetCameraDepth(cam, transform.position);
            float screenX = Mathf.Clamp(screenPosition.x, Screen.width * ScreenPadding, Screen.width * (1f - ScreenPadding));
            Vector3 world = cam.ScreenToWorldPoint(new Vector3(screenX, screenPosition.y, depth));
            SetTargetX(world.x);
        }

        public void SetTargetX(float worldX)
        {
            _targetX = ClampWorldXToScreen(worldX);
            _hasTarget = true;
        }

        public void StartAim()
        {
            if (IsAiming) return;
            IsAiming = true;
            StartAimEvent?.Invoke();
        }

        public void StopAim()
        {
            if (!IsAiming) return;
            IsAiming = false;
            StopAimEvent?.Invoke();
        }

        public void SetAimExtended(bool extended)
        {
            if (IsAimExtended == extended) return;
            IsAimExtended = extended;
            AimHeightChanged?.Invoke(extended);
        }

        public void SetFacingTurnLocked(bool locked)
        {
            _facingTurnLocked = locked;
            if (locked) ResetFacingCandidate();
        }

        public void ForceFacing(bool left)
        {
            facingLeft = left;
            ResetFacingCandidate();
        }

        public void TryShoot()
        {
            if (Time.time - _lastShootTime < shootInterval) return;
            _lastShootTime = Time.time;
            ShootEvent?.Invoke();
        }

        public float GetNormalizedScreenX(float worldX)
        {
            Vector2 bounds = GetScreenWorldXBounds();
            float width = bounds.y - bounds.x;
            if (Mathf.Abs(width) < 0.0001f) return 0.5f;
            return Mathf.Clamp01((worldX - bounds.x) / width);
        }

        private bool ShouldPlaceAtLowerThird()
        {
            return movementSettings != null && placeAtLowerThirdOnStart && movementSettings.placeAtLowerThirdOnStart;
        }

        private void StepHorizontalMovement(float deltaTime)
        {
            if (!_hasTarget || deltaTime <= 0f) return;

            Vector2 bounds = GetScreenWorldXBounds();
            _targetX = Mathf.Clamp(_targetX, bounds.x, bounds.y);
            Vector3 position = transform.position;

            if (HasReachedTarget(position.x))
            {
                position.x = _targetX;
                currentSpeed = 0f;
            }
            else
            {
                position.x = IntegrateSpring(position.x, bounds, deltaTime);
            }

            transform.position = position;
        }

        private bool HasReachedTarget(float x)
        {
            return Mathf.Abs(_targetX - x) <= movementSettings.stopDistance
                && Mathf.Abs(currentSpeed) <= movementSettings.stopSpeed;
        }

        private float IntegrateSpring(float x, Vector2 bounds, float deltaTime)
        {
            float omega = 2f / Mathf.Max(MinFollowSmoothTime, movementSettings.followSmoothTime);
            float maxSpeed = Mathf.Max(0.01f, movementSettings.maxSpeed);
            float maxAcceleration = Mathf.Max(0.01f, movementSettings.acceleration);
            float maxBraking = Mathf.Max(0.01f, movementSettings.brakingDeceleration);

            float frameTime = Mathf.Min(deltaTime, MaxFrameTime);
            int steps = Mathf.Clamp(Mathf.CeilToInt(frameTime * SubstepsPerSecond), 1, MaxSubsteps);
            float stepTime = frameTime / steps;
            float velocity = currentSpeed;

            for (int i = 0; i < steps; i++)
            {
                float acceleration = omega * omega * (_targetX - x) - 2f * omega * velocity;
                float limit = acceleration * velocity >= 0f ? maxAcceleration : maxBraking;
                acceleration = Mathf.Clamp(acceleration, -limit, limit);

                velocity = Mathf.Clamp(velocity + acceleration * stepTime, -maxSpeed, maxSpeed);
                x += velocity * stepTime;

                if (x <= bounds.x || x >= bounds.y)
                {
                    x = Mathf.Clamp(x, bounds.x, bounds.y);
                    velocity = 0f;
                    break;
                }
            }

            currentSpeed = velocity;
            return x;
        }

        private void UpdateFacingFromVelocity()
        {
            if (movementSettings == null) return;

            if (!CanEvaluateFacing())
            {
                ResetFacingCandidate();
                return;
            }

            bool candidateLeft = currentSpeed < 0f;
            if (candidateLeft == facingLeft)
            {
                ResetFacingCandidate();
                return;
            }

            if (!_hasFacingCandidate || _facingCandidateLeft != candidateLeft)
            {
                StartFacingCandidate(candidateLeft);
                return;
            }

            if (IsFacingChangeAllowed()) ApplyFacingChange(candidateLeft);
        }

        private bool CanEvaluateFacing()
        {
            if (IsAiming || _facingTurnLocked) return false;
            if (GetTargetDistancePixels() <= Mathf.Max(0f, movementSettings.facingDeadZonePixels)) return false;
            return Mathf.Abs(currentSpeed) >= Mathf.Max(0f, movementSettings.minimumFacingSpeed);
        }

        private void StartFacingCandidate(bool candidateLeft)
        {
            _hasFacingCandidate = true;
            _facingCandidateLeft = candidateLeft;
            _facingCandidateSince = Time.time;
        }

        private bool IsFacingChangeAllowed()
        {
            if (Time.time < _nextAllowedFacingChangeTime) return false;
            float confirmTime = Mathf.Max(0f, movementSettings.facingDirectionConfirmTime);
            return Time.time - _facingCandidateSince >= confirmTime;
        }

        private void ApplyFacingChange(bool candidateLeft)
        {
            facingLeft = candidateLeft;
            _nextAllowedFacingChangeTime = Time.time + Mathf.Max(0f, movementSettings.facingTurnCooldown);
            ResetFacingCandidate();
        }

        private void ResetFacingCandidate()
        {
            _hasFacingCandidate = false;
            _facingCandidateSince = 0f;
        }

        private float GetTargetDistancePixels()
        {
            if (movementCamera == null) return float.PositiveInfinity;

            Vector3 characterScreen = movementCamera.WorldToScreenPoint(transform.position);
            Vector3 targetWorld = new Vector3(_targetX, transform.position.y, transform.position.z);
            Vector3 targetScreen = movementCamera.WorldToScreenPoint(targetWorld);
            return Mathf.Abs(targetScreen.x - characterScreen.x);
        }

        private void UpdateLocomotionState()
        {
            if (movementSettings == null) return;

            float speed = AbsoluteSpeed;
            float idleLimit = Midpoint(movementSettings.idleToWalkStartSpeed, movementSettings.idleToWalkEndSpeed);
            float runLimit = Midpoint(movementSettings.runAnimationBlendStartSpeed, movementSettings.runAnimationBlendEndSpeed);

            if (speed < idleLimit) state = SpineBeginnerBodyState.Idle;
            else if (speed < runLimit) state = SpineBeginnerBodyState.Walking;
            else state = SpineBeginnerBodyState.Running;
        }

        private static float Midpoint(float a, float b)
        {
            return 0.5f * (a + b);
        }

        private void PlaceAtLowerThird()
        {
            if (movementCamera == null) return;

            float depth = GetCameraDepth(movementCamera, transform.position);
            Vector3 screenPoint = new Vector3(Screen.width * 0.5f, Screen.height * VerticalFraction, depth);
            Vector3 position = transform.position;
            position.y = movementCamera.ScreenToWorldPoint(screenPoint).y;
            transform.position = position;
        }

        private float ClampWorldXToScreen(float x)
        {
            Vector2 bounds = GetScreenWorldXBounds();
            return Mathf.Clamp(x, bounds.x, bounds.y);
        }

        private Vector2 GetScreenWorldXBounds()
        {
            if (movementCamera == null) return new Vector2(-100000f, 100000f);

            float depth = GetCameraDepth(movementCamera, transform.position);
            float screenY = Screen.height * VerticalFraction;
            Vector3 left = movementCamera.ScreenToWorldPoint(new Vector3(Screen.width * ScreenPadding, screenY, depth));
            Vector3 right = movementCamera.ScreenToWorldPoint(new Vector3(Screen.width * (1f - ScreenPadding), screenY, depth));
            return left.x <= right.x ? new Vector2(left.x, right.x) : new Vector2(right.x, left.x);
        }

        private static float GetCameraDepth(Camera cam, Vector3 worldPosition)
        {
            return Mathf.Max(0.01f, Vector3.Dot(worldPosition - cam.transform.position, cam.transform.forward));
        }
    }
}