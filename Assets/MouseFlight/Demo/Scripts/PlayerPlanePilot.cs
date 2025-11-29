using UnityEngine;
using UnityEngine.InputSystem;

namespace MFlight.Demo
{
    /// <summary>
    /// プレイヤー操作用パイロット。
    /// 旧 Plane.cs が直接読んでいた InputAction をここに移している。
    /// </summary>
    [RequireComponent(typeof(Plane))]
    public class PlayerPlanePilot : BasePlanePilot
    {

        [SerializeField] private PlaneSkillController skillController;
        [SerializeField] private PlaneSkill equippedSkill;
        [SerializeField] private PlaneMissileLauncher missileLauncher;
        [SerializeField] private LockOnSystem lockOnSystem;

        [Header("Input Actions")]
        public InputActionReference pitchAction;
        public InputActionReference yawAction;
        public InputActionReference rollAction;
        public InputActionReference throttleAction;
        public InputActionReference skillAction;

        [Header("Weapon Input")]
        public InputActionReference missileAction;

        private float _pitch;
        private float _yaw;
        private float _roll;
        private float _throttleInput;

        public override float Pitch => _pitch;
        public override float Yaw => _yaw;
        public override float Roll => _roll;
        public override float ThrottleInput => _throttleInput;

        private void OnEnable()
        {
            if (pitchAction != null) pitchAction.action.Enable();
            if (yawAction != null) yawAction.action.Enable();
            if (rollAction != null) rollAction.action.Enable();
            if (throttleAction != null) throttleAction.action.Enable();
            if (skillAction != null) skillAction.action.Enable();
            if (missileAction != null) missileAction.action.Enable();
        }

        private void OnDisable()
        {
            if (pitchAction != null) pitchAction.action.Disable();
            if (yawAction != null) yawAction.action.Disable();
            if (rollAction != null) rollAction.action.Disable();
            if (throttleAction != null) throttleAction.action.Disable();
            if (missileAction != null) missileAction.action.Disable();
        }

        public override void TickPilot()
        {
            if (pitchAction == null || yawAction == null || rollAction == null || throttleAction == null)
            {
                _pitch = _yaw = _roll = _throttleInput = 0f;
                return;
            }

            // 入力読み取り（元 Plane.Update と同じ）
            Vector2 pitchVec = pitchAction.action.ReadValue<Vector2>();
            Vector2 yawVec = yawAction.action.ReadValue<Vector2>();
            Vector2 rollVec = rollAction.action.ReadValue<Vector2>();

            _pitch = Mathf.Clamp(pitchVec.y, -1f, 1f);
            _yaw = Mathf.Clamp(yawVec.x, -1f, 1f);
            _roll = Mathf.Clamp(rollVec.x, -1f, 1f);

            // スロットル入力（-1 ～ 1）
            _throttleInput = Mathf.Clamp(throttleAction.action.ReadValue<float>(), -1f, 1f);

            if (skillAction.action.triggered)
            {
                skillController.UseSkill(equippedSkill);
            }

            // ★ ミサイル発射
            if (missileAction != null && missileAction.action.triggered)
            {
                if (missileLauncher != null && lockOnSystem != null && lockOnSystem.CurrentTarget != null)
                {
                    missileLauncher.FireAt(lockOnSystem.CurrentTarget.transform);
                }
            }
        }
    }
}
