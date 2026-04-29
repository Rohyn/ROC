using ROC.Session;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class CharacterSelectContinueButton : MonoBehaviour
{
    [SerializeField] private Button button;

    private void Awake()
    {
        if (button == null)
        {
            button = GetComponent<Button>();
        }
    }

    public void Continue()
    {
        if (button != null)
        {
            button.interactable = false;
        }

        if (PlayerSession.Local == null)
        {
            Debug.LogWarning("[CharacterSelectContinueButton] No local PlayerSession found.");

            if (button != null)
            {
                button.interactable = true;
            }

            return;
        }

        PlayerSession.Local.RequestContinueWithDefaultCharacter();
    }
}