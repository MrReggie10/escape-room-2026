using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public class CountdownTimer : MonoBehaviour
{
    public TMP_Text timerText;
    public float startTime = 10f;
    public string nextSceneName = "";

    float timeLeft;

    void Start()
    {
        timeLeft = startTime;
    }

    void Update()
    {
        if (timeLeft > 0)
        {
            timeLeft -= Time.deltaTime;
            if (timeLeft <= 0)
            {
                timeLeft = 0;
                OnFinished();
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