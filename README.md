# AI Learning Ecosystem (Unity)

A VR learning app covering Math, Physics, and Chemistry: a login screen, a
subject/category picker, per-subject minigames each with an AI tutor teacher,
and a skill-tracking "My Progress" screen. Built in Unity 6000.3.13f1 (URP,
XR Interaction Toolkit) and intended for both a native Windows build and a
WebXR/WebGL build.

> This README describes the project as it actually is today. An earlier,
> much more elaborate multi-phase plan (Firebase/Google login, a custom
> HTML/CSS WebGL login template copied from a separate EcoLearn web app,
> Gemini-backed NPC chat, a quest-driven forest "World" scene, twelve+
> scenes) exists as design notes but was **not** what got built — the
> simpler structure below is what's actually in the scenes and scripts.

## Scene flow

`Assets/PlatformScenes/`:

- **`StartScene.unity`** — entry point (Build Settings index 0).
  `StartSceneNav.cs` builds a small floating panel ("AI LEARNING ECOSYSTEM /
  Choose where to go") with one button, **SUBJECTS**, that loads `Hub`. A
  `ConvaiRuntimeSettings` component here (on the `Start Auth Bridge`
  GameObject) holds your Convai **API key** plus one Convai **character ID**
  per subject (Math/Physics/Chemistry) — see "One-time setup" below.
- **`Hub.unity`** — the picker, and the only navigation hub. `HubBootstrap.cs`
  builds a world-space sci-fi-styled canvas with three screens:
  - **Choose a subject** — MATH / PHYSICS / CHEMISTRY cards (each shows a
    personalized one-line blurb based on your actual adaptive level per
    game), plus **MY PROGRESS** and **< BACK TO START**.
  - **Category screen** (rebuilt per subject) — two minigames plus a
    "Meet the Teacher" option that loads that subject's classroom scene.
  - **Progress screen** — `SkillProfilePanel.cs`, see below.
  There is no separate free-roam "World" scene in the current build —
  `Assets/PlatformScenes/World/World.unity` exists on disk but is not
  registered in Build Settings and nothing loads it.
- **Minigame scenes**, one per category:
  - Math: `Math/MathCannon.unity`, `Math/MathShootingRange.unity` (also
    `Math/EquationEscapeRoom.unity`, `Math/GeometryBuilder.unity`,
    `Math/SurfaceAreaVolume.unity` exist and are reachable via `HubBootstrap`
    for their subjects even though "Math Cannon"/"Shooting Range" are the two
    wired into the current category screen).
  - Physics: `Physics/ProjectileLauncher.unity` (the archery/projectile
    lesson), `Physics/NewtonsLaws.unity` (a ported scene with its own
    simulation content, loaded directly rather than through the
    IMinigame/DifficultyManager adaptive system), `Physics/NewtonsForceArena.unity`.
  - Chemistry: `Chemistry/ForestChemistryMinigame.unity` ("Molecule
    Builder"), `Chemistry/ChemicalReactionLab.unity`, `Chemistry/PeriodicTableHunt.unity`.
  - Classrooms: `Classrooms/MathClassroom.unity`, `Classrooms/PhysicsClassroom.unity`,
    `Classrooms/ChemistryClassroom.unity` — each spawns a Convai-driven
    teacher via `MinigameTeacher.cs` who wanders the room.
- Registered Build Settings order: `StartScene`, `Hub`, `EquationEscapeRoom`,
  `MathCannon`, `GeometryBuilder`, `NewtonsLaws`, `ProjectileLauncher`,
  `NewtonsForceArena`, `ForestChemistryMinigame`, `ChemicalReactionLab`,
  `PeriodicTableHunt`, `MathClassroom`, `PhysicsClassroom`,
  `ChemistryClassroom`, `MathShootingRange`, `SurfaceAreaVolume`.

## Key systems

- **`Assets/HubWorld/CanvasUIHelpers.cs`** — shared runtime UI builder used
  everywhere: procedurally generated sci-fi chamfered panels/buttons
  (`CreateSciFiPanel`/`CreateSciFiButton`) plus the original EcoLearn-styled
  `CreatePanel`/`CreateButton`. All in-VR UI is built this way at runtime
  (once, then persisted as real saved scene objects — see below), since a
  headset only ever renders the WebGL canvas, never a surrounding webpage.
- **`[ExecuteAlways]` "build once, rediscover after" pattern** — `HubBootstrap`,
  `SciFiProgressBar`, and most minigame bootstrap scripts check
  `transform.Find("X") == null` in `Awake()`: build the UI/geometry the first
  time, otherwise re-find the already-built child objects. This makes the
  generated UI permanently visible and editable in the Scene view (not just
  spawned at Play time), with a `Rebuild` Inspector button
  (`Assets/HubWorld/Editor/MinigameRebuildEditors.cs`) to regenerate it after
  a code change. Runtime kickoff logic (things that should only run in Play
  mode) lives in `Start()` behind a `_runtimeStarted` guard, not in `Awake()`,
  since `[ExecuteAlways]`'s `Awake()` can fire before `Application.isPlaying`
  has actually settled during the edit→play transition.
- **`Assets/HubWorld/ConvaiRuntimeSettings.cs`** + **`Teachers/MinigameTeacher.cs`** —
  the AI tutor system. One Convai API key and three per-subject character IDs
  are set once, in the Editor Inspector, on `StartScene`'s `ConvaiRuntimeSettings`
  component (never hard-coded or committed as plaintext by an assistant).
  `MinigameTeacher` (present in each minigame/classroom scene) reads the
  override for its own subject by checking which `PlatformScenes/{Math,
  Physics,Chemistry}/` folder the scene lives in, builds a full Convai NPC
  stack (lip sync, head tracking, blinking, `ConvaiActionsHandler` with
  Move To/Point/Dance actions, `DynamicInfoController` fed by
  `ConvAIManager.UpdateGameContext` so the AI can answer "what do I do here?"
  grounded in the live minigame state), and has her wander
  (`TeacherWander.cs`) or optionally follow the player
  (`TeacherFollowPlayer.cs`) on a small locally-baked NavMesh.
- **`Assets/HubWorld/Learning/`** — `PlayerProgressManager` (per-concept
  mastery tracking) and `GameManager.Difficulty` (per-subject, per-minigame
  adaptive leveling), both `PlayerPrefs`-backed. `SkillProfilePanel.cs` is
  the visible "My Progress" surface: a `SciFiProgressBar` node-meter per
  minigame (current adaptive level out of 5) and a "Focus on: {concept}"
  weak-spot callout, reachable from the Hub's subject screen.
- **Archery / projectile physics lesson** (`Games/ArcheryProjectileGame.cs`,
  `ArcheryBow.cs`) — fixed launch speed so the trigonometry has one unknown
  (the launch angle); `BuildAngleHint()` shows the worked
  `sin(2θ) = Rg/U²` calculation with both solution angles for the current
  target distance; distances are tuned to keep both solutions in a forgiving
  20–45° band. Spent arrows auto-destroy after 7 seconds
  (`SpentArrowLifetime`) so used interactable colliders don't pile up and
  block new shots. Ambient forest music plays via `WorldMusicDirector`.
- **`Games/MathCannonGame.cs`** / **`MathShootingRangeGame.cs`** — the two
  Math minigames wired into the Hub. `MathShootingRangeGame` uses a real
  `XRSocketInteractor` holster (a sibling of the pistol stand, not a child of
  it, so the holster's world position doesn't move once the pistol is
  grabbed) and destroys bullets on any collision instead of a flat multi-
  second timer, fixing a lag issue caused by `ContinuousDynamic` collision
  bodies living far longer than needed.
- **`SciFiProgressBar.cs`** — a node-based HUD meter (diamond nodes per
  round, done/current/locked states, animated count-up percentage) used by
  both minigame HUDs and `SkillProfilePanel`.

## One-time setup (in the Unity Editor)

1. Open the project in Unity 6000.3.13f1 (or a later 6000.3.x). Let Package
   Manager resolve packages and let assets reimport; accept any one-time
   material/shader upgrade prompt.
2. **Set your Convai API key and per-subject teacher character IDs.** Open
   `StartScene.unity`, select the `Start Auth Bridge` GameObject, and fill in
   `ConvaiRuntimeSettings`'s `Api Key`, `Math Teacher Character ID`,
   `Physics Teacher Character ID`, and `Chemistry Teacher Character ID`
   fields directly in the Inspector. These are intentionally blank in the
   checked-in scene — paste your own values in locally; don't commit a real
   key if this repo is ever made public (leave the fields blank before
   committing, or keep this file out of version control).
3. Enter Play mode from `StartScene` and click **SUBJECTS** to reach the Hub,
   or open `Hub.unity` directly and press Play.

## Known limitation: WebGL + Convai

Convai's runtime depends on `Grpc.Core`, which has no WebAssembly build.
Only 2 files in this project currently guard Convai usage behind
`#if !UNITY_WEBGL`; the rest (including `MinigameTeacher.cs` and the
Convai component stack it builds) will fail to compile for the WebGL build
target as-is. A real WebGL/Netlify deployment needs those `#if !UNITY_WEBGL`
guards added across every file that references a Convai type — not yet
done. **A Windows Standalone (.exe) build is unaffected** by this and is the
currently-working way to test and share a build, since Convai's gRPC client
compiles fine for desktop targets.

## Notes

- `World.unity`, the "WORLD" navigation option, and the associated menu
  buttons in `HubBootstrap`/`StartSceneNav` were intentionally removed from
  the active flow — this app is subject-picking only now (Math / Physics /
  Chemistry via the Hub), with no separate free-roam scene.
- `robocopy` was used to copy this project from its working location,
  excluding the regenerable `Library/` and `Temp/` folders (both are
  Unity-generated caches, safe to delete and let Unity rebuild on next open).
