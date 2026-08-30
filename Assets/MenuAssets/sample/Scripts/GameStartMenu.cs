using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class GameStartMenu : MonoBehaviour
{
    [Header("UI Pages")]
    public GameObject mainMenu;
    public GameObject options;
    public GameObject about;
    public GameObject select; // NEW

    [Header("Main Menu Buttons")]
    public Button startButton;
    public Button optionButton;
    public Button aboutButton;
    public Button quitButton;

    [Header("Select Menu Buttons")]
    public List<Button> sceneButtons;   // Assign buttons here
    public List<int> sceneIndexes;      // Matching scene indexes

    public List<Button> returnButtons;

    void Start()
    {
        EnableMainMenu();

        // Main menu buttons
        startButton.onClick.AddListener(EnableSelect);
        optionButton.onClick.AddListener(EnableOption);
        aboutButton.onClick.AddListener(EnableAbout);
        quitButton.onClick.AddListener(QuitGame);

        // Return buttons
        foreach (var item in returnButtons)
        {
            item.onClick.AddListener(EnableMainMenu);
        }

        // Scene buttons
        for (int i = 0; i < sceneButtons.Count; i++)
        {
            int index = sceneIndexes[i]; // avoid closure issue
            sceneButtons[i].onClick.AddListener(() => LoadScene(index));
        }
    }

    public void QuitGame()
    {
        Application.Quit();
    }

    public void LoadScene(int index)
    {
        HideAll();
        SceneTransitionManager.singleton.GoToSceneAsync(index);
    }

    public void HideAll()
    {
        mainMenu.SetActive(false);
        options.SetActive(false);
        about.SetActive(false);
        select.SetActive(false); // NEW
    }

    public void EnableMainMenu()
    {
        mainMenu.SetActive(true);
        options.SetActive(false);
        about.SetActive(false);
        select.SetActive(false);
    }

    public void EnableOption()
    {
        mainMenu.SetActive(false);
        options.SetActive(true);
        about.SetActive(false);
        select.SetActive(false);
    }

    public void EnableAbout()
    {
        mainMenu.SetActive(false);
        options.SetActive(false);
        about.SetActive(true);
        select.SetActive(false);
    }

    public void EnableSelect() // NEW
    {
        mainMenu.SetActive(false);
        options.SetActive(false);
        about.SetActive(false);
        select.SetActive(true);
    }
}