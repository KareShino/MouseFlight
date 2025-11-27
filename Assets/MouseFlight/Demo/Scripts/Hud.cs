//
// Copyright (c) Brian Hernandez. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
//

using UnityEngine;
using TMPro;   // ★ 追加

namespace MFlight.Demo
{
    public class Hud : MonoBehaviour
    {
        [Header("Components")]
        [SerializeField] private MouseFlightController mouseFlight = null;

        [Header("HUD Elements")]
        [SerializeField] private RectTransform boresight = null;
        [SerializeField] private RectTransform mousePos = null;

        // ★ 高度・速度表示用
        [Header("Flight Info")]
        [SerializeField] private TextMeshProUGUI altitudeText = null;
        [SerializeField] private TextMeshProUGUI speedText = null;
        [SerializeField] private Rigidbody aircraftRigidbody = null; // 機体の Rigidbody をドラッグして割り当て

        private Camera playerCam = null;

        private void Awake()
        {
            if (mouseFlight == null)
                Debug.LogError(name + ": Hud - Mouse Flight Controller not assigned!");

            playerCam = mouseFlight.GetComponentInChildren<Camera>();

            if (playerCam == null)
                Debug.LogError(name + ": Hud - No camera found on assigned Mouse Flight Controller!");

            if (aircraftRigidbody == null)
                Debug.LogWarning(name + ": Hud - Aircraft Rigidbody is not assigned. Altitude/Speed will not update.");

            //Cursor.visible = false;
            //Cursor.lockState = CursorLockMode.Locked;
        }

        private void Update()
        {
            if (mouseFlight == null || playerCam == null)
                return;

            UpdateGraphics(mouseFlight);
        }

        private void UpdateGraphics(MouseFlightController controller)
        {
            // 既存の照準まわり
            if (boresight != null)
            {
                boresight.position = playerCam.WorldToScreenPoint(controller.BoresightPos);
                boresight.gameObject.SetActive(boresight.position.z > 1f);
            }

            if (mousePos != null)
            {
                mousePos.position = playerCam.WorldToScreenPoint(controller.MouseAimPos);
                mousePos.gameObject.SetActive(mousePos.position.z > 1f);
            }

            // ★ 高度・速度テキスト更新
            if (aircraftRigidbody != null)
            {
                // 速度（m/s をそのまま表示、km/hにしたければ * 3.6f）
                float speed = aircraftRigidbody.linearVelocity.magnitude;
                if (speedText != null)
                {
                    speedText.text = $"{speed:0} m/s";
                    // km/h にしたい場合:
                    // speedText.text = $"{speed * 3.6f:0} km/h";
                }

                // 高度（ワールド座標の Y をそのまま高度とする）
                float altitude = aircraftRigidbody.position.y;
                if (altitudeText != null)
                {
                    altitudeText.text = $"{altitude:0} m";
                }
            }
        }

        public void SetReferenceMouseFlight(MouseFlightController controller)
        {
            mouseFlight = controller;
        }
    }
}
