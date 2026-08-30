using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.UI;
using AILearningEcosystem.Learning;
using static AILearningEcosystem.Hub.CanvasUIHelpers;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Chemistry Minigame 1 - "Molecule Builder" (design doc section 5.1).
    /// Grab atoms off the bench and bring two within bonding range - a bond
    /// forms as a physical stick only if both atoms still have valence left,
    /// and the completed molecule becomes a single grabbable 3D object you
    /// can pick up and inspect. Replaces the old "select the right atoms in
    /// a pool" click mechanic entirely.
    /// </summary>
    [ExecuteAlways]
    public class ChemistryMoleculeGame : MonoBehaviour, IMinigame
    {
        public enum Topic { Diatomic, Compounds, AcidsBases }

        public string MinigameId => "MoleculeBuilder";
        public string Subject => "Chemistry";

        private class Recipe
        {
            public string Formula;
            public string Name;
            public string Fact;
            public (string a, string b)[] RequiredBonds;
            public Topic Topic;
        }

        private static readonly Dictionary<string, Color> ElementColors = new Dictionary<string, Color>
        {
            { "H", new Color(0.96f, 0.97f, 0.98f) },
            { "O", new Color(0.97f, 0.44f, 0.44f) },
            { "C", new Color(0.29f, 0.33f, 0.39f) },
            { "N", new Color(0.36f, 0.55f, 1f) },
            { "Na", new Color(0.66f, 0.6f, 0.95f) },
            { "Cl", new Color(0.4f, 0.85f, 0.45f) }
        };

        // Simplified single-bond-slot valence, sufficient for these teaching
        // molecules - see the doc comment on ChemistryMoleculeGame in the
        // design spec for why this doesn't attempt to model true double bonds.
        private static readonly Dictionary<string, int> Valence = new Dictionary<string, int>
        {
            { "H", 1 }, { "O", 2 }, { "C", 4 }, { "N", 3 }, { "Na", 1 }, { "Cl", 1 }
        };

        private static readonly Dictionary<Topic, string[]> TopicElements = new Dictionary<Topic, string[]>
        {
            { Topic.Diatomic, new[] { "H", "O" } },
            { Topic.Compounds, new[] { "H", "O", "C", "N" } },
            { Topic.AcidsBases, new[] { "H", "O", "Na", "Cl" } }
        };

        // Each entry names two ATOM INSTANCES, not just two elements - "H1"
        // and "H2" are two distinct hydrogens, both satisfied by any real H
        // atom the player bonds in (the digit is stripped for matching, see
        // StripDigits). This is what makes CollapseToUniqueAtoms and the
        // bond-completion check unambiguous: without instance labels, CO2's
        // [(C,O),(C,O)] and water's [(H,O),(H,O)] look identical as raw
        // element pairs despite needing a different atom count (CO2 needs a
        // second, separate oxygen; water reuses its one oxygen's second
        // valence slot on a second hydrogen) - a real bug this used to hit
        // (a previous count-based heuristic silently produced 0 required
        // atoms for every recipe, confirmed live as "atoms missing from the
        // bench").
        private static readonly Recipe[] Recipes =
        {
            new Recipe { Formula = "H2", Name = "Hydrogen gas", Topic = Topic.Diatomic,
                RequiredBonds = new[] { ("H1", "H2") },
                Fact = "The lightest, most abundant element in the universe - and flammable." },
            new Recipe { Formula = "O2", Name = "Oxygen gas", Topic = Topic.Diatomic,
                RequiredBonds = new[] { ("O1", "O2") },
                Fact = "The gas in the air you're breathing right now." },
            new Recipe { Formula = "H2O", Name = "Water", Topic = Topic.Compounds,
                RequiredBonds = new[] { ("H1", "O1"), ("H2", "O1") },
                Fact = "Bent shape (104.5 degrees) - that's why water is polar." },
            new Recipe { Formula = "CO2", Name = "Carbon dioxide", Topic = Topic.Compounds,
                RequiredBonds = new[] { ("C1", "O1"), ("C1", "O2") },
                Fact = "Linear molecule - you exhale it with every breath." },
            new Recipe { Formula = "NH3", Name = "Ammonia", Topic = Topic.Compounds,
                RequiredBonds = new[] { ("N1", "H1"), ("N1", "H2"), ("N1", "H3") },
                Fact = "Pyramid shape - the lone pair pushes the H atoms down." },
            new Recipe { Formula = "CH4", Name = "Methane", Topic = Topic.Compounds,
                RequiredBonds = new[] { ("C1", "H1"), ("C1", "H2"), ("C1", "H3"), ("C1", "H4") },
                Fact = "A perfect tetrahedron - the main gas in natural gas, and it burns." },
            new Recipe { Formula = "HCl", Name = "Hydrochloric acid", Topic = Topic.AcidsBases,
                RequiredBonds = new[] { ("H1", "Cl1") },
                Fact = "A strong acid - your stomach uses dilute HCl to digest food." },
            new Recipe { Formula = "NaOH", Name = "Sodium hydroxide", Topic = Topic.AcidsBases,
                RequiredBonds = new[] { ("Na1", "O1"), ("O1", "H1") },
                Fact = "A strong base, also called lye - used to make soap." },
            new Recipe { Formula = "NaCl", Name = "Table salt", Topic = Topic.AcidsBases,
                RequiredBonds = new[] { ("Na1", "Cl1") },
                Fact = "An acid and a base neutralize each other into this - ordinary salt." }
        };

        private static string StripDigits(string instanceLabel)
        {
            var i = 0;
            while (i < instanceLabel.Length && !char.IsDigit(instanceLabel[i])) i++;
            return instanceLabel.Substring(0, i);
        }

        private const float BenchZOffset = 2.2f;
        private const int TotalRounds = 5;

        public Action onComplete;
        public GameObject atomPrefab;
        public AudioClip bondSound;

        [Header("Completion effects (ported/reused from the project's existing VFX packs)")]
        public GameObject waterPourEffectPrefab;   // JMO CFXR Water Splash (Smaller) - H2O
        public GameObject gasEffectPrefab;         // Hovl Studio Smoke vortex - O2
        public GameObject fireEffectPrefab;        // JMO CFXR Fire - H2 (flammable)

        private Topic _topic;
        private Recipe[] _topicRecipes;
        private TMP_Text _questionText;
        private TMP_Text _feedbackText;
        private readonly List<Atom> _pool = new List<Atom>();
        private List<(string a, string b)> _remainingBonds = new List<(string, string)>();

        private Recipe _current;
        private int _round;
        private int _score;
        private int _mistakesThisTask;
        private string _lastAttempt = "";
        private float _taskStartTime;
        private bool _roundActive;

        private void Awake()
        {
            if (transform.Find("Chemistry Game Canvas") == null)
                BuildStatic();
            else
                RediscoverReferences();
        }

        public void InitializeGame(int startingLevel) { }

        public void StartWith(Topic topic)
        {
            _topic = topic;
            _topicRecipes = Recipes.Where(r => r.Topic == topic).ToArray();
            _round = 0;
            _score = 0;
            GameManager.Instance?.StartMinigameSession(this);
            StartGame();
        }

        public void StartGame() => NextRecipe();

        // ---- Build (edit-mode safe, persists in the scene) ----

        public void Rebuild()
        {
            ClearBuilt();
            BuildStatic();
        }

        private void ClearBuilt()
        {
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }
            _pool.Clear();
        }

        private void BuildStatic()
        {
            BuildHud();
            // Edit-mode/initial preview - a sample bench so the layout is
            // visible before any round actually starts.
            var preview = Recipes.First(r => r.Topic == Topic.Diatomic);
            _questionText.text = $"Build: {preview.Formula} ({preview.Name})";
            _feedbackText.text = "Grab atoms and bring two close together to bond them.";
            BuildAtomBenchFor(preview);
        }

        private void RediscoverReferences()
        {
            var panel = transform.Find("Chemistry Game Canvas/Panel");
            _questionText = panel != null ? panel.Find("Question")?.GetComponent<TMP_Text>() : null;
            _feedbackText = panel != null ? panel.Find("Feedback")?.GetComponent<TMP_Text>() : null;

            _pool.Clear();
            foreach (Transform child in transform)
            {
                var atom = child.GetComponent<Atom>();
                if (atom != null) _pool.Add(atom);
            }
        }

        public static string TopicLabel(Topic topic)
        {
            switch (topic)
            {
                case Topic.Diatomic: return "Diatomic Molecules";
                case Topic.Compounds: return "Everyday Compounds";
                default: return "Acids & Bases";
            }
        }

        private void BuildHud()
        {
            var canvasGO = new GameObject("Chemistry Game Canvas", typeof(RectTransform));
            canvasGO.transform.SetParent(transform, false);
            // Clear of the atom bench's floating/bobbing circle (atoms sit at
            // y=1.3, z=BenchZOffset, spread up to ~1.2m either side) - up and
            // back instead of low and in front, where the panel used to
            // visually clip into the atom cluster.
            canvasGO.transform.localPosition = new Vector3(0f, 2.6f, BenchZOffset + 0.6f);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<TrackedDeviceGraphicRaycaster>();
            var rect = canvasGO.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(560, 220);
            canvasGO.transform.localScale = Vector3.one * 0.003f;

            var panel = CreatePanel(canvasGO.transform, Vector2.zero, new Vector2(560, 220), PanelColor);
            _questionText = CreateText(panel.transform, "", 30, TextColor, TextAlignmentOptions.Center,
                new Vector2(0, 40), new Vector2(520, 90), "Question");
            _feedbackText = CreateText(panel.transform, "", 20, TextDimColor, TextAlignmentOptions.Center,
                new Vector2(0, -60), new Vector2(520, 60), "Feedback");
        }

        private void NextRecipe()
        {
            _round++;
            ClearPool();

            if (_topicRecipes == null || _round > TotalRounds || _round > _topicRecipes.Length)
            {
                _questionText.text = "Complete!";
                _feedbackText.text = $"Score: {_score} / {Mathf.Min(TotalRounds, _topicRecipes?.Length ?? 0)}";
                MinigameEnvironment.PlayRoundCompleteVfx(_questionText.transform.position);
                GameManager.Instance?.EndMinigameSession();
                onComplete?.Invoke();
                return;
            }

            _current = _topicRecipes[(_round - 1) % _topicRecipes.Length];
            _remainingBonds = _current.RequiredBonds.ToList();
            _mistakesThisTask = 0;
            _lastAttempt = "";
            _taskStartTime = Time.time;
            _questionText.text = $"Build: {_current.Formula} ({_current.Name})";
            _feedbackText.text = "Grab atoms and bring two close together to bond them.";
            _roundActive = true;

            BuildAtomBenchFor(_current);
        }

        // Bonded child atoms reparent onto whichever atom they docked onto
        // (Atom.TryBond), so only atoms still directly under this game's own
        // transform are roots - destroying a root also destroys every atom
        // (and bond stick) parented under it.
        private void ClearPool()
        {
            foreach (var atom in _pool)
                if (atom != null && atom.transform.parent == transform)
                {
                    if (Application.isPlaying) Destroy(atom.gameObject); else DestroyImmediate(atom.gameObject);
                }
            _pool.Clear();
        }

        private void BuildAtomBenchFor(Recipe recipe)
        {
            var needed = CollapseToUniqueAtoms(recipe.RequiredBonds);
            var distractorElements = TopicElements.TryGetValue(recipe.Topic, out var els) ? els : TopicElements[Topic.Diatomic];
            var spawnList = new List<string>(needed);
            while (spawnList.Count < needed.Count + 2)
                spawnList.Add(distractorElements[UnityEngine.Random.Range(0, distractorElements.Length)]);

            for (var i = 0; i < spawnList.Count; i++)
            {
                // A small real crystal prop stands in for an atom - color-
                // tinted per element - instead of a bare primitive sphere.
                var symbol = spawnList[i];
                GameObject atomGO;
                if (atomPrefab != null)
                {
                    atomGO = Instantiate(atomPrefab, transform);
                    atomGO.name = $"Atom {symbol}";
                    atomGO.transform.localScale = Vector3.one * 0.32f; // was 0.16 - "bigger" ask
                    // The crystal's own mesh collider is concave - a dynamic
                    // Rigidbody (added below) requires a convex one.
                    foreach (var mc in atomGO.GetComponentsInChildren<MeshCollider>())
                        mc.convex = true;
                }
                else
                {
                    atomGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    atomGO.name = $"Atom {symbol}";
                    atomGO.transform.SetParent(transform, false);
                    atomGO.transform.localScale = Vector3.one * 0.5f; // was 0.3
                }
                // Wider spacing to match the bigger atoms, and a bit higher
                // off the ground so the bobbing float reads clearly.
                var x = (i - (spawnList.Count - 1) / 2f) * 0.8f;
                atomGO.transform.localPosition = new Vector3(x, 1.3f, BenchZOffset);

                var rb = atomGO.AddComponent<Rigidbody>();
                rb.mass = 0.3f;
                atomGO.AddComponent<XRGrabInteractable>();

                var atom = atomGO.AddComponent<Atom>();
                var color = ElementColors.TryGetValue(symbol, out var c) ? c : Color.gray;
                atom.Init(symbol, Valence.TryGetValue(symbol, out var v) ? v : 1, color);
                atom.onBondAttempt = HandleBondAttempt;
                atom.bondSound = bondSound;
                _pool.Add(atom);
            }
        }

        // Each distinct instance label ("H1", "H2", "O1", ...) in the recipe
        // is one real atom the player must place on the bench - trivial and
        // unambiguous now that the recipes name atom instances instead of
        // bare elements.
        private static List<string> CollapseToUniqueAtoms((string a, string b)[] bonds)
        {
            var instances = new HashSet<string>();
            foreach (var bond in bonds)
            {
                instances.Add(bond.a);
                instances.Add(bond.b);
            }
            return instances.Select(StripDigits).ToList();
        }

        private void HandleBondAttempt(Atom a, Atom b, bool success)
        {
            if (!_roundActive) return;

            if (!success)
            {
                _mistakesThisTask++;
                _lastAttempt = $"{a.Element}-{b.Element}";
                var blocked = !a.CanBond ? a.Element : b.Element;
                _feedbackText.text = $"{blocked} already has all the bonds it can hold - try a different atom.";
                HandleFailure();
                return;
            }

            int idx = _remainingBonds.FindIndex(p =>
                (StripDigits(p.a) == a.Element && StripDigits(p.b) == b.Element) ||
                (StripDigits(p.a) == b.Element && StripDigits(p.b) == a.Element));

            if (idx < 0)
            {
                _feedbackText.text = $"That's a valid {a.Element}-{b.Element} bond, but {_current.Formula} doesn't need it - grab fresh atoms.";
                return;
            }

            _remainingBonds.RemoveAt(idx);
            int done = _current.RequiredBonds.Length - _remainingBonds.Count;
            _feedbackText.text = $"Bonded {a.Element}-{b.Element}! {done}/{_current.RequiredBonds.Length} bonds formed.";

            if (_remainingBonds.Count == 0)
            {
                _roundActive = false;
                LockMolecule();
                _feedbackText.text = _current.Fact;
                HandleSuccess();
            }
        }

        // The completed molecule is already a single rigid unit by construction
        // (docked atoms parent onto whichever atom they bonded to) - just
        // disable every non-root atom's own grab so re-grabbing one doesn't
        // try to pull it back out of the assembly.
        private void LockMolecule()
        {
            Atom root = _pool.FirstOrDefault(a => a != null && a.transform.parent == transform);
            foreach (var atom in _pool)
            {
                if (atom == null || atom == root) continue;
                var grab = atom.GetComponent<XRGrabInteractable>();
                if (grab != null) grab.enabled = false;
            }
            if (root == null) return;

            MinigameEnvironment.PlayRoundCompleteVfx(root.transform.position);
            StartCoroutine(PlayFormulaTransformEffect(_current.Formula, root.transform));
        }

        // The completed molecule doesn't just sit there - it actually turns
        // into the substance it represents, the same way it would in real
        // life: water pours out and puddles, a gas puffs away into the air,
        // a flammable gas ignites. Reuses this project's existing VFX packs
        // (JMO Cartoon FX Remaster, Hovl Studio) rather than hand-rolling new
        // particle art from scratch.
        private IEnumerator PlayFormulaTransformEffect(string formula, Transform molecule)
        {
            yield return new WaitForSeconds(1.2f); // let the player actually see the finished molecule first

            // Liquids pour, gases disperse, flammable gases ignite - NaCl is
            // left alone (a solid stays a solid, no transform needed).
            GameObject effectPrefab = formula switch
            {
                "H2O" or "NaOH" => waterPourEffectPrefab,
                "O2" or "CO2" or "NH3" or "HCl" => gasEffectPrefab,
                "H2" or "CH4" => fireEffectPrefab,
                _ => null
            };
            if (effectPrefab == null || molecule == null) yield break;

            var origin = molecule.position;
            var renderers = molecule.GetComponentsInChildren<Renderer>();

            if (formula == "H2O" || formula == "NaOH")
            {
                // Pour: the molecule fades/shrinks in place while a downward
                // stream carries it to the ground, where the splash effect
                // lands - "becomes water and pours down like fluid."
                var stream = BuildPourStream(origin);
                var groundY = origin.y - 1.2f;
                var t = 0f;
                while (t < 1f)
                {
                    t += Time.deltaTime / 1.5f;
                    foreach (var r in renderers) if (r != null) r.enabled = t < 0.3f;
                    stream.transform.position = Vector3.Lerp(origin, new Vector3(origin.x, groundY, origin.z), t);
                    yield return null;
                }
                Destroy(stream, 1.5f);
                if (effectPrefab != null)
                {
                    var splash = Instantiate(effectPrefab, new Vector3(origin.x, groundY, origin.z), Quaternion.identity);
                    Destroy(splash, 4f);
                }
            }
            else if (formula == "O2" || formula == "CO2" || formula == "NH3" || formula == "HCl")
            {
                // Disperses upward into the air, like a released gas.
                var gas = Instantiate(effectPrefab, origin, Quaternion.identity);
                Destroy(gas, 4f);
                var t = 0f;
                while (t < 1f)
                {
                    t += Time.deltaTime / 1.2f;
                    molecule.position = origin + Vector3.up * (t * 0.6f);
                    foreach (var r in renderers) if (r != null) r.enabled = t < 0.5f;
                    yield return null;
                }
            }
            else
            {
                // Ignites - a flammable gas catching fire.
                var fire = Instantiate(effectPrefab, origin, Quaternion.identity);
                Destroy(fire, 3f);
                yield return new WaitForSeconds(1.5f);
                foreach (var r in renderers) if (r != null) r.enabled = false;
            }

            if (molecule != null) Destroy(molecule.gameObject, 0.5f);
        }

        // A simple procedural falling-droplet stream - reliable without
        // depending on the exact internal setup of any one imported VFX
        // asset for the "pours down like fluid" motion itself (the imported
        // CFXR splash prefab still handles the landing impact).
        private GameObject BuildPourStream(Vector3 origin)
        {
            var go = new GameObject("Water Pour Stream");
            go.transform.position = origin;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startColor = new Color(0.4f, 0.65f, 0.95f, 0.85f);
            main.startSize = 0.03f;
            main.startSpeed = 0.2f;
            main.startLifetime = 0.6f;
            main.gravityModifier = 1.5f;
            var emission = ps.emission;
            emission.rateOverTime = 60f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 8f;
            shape.radius = 0.03f;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            renderer.material.color = new Color(0.4f, 0.65f, 0.95f);
            return go;
        }

        // ---- IMinigame ----

        public void SubmitAnswer(string playerAnswer) { /* bonding is the submit action - see HandleBondAttempt */ }

        public void HandleSuccess()
        {
            _score++;
            var data = GetLearningData();
            data.wasCorrect = true;
            GameManager.Instance?.ReportAnswer(data);
            Invoke(nameof(NextRecipe), 2f);
        }

        public void HandleFailure()
        {
            var data = GetLearningData();
            data.wasCorrect = false;
            GameManager.Instance?.ReportAnswer(data);
        }

        public void EndGame() => GameManager.Instance?.EndMinigameSession();

        public LearningTaskData GetLearningData()
        {
            return new LearningTaskData
            {
                subject = Subject,
                minigameId = MinigameId,
                level = 1,
                concept = "covalent bonding",
                taskDescription = _current != null ? $"build {_current.Formula} ({_current.Name})" : "",
                playerAnswer = _lastAttempt,
                correctAnswer = _current != null ? _current.Formula : "",
                mistakeCount = _mistakesThisTask,
                hintLevel = GameManager.Instance != null ? GameManager.Instance.Hints.CurrentLevel : 0,
                taskTimeSeconds = Time.time - _taskStartTime,
                sessionAccuracy = GameManager.Instance != null ? GameManager.Instance.Score.Accuracy : 1f
            };
        }
    }
}
