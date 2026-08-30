using UnityEngine;
using UnityEngine.SceneManagement;
using static AILearningEcosystem.Hub.CanvasUIHelpers;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Boots one of the standalone Chemistry minigame scenes (Assets/
    /// PlatformScenes/Chemistry/Diatomic.unity, Compounds.unity,
    /// AcidsBases.unity). Each scene now hosts a DIFFERENT minigame (matching
    /// the design doc's "3 distinct Chemistry minigames") - the field stays
    /// typed as ChemistryMoleculeGame.Topic purely so each scene's already-
    /// serialized enum index (0/1/2) needs no scene edit; Diatomic=0 selects
    /// Molecule Builder, Compounds=1 selects Chemical Reaction Lab,
    /// AcidsBases=2 selects Periodic Table Hunt.
    /// </summary>
    public class ChemistryTopicSceneStarter : MonoBehaviour
    {
        public ChemistryMoleculeGame.Topic topic = ChemistryMoleculeGame.Topic.Diatomic;

        private void Start()
        {
            EnsureEventSystem();
            NavTabBar.Build(transform);

            switch (topic)
            {
                case ChemistryMoleculeGame.Topic.Compounds:
                    StartReactionLab();
                    break;
                case ChemistryMoleculeGame.Topic.AcidsBases:
                    StartPeriodicHunt();
                    break;
                default:
                    StartMoleculeBuilder();
                    break;
            }
        }

        private void StartMoleculeBuilder()
        {
            var game = GetComponent<ChemistryMoleculeGame>();
            if (game == null) game = gameObject.AddComponent<ChemistryMoleculeGame>();
            game.onComplete = () =>
            {
                ConvaiGuide.Speak("You built every molecule in this topic - great job!");
                QuestLog.MarkComplete(SceneManager.GetActiveScene().name);
            };
            game.StartWith(ChemistryMoleculeGame.Topic.Diatomic);
            // The teacher's ConvaiNPC only finishes setting up its gRPC
            // client a frame after her own Start() runs - speaking this same
            // frame (same race documented in LeversGame/ArcheryProjectileGame)
            // silently dropped the welcome line, no audio/caption at all,
            // in every Chemistry scene (none of them had this fix yet).
            Invoke(nameof(SpeakMoleculeWelcome), 1.5f);
        }

        private void SpeakMoleculeWelcome() =>
            ConvaiGuide.Speak("Welcome to Molecule Builder. Grab two atoms and bring them close together to bond them.");

        private void StartReactionLab()
        {
            // Chemical Reaction Lab (equation-balancing) overlapped too much
            // with Molecule Builder's own bonding lesson - this scene is now
            // "Chemistry in the Real World" instead: real objects/scenarios,
            // point at the actual chemical process behind each one.
            var game = GetComponent<ChemistryRealWorldGame>();
            if (game == null) game = gameObject.AddComponent<ChemistryRealWorldGame>();
            game.onComplete = () =>
            {
                ConvaiGuide.Speak("You matched every scenario to its real chemistry - now you'll spot it everywhere.");
                QuestLog.MarkComplete(SceneManager.GetActiveScene().name);
            };
            game.StartWith();
            Invoke(nameof(SpeakReactionLabWelcome), 1.5f);
        }

        private void SpeakReactionLabWelcome() =>
            ConvaiGuide.Speak("Welcome to Chemistry in the Real World. Read what's happening on the tablet, then point at the chemical process actually behind it.");

        private void StartPeriodicHunt()
        {
            var game = GetComponent<PeriodicTableHuntGame>();
            if (game == null) game = gameObject.AddComponent<PeriodicTableHuntGame>();
            game.onComplete = (score, total) =>
            {
                ConvaiGuide.Speak($"Nice hunting! You found {score} out of {total} elements.");
                QuestLog.MarkComplete(SceneManager.GetActiveScene().name);
            };
            game.StartWith();
            Invoke(nameof(SpeakPeriodicHuntWelcome), 1.5f);
        }

        private void SpeakPeriodicHuntWelcome() =>
            ConvaiGuide.Speak("Welcome to Periodic Table Hunt. Search the grid for the element I describe, then drop it in the ring.");
    }
}
