using UnityEngine;
using UnityEngine.XR;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

public class MonkeyDialogue : MonoBehaviour
{
    public TypewriterText typewriter;
    public FruitBasket basket;

    private bool playerInZone = false;
    private bool dialogueStarted = false;

    private int step = 0;

    private InputDevice rightController;
    private bool wasPressedLastFrame = false;

    private enum Phase
    {
        Tutorial,
        Riddle1,
        Riddle2,
        Completed
    }

    private Phase phase = Phase.Tutorial;

    void Start()
    {
        GetRightController();
    }

    void GetRightController()
    {
        List<InputDevice> devices = new List<InputDevice>();
        InputDevices.GetDevicesAtXRNode(XRNode.RightHand, devices);

        if (devices.Count > 0)
            rightController = devices[0];
    }

    void Update()
    {
        if (!rightController.isValid)
        {
            GetRightController();
            return;
        }

        if (!playerInZone || !dialogueStarted)
            return;

        bool pressed;
        rightController.TryGetFeatureValue(CommonUsages.primaryButton, out pressed);

        // Detect single press
        if (pressed && !wasPressedLastFrame)
        {
            HandleInput();
        }

        wasPressedLastFrame = pressed;
    }

    void HandleInput()
    {
        // 🔹 Tutorial progression
        if (phase == Phase.Tutorial)
        {
            AdvanceTutorial();
        }
        else
        {
            // 🔹 Riddle phases → always check on A press
            CheckAnswers();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInZone = true;

            // ❗ Only start once, never reset step
            if (!dialogueStarted)
            {
                dialogueStarted = true;
                AdvanceTutorial(); // start only first time
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInZone = false;

            // ❗ Do NOT reset dialogueStarted
            // This ensures progress is preserved
        }
    }


    void AdvanceTutorial()
    {
        step++;

        switch (step)
        {
            case 1:
                typewriter.ShowText(
                    "Hee hee! Haa haa! \n" +
                    "x = bananas\n" +
                    "y = apples"
                );
                break;

            case 2:
                typewriter.ShowText(
                    "Equation:\n" +
                    "x + y = total fruits\n" +
                    "Here apples are fixed = 6\n" +
                    "So:\n" +
                    "x + 6 = 12"
                );
                break;

            case 3:
                typewriter.ShowText(
                    "Example:\n" +
                    "x + 6 = 12\n" +
                    "x = 6 bananas"
                );
                break;

            case 4:
                typewriter.ShowText(
                    "Riddle!\n" +
                    "Total fruits = 12\n" +
                    "Apples = 6\n" +
                    "Solve:\n" +
                    "x + 6 = 12\n" +
                    "Place bananas in the basket\n" +
                    "Press A to check your answer."
                );

                phase = Phase.Riddle1;
                break;
        }
    }

    void CheckAnswers()
    {
        typewriter.StopAllCoroutines();

        if (phase == Phase.Riddle1)
        {
            int correctBananas = 12 - 6;
            int currentBananas = basket.bananaCount;

            if (currentBananas == correctBananas)
            {
                typewriter.ShowText("Correct! \nWell done!\nThank you for solving the riddle.");

                // Load menu after delay
                Invoke(nameof(LoadMenu), 3f);
            }
            else
            {
                int needed = correctBananas - currentBananas;

                typewriter.ShowText(
                    "Try again.. \n" +
                    "You need " + correctBananas + " bananas total.\n" +
                    "You currently have " + currentBananas + ".\n" +
                    "Add " + Mathf.Abs(needed) + " more bananas "
                );
            }
        }
        else if (phase == Phase.Riddle2)
        {
            int correctOranges = 10 - 2;
            int currentOranges = basket.orangeCount;

            if (currentOranges == correctOranges)
            {
                typewriter.ShowText("Correct! \nAll puzzles solved!\nThank you for playing.");

                Invoke(nameof(LoadMenu), 3f);
            }
            else
            {
                int needed = correctOranges - currentOranges;

                typewriter.ShowText(
                    "Try again.. \n" +
                    "You need " + correctOranges + " oranges total.\n" +
                    "You currently have " + currentOranges + ".\n" +
                    "Add " + Mathf.Abs(needed) + " more oranges "
                );
            }
        }
    }

    void LoadMenu()
    {
        SceneManager.LoadScene("LumoraMenu"); // 🔥 make sure scene name matches
    }
}