using UnityEngine;
using UnityEngine.Serialization;

namespace Spine.Unity.Examples {

	[CreateAssetMenu(fileName = "SpineboyMovementSettings", menuName = "Spine/Examples/Spineboy Movement Settings")]
	public class SpineboyMovementSettings : ScriptableObject {
		[Header("Cursor Follow")]
		[Tooltip("Maximum horizontal speed in world units per second.")]
		[Min(0.01f)] public float maxSpeed = 5f;
		[Tooltip("Acceleration limit while speeding up (world units / second^2).")]
		[Min(0.01f)] public float acceleration = 12f;
		[Tooltip("Deceleration limit while slowing down or reversing (world units / second^2).")]
		[Min(0.01f)] public float brakingDeceleration = 18f;
		[Tooltip("Softness of the stop. The character is pulled to the cursor like a critically damped spring with this time constant: " +
			"no overshoot, the speed decays exponentially and never changes sign on the way. Larger = longer, softer approach.")]
		[Min(0.05f)] public float followSmoothTime = 0.4f;
		[Tooltip("Final snap: the character is placed exactly on the cursor when closer than this (world units) AND slower than Stop Speed. Keep it sub-pixel.")]
		[Min(0f)] public float stopDistance = 0.004f;
		[Min(0f)] public float stopSpeed = 0.05f;

		[Header("Screen Bounds")]
		[Range(0f, 0.2f)] public float horizontalScreenPadding = 0.02f;
		[Range(0.05f, 0.6f)] public float verticalScreenFraction = 0.28f;
		public bool placeAtLowerThirdOnStart = true;

		[Header("Idle -> Walk (world units / second)")]
		[Tooltip("At or below this speed the character is 100% idle.")]
		[FormerlySerializedAs("idleEnterSpeed")]
		[Min(0f)] public float idleToWalkStartSpeed = 0.05f;
		[Tooltip("At or above this speed the walk animation is at 100% weight. A wide range = smooth stop without a visible pop.")]
		[Min(0.01f)] public float idleToWalkEndSpeed = 0.9f;

		[Header("Walk -> Run (world units / second)")]
		[Tooltip("Speed at which the run animation starts blending over walk.")]
		[Min(0f)] public float runAnimationBlendStartSpeed = 1.8f;
		[Tooltip("Speed at which the run animation reaches full weight.")]
		[Min(0.01f)] public float runAnimationBlendEndSpeed = 4.2f;

		[Tooltip("Extra time smoothing of the blend weights (seconds). Does not affect the deterministic leg phase, only the idle/walk/run mix.")]
		[Min(0f)] public float locomotionBlendSmoothTime = 0.08f;

		[Header("Facing")]
		[Tooltip("Minimum absolute horizontal velocity required before considering a facing change.")]
		[Min(0f)] public float minimumFacingSpeed = 0.35f;
		[Tooltip("Cursor must be farther than this many screen pixels from the character before facing can change.")]
		[Min(0f)] public float facingDeadZonePixels = 16f;
		[Tooltip("The opposite movement direction must remain stable for this long before facing changes.")]
		[Min(0f)] public float facingDirectionConfirmTime = 0.12f;
		[Tooltip("Minimum time between two facing changes, to prevent rapid left-right flipping.")]
		[Min(0f)] public float facingTurnCooldown = 0.18f;

		[Header("Spatial Animation Phase")]
		[Tooltip("Number of full walk/run cycles (2 steps each) between the left and the right screen edge. Fixed regardless of resolution.")]
		[Min(1)] public int cyclesAcrossScreen = 6;

		[Header("Aiming")]
		[Tooltip("The arm is extended when the cursor is at least this many character heights above the character's feet.")]
		[Min(0.1f)] public float aimExtendedHeightMultiplier = 2f;
		[Tooltip("Hysteresis around the threshold as a fraction of the character height (prevents flicker right on the line).")]
		[Range(0f, 0.3f)] public float aimHeightHysteresisFraction = 0.03f;
		[Min(0.01f)] public float aimBlendDuration = 0.12f;

		void OnValidate () {
			idleToWalkEndSpeed = Mathf.Max(idleToWalkEndSpeed, idleToWalkStartSpeed + 0.01f);
			runAnimationBlendEndSpeed = Mathf.Max(runAnimationBlendEndSpeed, runAnimationBlendStartSpeed + 0.01f);
		}
	}
}
