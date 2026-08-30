using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using AILearningEcosystem.Learning;
using static AILearningEcosystem.Hub.CanvasUIHelpers;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Hub.unity's entire content: nothing but a subject -> category picker,
    /// mirroring EcoLearn's own Learn page (see EcoLearn/frontend/scripts/
    /// learn/learn.js): pick a subject, then a category, and that category's
    /// minigame scene loads. This scene has no other content - login (in
    /// StartScene) loads straight here, and there is no separate free-roam
    /// "World" to distinguish it from. Every subject has three real minigame
    /// scenes under Assets/PlatformScenes/{Math,Physics,Chemistry}/.
    /// Physics's first category, "Newton's Laws of Motion", loads the real
    /// ported Lumora Physics scene instead of a custom minigame, since that
    /// scene's own simulation/puzzle content already covers it well;
    /// Electricity and Levers are original minigames. If a Convai guide NPC
    /// is present (see ConvaiGuide.cs - "Convai NPC Amelia" if one was added
    /// via Tools > AI Learning Ecosystem > Add AI Tutor To Open Scene), she
    /// narrates each step. Degrades gracefully (does nothing) if no guide is
    /// present or configured.
    ///
    /// [ExecuteAlways] - builds and persists as real, edit-time-visible scene
    /// content (same "build once, rediscover after" pattern as every other
    /// procedurally-built object in this project) instead of only existing
    /// once Play mode starts. "make these permanently visible in scene view,
    /// don't spawn via code" - the canvas/panels are still BUILT via code
    /// (there's no art-authored alternative), but only the very first time;
    /// after that they're real GameObjects saved in Hub.unity like anything
    /// else, editable/movable in the Scene view with no Play required.
    /// </summary>
    [ExecuteAlways]
    public class HubBootstrap : MonoBehaviour
    {
        private enum Subject { Math, Physics, Chemistry }

        private static readonly Color MathAccent = new Color(0.357f, 0.549f, 1f);
        private static readonly Color PhysicsAccent = new Color(0.133f, 0.827f, 0.933f);
        private static readonly Color ChemistryAccent = new Color(0.655f, 0.545f, 0.98f);

        private GameObject _subjectScreen;
        private GameObject _categoryScreen;
        private GameObject _progressScreen;
        private Transform _categoryContent;
        private TMP_Text _categoryHeading;
        private bool _runtimeStarted;

        private void Awake()
        {
            if (transform.Find("Learn Hub Canvas") == null)
                BuildUI();
            else
                RediscoverReferences();

            WireInteractions();
        }

        // Runtime Button.onClick.AddListener() calls are NOT serialized into
        // the scene file - only Inspector-configured "persistent calls" are.
        // Before this became a persist-once-and-rediscover component, that
        // never mattered because BuildUI() (and every AddListener() call
        // inside it) ran fresh every single Play session. Now that the
        // canvas is real, saved scene content, RediscoverReferences() finds
        // the exact same button GameObjects but their click handlers are
        // gone - confirmed live ("unable to click on any buttons") the very
        // first Play session after saving the persisted canvas. Re-wiring
        // unconditionally here (whether Awake() just built everything fresh
        // or found it already there) is the actual fix; RemoveAllListeners
        // first makes it safe to call more than once.
        private void WireInteractions()
        {
            if (_subjectScreen != null)
            {
                Wire(_subjectScreen.transform, "Button_MATH", () => ShowCategoryScreen(Subject.Math));
                Wire(_subjectScreen.transform, "Button_PHYSICS", () => ShowCategoryScreen(Subject.Physics));
                Wire(_subjectScreen.transform, "Button_CHEMISTRY", () => ShowCategoryScreen(Subject.Chemistry));
                Wire(_subjectScreen.transform, "Button_MY PROGRESS", ShowProgressScreen);
                Wire(_subjectScreen.transform, "Button_< BACK TO START", () => SceneManager.LoadScene("StartScene"));
            }
            if (_categoryScreen != null)
                Wire(_categoryScreen.transform, "Button_< BACK TO SUBJECTS", ShowSubjectScreen);
            if (_progressScreen != null)
                Wire(_progressScreen.transform, "Button_< BACK", ShowSubjectScreen);
        }

        private static void Wire(Transform root, string buttonPath, UnityEngine.Events.UnityAction action)
        {
            var button = root.Find(buttonPath)?.GetComponent<Button>();
            if (button == null) return;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }

        // Deliberately separate from Awake(): [ExecuteAlways] means Awake()
        // can fire while Application.isPlaying hasn't actually settled yet
        // during the edit-to-play transition (confirmed as a real bug
        // elsewhere in this project - PhoboNewtonsGuide's runtime kickoff
        // silently never ran when this same check lived in Awake() instead).
        private void Start()
        {
            if (!Application.isPlaying || _runtimeStarted) return;
            _runtimeStarted = true;
            ShowSubjectScreen();
            ConvaiGuide.Speak("Welcome! Pick a subject to begin - Math, Physics, or Chemistry.");
        }

        /// <summary>Editor "Rebuild" button entry point - tears down and rebuilds the whole canvas from scratch.</summary>
        public void Rebuild()
        {
            var existing = transform.Find("Learn Hub Canvas");
            if (existing != null) SafeDestroy(existing.gameObject);
            BuildUI();
        }

        private static void SafeDestroy(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        private void RediscoverReferences()
        {
            var canvas = transform.Find("Learn Hub Canvas");
            _subjectScreen = canvas.Find("Subject Screen")?.gameObject;
            _categoryScreen = canvas.Find("Category Screen")?.gameObject;
            _progressScreen = canvas.Find("Progress Screen")?.gameObject;
            _categoryHeading = _categoryScreen != null ? _categoryScreen.transform.Find("Text")?.GetComponent<TMP_Text>() : null;
            _categoryContent = _categoryScreen != null ? _categoryScreen.transform.Find("Category Content") : null;
        }

        // Music plays for the whole time the player is choosing a subject/
        // category, not just the very first screen - see SceneMusic on the
        // scene's own "Music" GameObject, which starts in Awake() regardless
        // of which of this script's two screens ends up showing.

        // ------------------------------------------------------------------
        // UI shell
        // ------------------------------------------------------------------

        private void BuildUI()
        {
            var canvasGO = new GameObject("Learn Hub Canvas", typeof(RectTransform));
            canvasGO.transform.SetParent(transform, false);
            PositionCanvasInFrontOfPlayer(canvasGO.transform);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<TrackedDeviceGraphicRaycaster>();
            var rect = canvasGO.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(900, 560);
            canvasGO.transform.localScale = Vector3.one * 0.0011f;

            _subjectScreen = BuildSubjectScreen(canvasGO.transform);
            _categoryScreen = BuildCategoryScreenShell(canvasGO.transform);
            _progressScreen = SkillProfilePanel.Build(canvasGO.transform, ShowSubjectScreen);
            _progressScreen.name = "Progress Screen";

            // Only the subject screen should actually be visible right after
            // building (in the Editor, or the first time this ever runs in
            // Play) - the other two exist as real persisted GameObjects now
            // too, just inactive until navigated to.
            ShowOnly(_subjectScreen);
        }

        // The canvas used to be positioned relative to this GameObject's own
        // (arbitrary, origin-ish) transform, which landed it barely a meter
        // from wherever the player rig actually spawns - close enough to
        // feel like it was shoved in your face. Position it relative to the
        // real player rig instead, at a normal reading distance, so it's the
        // same comfortable placement the very first time it appears and the
        // whole time the player is choosing a subject/category afterward -
        // nothing here ever moves it again once built.
        private static void PositionCanvasInFrontOfPlayer(Transform canvasTransform)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            Vector3 pos;
            float yaw;
            if (player != null)
            {
                pos = player.transform.position + player.transform.forward * 2.2f + Vector3.up * 1.6f;
                yaw = player.transform.eulerAngles.y;
            }
            else
            {
                pos = new Vector3(0f, 1.6f, 2f);
                yaw = 0f;
            }
            canvasTransform.position = pos;
            // Flat Y-only rotation, same convention every other world-space
            // canvas in this project uses (e.g. StartSceneNav's panel) - the
            // previous LookRotation-toward-player formula could pick up
            // pitch/roll from the player's forward vector and render the
            // panel flipped/upside-down (confirmed live).
            //
            // The +180 here (matching the panel facing the player) turned out
            // to be the wrong face - confirmed live via screenshot the whole
            // canvas (heading, buttons, blurbs) rendered mirrored/backward,
            // the same double-sided-quad "reading it through the back"
            // mirroring documented on MathCannon's trig table. Dropping the
            // +180 shows the correct face without touching any child content.
            canvasTransform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        private GameObject BuildSubjectScreen(Transform parent)
        {
            // No background art - the busy HUD sheet (gauges/bar charts/map)
            // read as clutter competing with the real buttons and text on
            // top of it, not ambient texture ("remove that bg" - confirmed
            // live, it was legible enough to be distracting rather than
            // subtle). Plain dark chamfered panel + glowing frame only.
            var panel = CreateSciFiPanel(parent, Vector2.zero, new Vector2(900, 560));
            panel.gameObject.name = "Subject Screen";
            var title = CreateText(panel.transform, "CHOOSE A SUBJECT", 36, SciFiGlowCore, TextAlignmentOptions.Center,
                new Vector2(0, 230), new Vector2(700, 60));
            title.fontStyle = FontStyles.Bold;
            title.characterSpacing = 3f;

            var subjects = new (Subject id, string label, string subjectKey, Color accent)[]
            {
                (Subject.Math, "MATH", "Mathematics", MathAccent),
                (Subject.Physics, "PHYSICS", "Physics", PhysicsAccent),
                (Subject.Chemistry, "CHEMISTRY", "Chemistry", ChemistryAccent)
            };

            for (var i = 0; i < subjects.Length; i++)
            {
                var s = subjects[i];
                var anchoredX = (i - 1) * 290;
                CreateSciFiButton(panel.transform, s.label, s.accent,
                    new Vector2(anchoredX, 80), new Vector2(260, 90), () => ShowCategoryScreen(s.id), 24);
                // 18pt read as genuinely blurry at this canvas's small
                // world-space scale/viewing distance - not a rendering bug,
                // just too few actual on-screen pixels per glyph at that
                // size (the 24-36pt title/buttons on the same canvas stayed
                // crisp). Bumped rather than shrinking the panel/scale.
                CreateText(panel.transform, PersonalizedBlurb(s.subjectKey), 22, TextDimColor, TextAlignmentOptions.Center,
                    new Vector2(anchoredX, -30), new Vector2(260, 140));
            }

            // "Enter the Adventure World" removed - "no need for that, only
            // subjects" - this Hub is subject-picking only now, so My
            // Progress is centered instead of sharing the row with it.
            CreateSciFiButton(panel.transform, "MY PROGRESS", SciFiFrameColor,
                new Vector2(0, -190), new Vector2(280, 56), ShowProgressScreen);

            // Subject Select had no way back to the login screen at all -
            // the only exit was forward into a subject.
            CreateSciFiButton(panel.transform, "< BACK TO START", SciFiTextDim,
                new Vector2(0, -250), new Vector2(280, 50), () => SceneManager.LoadScene("StartScene"));

            return panel.gameObject;
        }

        // Replaces the old hardcoded per-subject minigame list with a line
        // reflecting what this learner has actually done - the same adaptive
        // level data SkillProfilePanel shows in full, condensed to one line
        // so the picker itself hints at where the player stands before they
        // even open "My Progress".
        private static string PersonalizedBlurb(string subjectKey)
        {
            SkillProfilePanel.SubjectEntry entry = default;
            foreach (var s in SkillProfilePanel.Subjects)
                if (s.subject == subjectKey) { entry = s; break; }
            if (entry.games == null || entry.games.Length == 0) return "";

            if (GameManager.Instance == null)
                return $"{entry.games.Length} minigames - sign in to track your progress.";

            var total = 0;
            foreach (var game in entry.games)
                total += GameManager.Instance.Difficulty.CurrentLevel(subjectKey, game.minigameId);
            var average = (float)total / entry.games.Length;

            return Mathf.Approximately(average, 1f)
                ? $"New here - {entry.games.Length} minigames to try."
                : $"Avg. level {average:0.0}/5 across {entry.games.Length} minigames.";
        }

        private GameObject BuildCategoryScreenShell(Transform parent)
        {
            // No background art - the busy HUD sheet (gauges/bar charts/map)
            // read as clutter competing with the real buttons and text on
            // top of it, not ambient texture ("remove that bg" - confirmed
            // live, it was legible enough to be distracting rather than
            // subtle). Plain dark chamfered panel + glowing frame only.
            var panel = CreateSciFiPanel(parent, Vector2.zero, new Vector2(900, 560));
            panel.gameObject.name = "Category Screen";
            _categoryHeading = CreateText(panel.transform, "", 32, SciFiGlowCore, TextAlignmentOptions.Center,
                new Vector2(0, 220), new Vector2(700, 80));

            var contentGO = new GameObject("Category Content", typeof(RectTransform));
            contentGO.transform.SetParent(panel.transform, false);
            _categoryContent = contentGO.transform;

            CreateSciFiButton(panel.transform, "< BACK TO SUBJECTS", SciFiTextDim,
                new Vector2(0, -240), new Vector2(280, 50), ShowSubjectScreen);

            return panel.gameObject;
        }

        private void ShowOnly(GameObject target)
        {
            _subjectScreen.SetActive(target == _subjectScreen);
            _categoryScreen.SetActive(target == _categoryScreen);
            _progressScreen.SetActive(target == _progressScreen);
        }

        private void ShowProgressScreen() => ShowOnly(_progressScreen);

        private void ShowSubjectScreen()
        {
            ShowOnly(_subjectScreen);
        }

        // ------------------------------------------------------------------
        // Category screen content (rebuilt per subject) - each category is a
        // real minigame scene under Assets/PlatformScenes/{Subject}/.
        // ------------------------------------------------------------------

        private void ShowCategoryScreen(Subject subject)
        {
            foreach (Transform child in _categoryContent) Destroy(child.gameObject);

            switch (subject)
            {
                case Subject.Math:
                    _categoryHeading.text = "Math - pick a minigame";
                    BuildCategoryButton(0, "Math Cannon", MathAccent, () => LoadMinigame("MathCannon", "Let's fire up the math cannon!"));
                    BuildCategoryButton(1, "Shooting Range", MathAccent, () => LoadMinigame("MathShootingRange", "Let's hit the shooting range!"));
                    BuildCategoryButton(2, "Meet Mr. Sharma (Math Teacher)", GhostButtonColor, () => SceneManager.LoadScene("MathClassroom"));
                    ConvaiGuide.Speak("Great choice - Math! Pick a minigame: Math Cannon or Shooting Range.");
                    break;
                case Subject.Chemistry:
                    _categoryHeading.text = "Chemistry - pick a minigame";
                    BuildCategoryButton(0, "Molecule Builder", ChemistryAccent, () => LoadMinigame("ForestChemistryMinigame", "Let's build some molecules!"));
                    BuildCategoryButton(1, "Chemical Reaction Lab", ChemistryAccent, () => LoadMinigame("ChemicalReactionLab", "Let's run some reactions!"));
                    BuildCategoryButton(2, "Meet Mr. Rao (Chemistry Teacher)", GhostButtonColor, () => SceneManager.LoadScene("ChemistryClassroom"));
                    ConvaiGuide.Speak("Chemistry it is! Pick a minigame: Molecule Builder or Chemical Reaction Lab.");
                    break;
                default:
                    _categoryHeading.text = "Physics - pick a minigame";
                    BuildCategoryButton(0, "Projectile Launcher", PhysicsAccent, () => LoadMinigame("ProjectileLauncher", "Let's launch some projectiles!"));
                    BuildCategoryButton(1, "Newton's Laws of Motion", PhysicsAccent, () => LoadMinigame("NewtonsLaws", "Let's head to the physics lab to explore Newton's Laws of Motion."));
                    BuildCategoryButton(2, "Meet Mrs. Iyer (Physics Teacher)", GhostButtonColor, () => SceneManager.LoadScene("PhysicsClassroom"));
                    ConvaiGuide.Speak("Physics! Pick a minigame: Projectile Launcher or Newton's Laws of Motion.");
                    break;
            }

            ShowOnly(_categoryScreen);
        }

        private void BuildCategoryButton(int index, string label, Color accent, UnityEngine.Events.UnityAction onClick)
        {
            var anchoredY = 130 - index * 80;
            CreateSciFiButton(_categoryContent, label.ToUpperInvariant(), accent,
                new Vector2(0, anchoredY), new Vector2(420, 60), onClick);
        }

        private static void LoadMinigame(string sceneName, string speakMessage)
        {
            ConvaiGuide.Speak(speakMessage);
            SceneManager.LoadScene(sceneName);
        }
    }
}
