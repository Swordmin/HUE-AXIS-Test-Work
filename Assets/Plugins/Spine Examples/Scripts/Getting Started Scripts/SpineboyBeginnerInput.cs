/******************************************************************************
 * Spine Runtimes License Agreement
 * Last updated July 28, 2023. Replaces all prior versions.
 *
 * Copyright (c) 2013-2023, Esoteric Software LLC
 *
 * Integration of the Spine Runtimes into software or otherwise creating
 * derivative works of the Spine Runtimes is permitted under the terms and
 * conditions of Section 2 of the Spine Editor License Agreement:
 * http://esotericsoftware.com/spine-editor-license
 *
 * Otherwise, it is permitted to integrate the Spine Runtimes into software or
 * otherwise create derivative works of the Spine Runtimes (collectively,
 * "Products"), provided that each user of the Products must obtain their own
 * Spine Editor license and redistribution of the Products in any form must
 * include this license and copyright notice.
 *
 * THE SPINE RUNTIMES ARE PROVIDED BY ESOTERIC SOFTWARE LLC "AS IS" AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL ESOTERIC SOFTWARE LLC BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES,
 * BUSINESS INTERRUPTION, OR LOSS OF USE, DATA, OR PROFITS) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THE
 * SPINE RUNTIMES, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 *****************************************************************************/

using UnityEngine;

namespace Spine.Unity.Examples {
	/// <summary>
	/// Input adapter for the Hue Axis task. Movement is driven by the mouse X position;
	/// right mouse button aims, left mouse button fires. No keyboard axis is used.
	/// Runs before the model and the view so they always see this frame's input.
	/// </summary>
	[DefaultExecutionOrder(-100)]
	public class SpineboyBeginnerInput : MonoBehaviour {
		[Header("Input")]
		[Tooltip("Optional override. If empty, the model's camera or Camera.main is used.")]
		public Camera inputCamera;
		[Range(0, 6)] public int aimMouseButton = 1;
		[Range(0, 6)] public int shootMouseButton = 0;
		public bool allowShooting = true;

		[Header("References")]
		public SpineboyBeginnerModel model;

		void OnValidate () {
			if (model == null) model = GetComponent<SpineboyBeginnerModel>();
			if (inputCamera == null && model != null) inputCamera = model.movementCamera;
		}

		void OnDisable () {
			if (model != null) model.StopAim();
		}

		void Update () {
			if (model == null) return;

			// Send the screen position every frame. The model converts it to world space,
			// clamps it to the screen edges and handles inertia itself.
			model.SetTargetFromScreen(Input.mousePosition, inputCamera);

			if (Input.GetMouseButtonDown(aimMouseButton)) model.StartAim();
			if (Input.GetMouseButtonUp(aimMouseButton)) model.StopAim();
			if (allowShooting && Input.GetMouseButton(shootMouseButton)) model.TryShoot();
		}
	}
}
