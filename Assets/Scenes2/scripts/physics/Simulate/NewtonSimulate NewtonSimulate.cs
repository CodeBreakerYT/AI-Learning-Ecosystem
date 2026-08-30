using UnityEngine;
using TMPro;
using System.Collections;

public class NewtonSimulate : MonoBehaviour
{
    [Header("Select Law")]
    public bool firstLaw;
    public bool secondLaw;
    public bool thirdLaw;

    [Header("Objects")]
    public Rigidbody obj1;
    public Rigidbody obj2;

    [Header("Hands Setup")]
    public GameObject defaultLeftHand;
    public GameObject defaultRightHand;

    public GameObject physicsLeftHand;
    public GameObject physicsRightHand;

    [Header("UI")]
    public TextMeshProUGUI displayText;

    [Header("Settings")]
    public float motionSpeed = 2f;
    public float pushForce = 5f;
    public float typingSpeed = 0.03f;

    private int stage = 0;
    private int lastStage = -1;
    private bool buttonPressed = false;
    private bool hasStarted = false;
    private bool canPressButton = false;

    private float enterTime = 0f;
    private float inputBlockTime = 1.5f;

    private Coroutine typingCoroutine;
    private bool typingDone = false;

    private Vector3 obj1StartPos;
    private Vector3 obj2StartPos;

    private Quaternion obj1StartRot;
    private Quaternion obj2StartRot;

    private bool handsSwitched = false; // ✅ prevent repeated switching

    // How close the player must walk for this station to start - a plain
    // distance check instead of OnTriggerEnter(Player), which never fired
    // at all: confirmed live that even genuine CharacterController.Move()
    // calls walking the player straight through this object's own trigger
    // volume never invoked OnTriggerEnter, because the player rig uses a
    // CharacterController with no Rigidbody anywhere on it, and neither
    // Unity's regular collision system nor a kinematic Rigidbody added to
    // the trigger volume reliably bridges that gap for CharacterController-
    // driven movement. A polled distance check has no such dependency on
    // Unity's physics/trigger message pipeline at all.
    public float startRadius = 2.5f;
    private Transform _player;

    // Lets an external teacher NPC speak the same line being typed onscreen,
    // without this script needing to know Convai exists.
    public System.Action<string> OnStageText;

    // Fired whenever the stage actually advances - which law and which
    // numbered stage, so an external guide (see PhoboNewtonsGuide) can speak
    // its OWN fuller custom teaching line per stage instead of just echoing
    // the terse on-screen caption text.
    public System.Action<bool, bool, bool, int> OnStageChanged;

    void Start()
    {
        var playerGO = GameObject.FindGameObjectWithTag("Player");
        _player = playerGO != null ? playerGO.transform : null;

        obj1StartPos = obj1.transform.position;
        obj2StartPos = obj2.transform.position;

        obj1StartRot = obj1.transform.rotation;
        obj2StartRot = obj2.transform.rotation;

        SetObjectsStill();

        // Initial hands
        if (firstLaw)
            EnablePhysicsHands();
        else if (secondLaw)
            EnablePhysicsHands(); // ✅ START WITH PHYSICS HANDS
        else
            EnableDefaultHands();
    }

    // ================= HAND CONTROL =================
    void EnableDefaultHands()
    {
        if (defaultLeftHand) defaultLeftHand.SetActive(true);
        if (defaultRightHand) defaultRightHand.SetActive(true);

        if (physicsLeftHand) physicsLeftHand.SetActive(false);
        if (physicsRightHand) physicsRightHand.SetActive(false);
    }

    void EnablePhysicsHands()
    {
        // Only swap if BOTH physics-hand replacements are actually assigned -
        // otherwise this disables the default hands (which carry the ray
        // interactor the player needs to press this very script's own UI
        // buttons) and never turns anything back on. Confirmed live: with
        // physicsLeftHand/physicsRightHand left unassigned (true in every
        // law's current scene setup), this was exactly why the button
        // stopped responding after the very first press - the interactor
        // got switched off with nothing to replace it.
        if (physicsLeftHand == null || physicsRightHand == null) return;

        if (defaultLeftHand) defaultLeftHand.SetActive(false);
        if (defaultRightHand) defaultRightHand.SetActive(false);

        physicsLeftHand.SetActive(true);
        physicsRightHand.SetActive(true);
    }

    // ================= RESET =================
    void ResetObjects()
    {
        obj1.isKinematic = false;
        obj2.isKinematic = false;

        obj1.transform.position = obj1StartPos;
        obj2.transform.position = obj2StartPos;

        obj1.transform.rotation = obj1StartRot;
        obj2.transform.rotation = obj2StartRot;

        obj1.linearVelocity = Vector3.zero;
        obj2.linearVelocity = Vector3.zero;

        obj1.angularVelocity = Vector3.zero;
        obj2.angularVelocity = Vector3.zero;

        obj1.isKinematic = true;
        obj2.isKinematic = true;
    }

    // ================= TRIGGER START =================
    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        BeginStation();
    }

    private void BeginStation()
    {
        if (hasStarted) return;

        hasStarted = true;
        enterTime = Time.time;

        ResetObjects();

        StartCoroutine(StartIntro());
    }

    IEnumerator StartIntro()
    {
        OnStageChanged?.Invoke(firstLaw, secondLaw, thirdLaw, 0);
        yield return StartCoroutine(TypeText(GetStartText()));
        yield return new WaitForSeconds(0.5f);

        canPressButton = true;
    }

    void Update()
    {
        if (!hasStarted)
        {
            if (_player != null && Vector3.Distance(_player.position, transform.position) <= startRadius)
                BeginStation();
            return;
        }

        if (stage == lastStage) return;
        lastStage = stage;
        OnStageChanged?.Invoke(firstLaw, secondLaw, thirdLaw, stage);

        if (firstLaw)
        {
            EnablePhysicsHands();
            FirstLawFlow();
        }
        else if (secondLaw)
        {
            SecondLawFlow();
        }
        else if (thirdLaw)
        {
            EnablePhysicsHands();
            ThirdLawFlow();
        }
    }

    // ================= FIRST LAW =================
    void FirstLawFlow()
    {
        if (stage == 1)
        {
            SetObjectsStill();
            StartTyping("1st Law of Motion\n\nA body at rest remains at rest");
        }
        else if (stage == 2)
        {
            SetObjectsMoving();
            StartTyping("A body in motion remains in motion\n\nunless acted upon by an external force");
        }
        else if (stage == 3)
        {
            StartTyping("External force applied!\n\nWatch the interaction");
            ApplyCollisionForce();
        }
        else if (stage == 4)
        {
            StartTyping("Now push that boulder");
        }
    }

    // ================= SECOND LAW =================
    void SecondLawFlow()
    {
        // ✅ Always keep physics hands until final stage
        if (!handsSwitched)
            EnablePhysicsHands();

        if (stage == 1)
        {
            StartTyping("2nd Law of Motion\n\nForce depends on mass and acceleration");
        }
        else if (stage == 2)
        {
            StartTyping("Heavier objects need more force\n\nto move the same way");
        }
        else if (stage == 3)
        {
            StartTyping("Place car and truck on seesaw\n\nSee their weight difference");

            StartCoroutine(SwitchHandsAfterTyping());
        }
    }


    IEnumerator SwitchHandsAfterTyping()
    {
        while (!typingDone)
            yield return null;

        EnableDefaultHands(); // ✅ SWITCH HERE
        handsSwitched = true;
    }

    // ================= COMMON =================
    string GetStartText()
    {
        if (firstLaw) return "1st Law of Motion\n\nPress button to begin";
        if (secondLaw) return "2nd Law of Motion\n\nPress button to begin";
        if (thirdLaw) return "3rd Law of Motion\n\nPress button to begin";

        return "Press button to begin";
    }

    void SetObjectsStill()
    {
        // Setting linearVelocity while already kinematic logs a Unity
        // warning every call - only meaningful before the kinematic flip.
        if (!obj1.isKinematic) obj1.linearVelocity = Vector3.zero;
        if (!obj2.isKinematic) obj2.linearVelocity = Vector3.zero;

        obj1.isKinematic = true;
        obj2.isKinematic = true;
    }

    void SetObjectsMoving()
    {
        obj1.isKinematic = false;
        obj2.isKinematic = false;

        obj1.linearVelocity = Vector3.forward * motionSpeed;
        obj2.linearVelocity = Vector3.back * motionSpeed;
    }

    void ApplyCollisionForce()
    {
        obj1.isKinematic = false;
        obj2.isKinematic = false;

        obj1.linearVelocity = Vector3.forward * motionSpeed;

        StartCoroutine(WaitAndPush());
    }

    IEnumerator WaitAndPush()
    {
        yield return new WaitForSeconds(1f);
        obj2.AddForce(Vector3.forward * pushForce, ForceMode.Impulse);
    }

    // ================= TYPEWRITER =================
    void StartTyping(string msg)
    {
        if (typingCoroutine != null)
            StopCoroutine(typingCoroutine);

        typingCoroutine = StartCoroutine(TypeText(msg));
        OnStageText?.Invoke(msg);
    }

    IEnumerator TypeText(string msg)
    {
        typingDone = false;

        displayText.text = "";

        foreach (char c in msg)
        {
            displayText.text += c;
            yield return new WaitForSeconds(typingSpeed);
        }

        typingDone = true;
    }

    // ================= BUTTON =================
    public void OnButtonPressed()
    {
        if (Time.time < enterTime + inputBlockTime) return;
        if (!hasStarted) return;
        if (!canPressButton) return;
        if (buttonPressed) return;

        buttonPressed = true;
        stage++;

        Invoke(nameof(ResetButton), 0.4f);
    }

    void ResetButton()
    {
        buttonPressed = false;
    }


    void ThirdLawFlow()
    {
        // ✅ Always physics hands for 3rd law
        EnablePhysicsHands();

        if (stage == 1)
        {
            StartTyping("3rd Law of Motion\n\nEvery action has an equal and opposite reaction");
        }
        else if (stage == 2)
        {
            StartTyping("Action Force = Reaction Force");
        }
        else if (stage == 3)
        {
            StartTyping("Watch closely\n\nBoth objects will push each other equally");

            StartCoroutine(ApplyThirdLawForce());
        }
    }

    IEnumerator ApplyThirdLawForce()
    {
        // wait until typing finishes
        while (!typingDone)
            yield return null;

        yield return new WaitForSeconds(0.5f);

        obj1.isKinematic = false;
        obj2.isKinematic = false;

        // ✅ push in LOCAL forward directions
        obj1.AddForce(obj1.transform.forward * pushForce, ForceMode.Impulse);
        obj2.AddForce(-obj2.transform.forward * pushForce, ForceMode.Impulse);
    }
}