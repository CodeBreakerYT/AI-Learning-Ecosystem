using System.Collections;
using TMPro;
using UnityEngine;

public class TypewriterText : MonoBehaviour
{
    public TextMeshProUGUI textComponent;
    public float typingSpeed = 0.03f;

    private Coroutine typingCoroutine;
    private string currentFullText = "";
    private bool isTyping = false;
    private bool isCompleted = false;

    void Awake()
    {
        if (textComponent == null)
            textComponent = GetComponent<TextMeshProUGUI>();
    }

    void Update()
    {
        // Press B (or any key you choose)
        if (Input.GetKeyDown(KeyCode.B))
        {
            SkipOrComplete();
        }
    }

    public void ShowText(string text)
    {
        currentFullText = text;
        isCompleted = false;

        if (typingCoroutine != null)
            StopCoroutine(typingCoroutine);

        typingCoroutine = StartCoroutine(TypeText());
    }

    IEnumerator TypeText()
    {
        isTyping = true;
        textComponent.text = "";

        foreach (char c in currentFullText.ToCharArray())
        {
            textComponent.text += c;
            yield return new WaitForSeconds(typingSpeed);
        }

        isTyping = false;
        isCompleted = true;
    }

    public void SkipOrComplete()
    {
        if (isTyping)
        {
            // Instantly show full text
            if (typingCoroutine != null)
                StopCoroutine(typingCoroutine);

            textComponent.text = currentFullText;
            isTyping = false;
            isCompleted = true;
        }
    }

    public bool IsCompleted()
    {
        return isCompleted;
    }


}