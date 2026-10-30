using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public class CountdownTimer : MonoBehaviour
{
    public TMP_Text timerText;
    public float initDelayTime = 10f;
    public float startTime = 10f;
    public string nextSceneName = "";

    float timeLeft;
    float initLeft;

    void Start()
    {
        timeLeft = startTime;
        initLeft = initDelayTime;
    }

    void Update()
    {
        initLeft -= Time.deltaTime;
        if (initLeft <= 0)
        {
            initLeft = 0;
            if (timeLeft > 0)
            {
                timeLeft -= Time.deltaTime;
                if (timeLeft <= 0)
                {
                    timeLeft = 0;
                    OnFinished();
                }
            }
        }
        int total = Mathf.CeilToInt(timeLeft);
        timerText.text = string.Format("{0:00}:{1:00}", total / 60, total % 60);
    }

    void OnFinished()
    {
        if (!string.IsNullOrEmpty(nextSceneName))
            SceneManager.LoadScene(nextSceneName);
    }
}