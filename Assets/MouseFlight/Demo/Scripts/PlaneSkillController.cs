using UnityEngine;

namespace MFlight.Demo
{
    public class PlaneSkillController : MonoBehaviour
    {
        private Plane plane;
        private PlaneSkill activeSkill;
        private float timer;

        private void Awake()
        {
            plane = GetComponent<Plane>();
        }

        public void UseSkill(PlaneSkill skill)
        {
            // åªç›ÇÃÉXÉLÉãÇ™Ç†ÇÍÇŒâèú
            if (activeSkill != null)
                activeSkill.Deactivate(plane);

            Debug.Log($"Using skill: {skill.skillName}");
            activeSkill = skill;
            timer = skill.duration;
            skill.Activate(plane);
        }

        private void Update()
        {
            if (activeSkill == null) return;

            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                activeSkill.Deactivate(plane);
                Debug.Log($"Skill ended: {activeSkill.skillName}");
                activeSkill = null;
            }
        }
    }

}