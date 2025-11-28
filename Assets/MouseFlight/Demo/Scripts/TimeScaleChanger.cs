using UnityEngine;

public class TimeScaleChanger : MonoBehaviour
{
    [SerializeField] private float timeScale = 1.0f;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if(Time.timeScale != timeScale)
        {
            Time.timeScale = timeScale;
        }
    }
}
