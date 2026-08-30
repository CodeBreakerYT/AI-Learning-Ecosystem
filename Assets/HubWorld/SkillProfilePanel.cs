using TMPro;
using UnityEngine;
using AILearningEcosystem.Learning;
using static AILearningEcosystem.Hub.CanvasUIHelpers;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// "My Progress" - the visible skill-tracking surface the Hub didn't
    /// have. Every minigame session already feeds PlayerProgressManager
    /// (per-concept mastery, scoped per learner via UserSession) and
    /// GameManager.Difficulty (per-minigame adaptive level), but neither was
    /// ever shown to the player - this is read-only, no new tracking logic.
    /// Built as a third HubBootstrap screen, same CanvasUIHelpers primitives
    /// as _subjectScreen/_categoryScreen so it matches the existing look.
    ///
    /// Minigame ids/labels here mirror HubBootstrap's own category buttons
    /// exactly. "Newton's Laws of Motion" is intentionally excluded - that
    /// category loads the ported Lumora Physics scene directly, which never
    /// goes through IMinigame/DifficultyManager.
    /// </summary>
    public static class SkillProfilePanel
    {
        public struct GameEntry
        {
            public string label;
            public string minigameId;
            public GameEntry(string label, string minigameId) { this.label = label; this.minigameId = minigameId; }
        }

        public struct SubjectEntry
        {
            public string subject;
            public Color accent;
            public GameEntry[] games;
        }

        /// <summary>
        /// Exposed so HubBootstrap can compute the same per-subject average
        /// level for its subject-picker blurbs, without duplicating this
        /// list. Mirrors HubBootstrap's own category lists - "Newton's Laws
        /// of Motion" is listed here too now, though it loads the ported
        /// Lumora scene directly and never reports a result through
        /// IMinigame/DifficultyManager, so its meter will sit at whatever
        /// GameManager.Difficulty's default level is (1/5, empty) rather
        /// than ever actually moving - shown for visibility/consistency with
        /// every other Physics category, not because it's truly adaptive.
        /// </summary>
        public static readonly SubjectEntry[] Subjects =
        {
            new SubjectEntry
            {
                subject = "Mathematics",
                accent = new Color(0.357f, 0.549f, 1f),
                games = new[]
                {
                    new GameEntry("Math Cannon", "MathCannon"),
                    new GameEntry("Shooting Range", "MathShootingRange"),
                }
            },
            new SubjectEntry
            {
                subject = "Physics",
                accent = new Color(0.133f, 0.827f, 0.933f),
                games = new[]
                {
                    new GameEntry("Projectile Launcher", "ProjectileLauncher"),
                    new GameEntry("Newton's Laws of Motion", "NewtonsLaws"),
                }
            },
            new SubjectEntry
            {
                subject = "Chemistry",
                accent = new Color(0.655f, 0.545f, 0.98f),
                games = new[]
                {
                    new GameEntry("Molecule Builder", "MoleculeBuilder"),
                    new GameEntry("Chemical Reaction Lab", "ChemicalReactionLab"),
                }
            },
        };

        public static GameObject Build(Transform parent, UnityEngine.Events.UnityAction onBack)
        {
            var panel = CreateSciFiPanel(parent, Vector2.zero, new Vector2(900, 560));
            var title = CreateText(panel.transform, "MY PROGRESS", 36, SciFiGlowCore, TextAlignmentOptions.Center,
                new Vector2(0, 245), new Vector2(700, 60));
            title.fontStyle = FontStyles.Bold;
            title.characterSpacing = 3f;

            // Level X/5 used to be a bare number pair - now a real lit-node
            // meter (the same SciFiProgressBar the archery lesson uses), so
            // "how close to maxed out" reads at a glance instead of having
            // to parse "3/5" as a fraction.
            var y = 185;
            foreach (var subject in Subjects)
            {
                // Shifted right from -420 - that sat right against the
                // panel's own left border/chamfered corner, with a big dead
                // gap before the meters started on the right.
                var header = CreateText(panel.transform, subject.subject.ToUpperInvariant(), 22, subject.accent, TextAlignmentOptions.Left,
                    new Vector2(-330, y), new Vector2(280, 30));
                header.fontStyle = FontStyles.Bold;
                header.characterSpacing = 2f;
                y -= 40;

                foreach (var game in subject.games)
                {
                    var level = GameManager.Instance != null
                        ? GameManager.Instance.Difficulty.CurrentLevel(subject.subject, game.minigameId)
                        : 1;
                    CreateText(panel.transform, game.label, 18, TextDimColor, TextAlignmentOptions.Left,
                        new Vector2(-300, y), new Vector2(280, 26));
                    var meter = SciFiProgressBar.Build(panel.transform, new Vector2(255, y - 4), new Vector2(230, 30), 5, 5);
                    meter.SetProgress(level, 5, $"LV {level}/5");
                    y -= 38;
                }
                y -= 10;
            }

            var weak = PlayerProgressManager.WeakConcepts();
            var focusText = weak.Count > 0 ? $"Focus on: {weak[0]}" : "Nothing flagged yet - keep going!";
            CreateText(panel.transform, focusText, 20, SciFiTextDim, TextAlignmentOptions.Center,
                new Vector2(0, -225), new Vector2(760, 40));

            CreateSciFiButton(panel.transform, "< BACK", SciFiFrameColor,
                new Vector2(0, -265), new Vector2(220, 50), onBack);

            return panel.gameObject;
        }
    }
}
