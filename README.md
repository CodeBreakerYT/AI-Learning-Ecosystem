# AI Learning Ecosystem (Unity)

A VR learning app covering Math, Physics, and Chemistry, with AI tutor/quest-giver
NPCs backed by Google Gemini (see "NPC dialogue" below), built for WebXR
export so it can run in a browser and deploy as a static site (e.g. on Vercel).

**NPC dialogue was originally built on Convai, then replaced.** Convai's NPC
runtime depends on `Grpc.Core`, which has no WebAssembly build and is excluded
from WebGL compilation entirely (`Convai.Runtime.asmdef`) - so on this project's
actual deploy target, Convai NPCs never spoke at all. `Assets/Convai/` is still
on disk (other Convai subsystems may still be referenced by ported scenes) but
NPC speech/chat now runs through `Assets/HubWorld/Chat/` instead - see "NPC
dialogue: Gemini chat + push-to-talk" for the current system. (An intermediate
pass used a free Hugging Face model instead of Gemini - swapped out at the
user's request; nothing from that pass remains in the project.)

**The three subject-teacher classrooms are the one exception** - built with
the real Convai SDK per explicit request, so they inherit that same WebGL
limitation (see "Subject teacher classrooms" below). Everything else in this
README about Convai not working in the shipped build does not apply to
`World.unity`'s quest-givers, which are Gemini-backed and WebGL-safe.

## What's in this project

- **Unity 6000.3.13f1**, URP, built from Unity's VR Core template (XR Interaction
  Toolkit rig, teleport/blink locomotion).
- **`Assets/PlatformScenes/`** — twelve scenes total:
  - `StartScene.unity` — the actual Build Settings entry point (index 0). This is where
    login/registration happens — see `Assets/WebGLTemplates/EcoLearn/` below — via
    `StartAuthBridge.cs`. Only loads `Hub` once authenticated.
  - `Hub.unity` — a subject → category picker, nothing else: login goes straight here,
    with no separate "World"/free-roam scene to distinguish it from (there used to be
    one — `World/World.unity`, Lumora's inherited `LumoraMenu.unity` classroom/menu
    scene, wearing a picker UI over hidden classroom dressing. It's been retired and
    moved to `_Archive/World_scene_backup/` at the repo root, not deleted outright,
    since nothing in this project's git history has ever been committed — see
    `HubBootstrap.cs` below for why a clean scene built for this app specifically was
    simpler and less baggage-prone than continuing to fight Lumora's leftovers).
    Built from the same pieces as every other scene: `PlayerPhysics` rig,
    `StartSceneEnvironment.cs` for the backdrop (the exact same dark-navy/grid-floor/
    pylon-ring look `StartScene` uses, for visual consistency), and `HubBootstrap.cs`
    for the picker itself. Every category here loads one of the nine minigame scenes
    below.
  - `World.unity` — a **new, different** "World" (not the retired one above): a
    walkable forest adventure with quest-giver NPCs posing real-life problems tied
    to Math/Physics/Chemistry, reachable via an "Enter the Adventure World" button
    on `Hub`'s subject-picker screen. See the dedicated section below for how it's
    built and how quests work.
  - `Math/Addition.unity`, `Math/Subtraction.unity`, `Math/Multiplication.unity` —
    three standalone scenes, each running `MathBlockTossGame` fixed to that operation
    via `MathTopicSceneStarter.cs`.
  - `Chemistry/Diatomic.unity`, `Chemistry/Compounds.unity`,
    `Chemistry/AcidsBases.unity` — three standalone scenes, each running
    `ChemistryMoleculeGame` fixed to that topic via `ChemistryTopicSceneStarter.cs`.
  - `Physics/NewtonsLaws.unity` — formerly `Subjects/Physics.unity` /
    `LumoraPhysics.unity`, moved here unchanged (Newton's-law simulation, weighable
    objects, VR puzzle doors/buttons) along with its baked-lighting companion folder
    (`Physics/NewtonsLaws/`, renamed to match) — reused as-is rather than rebuilt,
    since it already covers this well.
  - `Physics/Electricity.unity` — `ElectricityGame.cs`, a new "Complete the Circuit"
    minigame (see below).
  - `Physics/Levers.unity` — `LeversGame.cs`, a new "Balance the Lever" minigame
    (see below).
  - `Subjects/Maths.unity` and `Subjects/Chemistry.unity` (the original ported
    `LumoraMath`/`LumoraChemistry.unity` labs) are **no longer part of the active
    app** — dropped from Build Settings and from the Learn Hub's flow, replaced by
    the nine scenes above. The files are left on disk, untouched, in case they're
    wanted again later — nothing currently loads them.
  - Registered in Build Settings in that order: `StartScene`, `Hub`, `World`, then
    the three Math scenes, the three Physics scenes, then the three Chemistry
    scenes — twelve scenes total. `Assets/Scenes/` (Unity VR Core template's leftover
    `SampleScene`/`BasicScene`, never part of the actual app) has been deleted and
    dropped from Build Settings.
  - The rest of the original `Assets/Scenes2/` tree (`assets/`, `pefabs/`, `scripts/`)
    stays where it is — only the `.unity` scene files moved/dropped.
  - **How the nine minigame scenes were built**: each is a self-contained Unity scene
    (Ground, Directional Light, Audio Manager, plus a player rig) assembled from
    proven pieces, not hand-written from nothing. The player rig is
    `Assets/Scenes2/pefabs/player/PlayerPhysics.prefab` — the same rig
    `Physics/NewtonsLaws.unity` (and originally every Lumora subject scene) already
    uses — referenced as a single `PrefabInstance`, exactly like `NewtonsLaws.unity`
    references it. This was a deliberate fix: the first pass used the bare VR-template
    rig embedded in the old `World.unity` ("Simple XR Player with Ray Interactor"),
    which turned out to only support snap-turning — no locomotion at all, in either
    the new scenes or that scene itself. `PlayerPhysics` is Lumora's real, full-featured
    rig: real movement (`Move`/`Jump`/`Gravity`/`Locomotion`/`Teleport`), an
    `XRRayInteractor` for at-a-distance selection, and an `XRDirectInteractor` plus
    actual animated hand meshes on each hand for close-up touch interaction — both
    kinds of interactor work with `AnswerTarget`'s `XRSimpleInteractable` out of the
    box, no extra wiring needed. None of the nine scenes include the Lumora classroom
    dressing or the `Emily`/`Convai Essentials - XR` prefab instances (kept
    deliberately minimal and free of the pre-existing broken prefab reference); add a
    guide via step 3 below if you want narration in a specific minigame scene.
    `PlayerPhysics` doesn't bundle its own `EventSystem` (unlike the old rig), so
    every minigame's bootstrap script calls the new `CanvasUIHelpers.EnsureEventSystem()`
    helper at `Start()` to build one at runtime if the scene doesn't already have one
    — the same helper `StartAuthBridge.cs` uses for the login screen.
- **`Assets/MenuAssets/`** + **`Assets/Animated Hands/`** — dormant now that `World.unity`
  has been retired (see above); left in place rather than removed since other content
  may still reference them. History, for context: the old `World.unity` (via its
  pre-existing `Ground`/`Menu`/`Scene Transition Manager` objects, all inherited from
  Lumora, not added by this project) turned out to depend on a second, separate Lumora
  folder (`Assets/Scenes/1_Assets/Menu/` — ground material, transition/fade prefabs, UI
  textures, fonts, click sounds) plus two hand-model prefabs from `Assets/Animated
  Hands/`, neither of which got ported in the original Phase 1 pass (only `Scenes2` and
  `Convai` did). This showed up as a magenta "missing material" ground plane and
  `(Missing Prefab)` labels in the Hierarchy. Confirmed via a full GUID audit of every
  reference `World.unity` makes (grepped all ~130 unique GUIDs against both projects)
  and ported the two missing folders wholesale, preserving GUIDs the same way as
  `Convai`/`Scenes2` — every reference now resolves except pre-existing ones already
  broken in Lumora itself (same category as the `Convai Essentials - XR` gap already
  documented above) and one deliberately-skipped skybox material whose own texture
  dependency didn't resolve cleanly either — low priority, just falls back to Unity's
  default skybox.
- **`Assets/Convai/`** — ported Convai AI-NPC SDK (chat/voice, lip sync, head
  tracking) plus the XR-specific essentials prefab and the "Amelia" demo tutor avatar.
- **`Assets/Editor/AITutorSetup.cs`** — adds a `Tools > AI Learning Ecosystem > Add AI
  Tutor To Open Scene` menu command that drops the Convai Essentials - XR prefab and
  the Amelia NPC into whichever scene is open.
- **WebXR export** — `com.de-panther.webxr` + `com.de-panther.webxr-interactions`
  (v0.25.0, via an OpenUPM scoped registry in `Packages/manifest.json`), for a browser
  build with real headset support.
- **`Assets/WebGLTemplates/EcoLearn/`** — `StartScene`'s login/register screen is
  **real HTML/CSS copied directly from EcoLearn**, not a Unity recreation: the
  `#ui-mainPage` hero and `#ui-login` auth-card markup in `index.html` are the same
  ids/classes/copy as `EcoLearn/frontend/index.html` (email/username field,
  password, "Forgot password?", "Sign In", the Google button, the "Test
  credentials: admin / admin" hint, Register's name/email/password/confirm fields
  with the same "passwords don't match" client-side check as the real site's
  `login.js`), and `TemplateData/styles.css`, `logo.svg`, `google-icon.svg` are
  byte-for-byte copies of `EcoLearn/frontend/public/assets/ui/*`, plus the same
  Google Fonts (Space Grotesk/Inter) `<link>` tags — pixel-identical to the live
  site, real CSS blur/gradients/fonts included. The rest of `index.html` is Unity's
  standard WebGL loader boilerplate (canvas + `createUnityInstance`) with that
  markup layered on top as a page overlay; only the nav bar and the
  Learn/World/VR Setup/Profile/Devices/Contact sections were dropped (those routes
  don't exist on the Unity side — World is a separate Unity scene loaded after
  auth, not another overlay page). Selected as the active template via
  `ProjectSettings.asset` (`webGLTemplate: PROJECT:EcoLearn`). **Because this is
  real DOM/CSS layered over the canvas, it only ever renders in an actual browser
  hosting a WebGL build — Unity's Editor Game view never runs a browser, so this
  screen cannot and will not appear there, ever.** See the one-time setup section
  below for how to test everything *after* login without a full build each time.
- **`Assets/HubWorld/StartAuthBridge.cs`** — lives on the `Start Auth Bridge`
  GameObject in `StartScene.unity` (the name matters — it's exactly what the HTML
  overlay's JS targets via the browser's `unityInstance.SendMessage(...)` API, no
  plugin needed for that direction). Exposes `ReceiveLoginSubmit`/
  `ReceiveRegisterSubmit`/`ReceiveGoogleSignIn` to receive the overlay's form
  submissions, and calls back into the DOM via `Assets/Plugins/WebGL/
  EcoLearnUIPlugin.jslib` to hide the overlay on success or show status/error text —
  all the actual logic (including the `admin`/`admin` bypass) lives in C#, the HTML
  only handles presentation. Real Firebase accounts including Google sign-in, via
  `FirebaseAuthBridge.cs` + `Assets/Plugins/WebGL/FirebaseAuthPlugin.jslib` driving
  the actual Firebase JS SDK from C# (Firebase's own Unity SDK doesn't support
  WebGL), so login state and the `users`/`loginLogs` Firestore writes are shared
  with the web app. On successful auth, loads `Hub` (`StartAuthBridge.nextScene`).
- **`Assets/HubWorld/StartSceneNav.cs`** — a floating "AI Learning Ecosystem"
  heading, two pressable 3D tabs ("Subjects" → `Hub`, "World" → `World`), and
  looping background music (`LOOP_Splash Screen @ 88 BMP` from the Complete
  Mysterious Forest Game Music Pack, already in the project), positioned in
  front of the spawn point. Exists because the HTML login overlay below only
  ever renders inside an actual deployed WebGL build — not Editor Play Mode,
  not a native build, and browsers don't run inside the Editor's Game view —
  which made `StartScene` a dead end everywhere else: no heading, nothing
  clickable, nothing audible, and only an undiscoverable Space-key debug
  bypass (still there, see `StartAuthBridge.cs`) to get past it while testing.
  This panel works unconditionally regardless of build target or login state,
  same Canvas + `TrackedDeviceGraphicRaycaster` + XR ray interactor pattern
  used everywhere else in this project — live-verified in Play Mode,
  including actually invoking the "Subjects" button and confirming the active
  scene became `Hub`. The HTML login flow itself is untouched and still the
  real path in a deployed build; this is the always-available supplement so
  the app is never a dead end without one.
- **`Assets/HubWorld/StartSceneEnvironment.cs`** — builds a real 3D scene behind
  the HTML login overlay, matching what EcoLearn's own home page actually shows
  (see `EcoLearn/frontend/scripts/core/xrManager.js`, `setupEnvironment()`): dark
  navy fog, a glowing sci-fi grid floor, a soft glow pool, a ring of 8 glowing
  accent-colored pylons, drifting dust, and a small floating placeholder cube.
  Without this, `StartScene` was just an empty Main Camera pointed at Unity's
  default gray skybox — a real gap, not just a login-overlay-doesn't-render-in-
  Editor issue. All built via primitives + procedurally generated textures at
  runtime (same pattern as `CanvasUIHelpers`), verified live via Unity MCP
  screenshots rather than guessed blind. One gotcha worth knowing if you touch
  this: this project renders in **Linear color space**, so hex-style colors must
  be constructed as `new Color32(r, g, b, a)` (and cast to `Color` where needed)
  rather than `new Color(r/255f, g/255f, b/255f)` — the latter looks washed out
  once Unity gamma-corrects it for display.
  `StartScene`'s camera lives on Lumora's `PlayerPhysics` rig (same one used by
  every minigame scene — real `TrackedPoseDriver` head tracking, hands,
  locomotion), not a bare `Main Camera`; `SetupCameraAndFog()` only touches
  render settings and the camera's clear color, never its transform, so it
  never fights real headset head tracking. The rig's root is rotated 180° so
  its resting forward direction actually faces the environment described
  above (a bare `Quaternion.identity` camera, as this scene originally had,
  faces *away* from it — Unity's forward convention is the opposite of Three.js's).
  Every scene built on `PlayerPhysics` also inherits a stuck "Loading..." canvas
  from Lumora's original game framework (its dismiss logic lived on a
  `GameManager` script that didn't get ported, so it just never hides) — fixed
  by deactivating that `LoadingCanvas` (and the irrelevant floating `HealthCanvas`
  HUD) directly on each scene's rig instance. Can't be fixed at the shared
  `PlayerPhysics.prefab` itself: Unity refuses to save a prefab that has any
  missing-script component anywhere in its hierarchy, and this prefab has
  several pre-existing ones inherited from Lumora (inventory slot placeholders,
  `StoreObjects`, `GameManager`) — harmless "referenced script is missing"
  console warnings, not actual errors, safe to ignore.
- **`Assets/HubWorld/CanvasUIHelpers.cs`** — shared static helpers for building
  runtime Canvas/TextMeshPro UI (rounded-rect panels/buttons via a procedurally
  generated 9-sliced sprite, `TMP_InputField` construction), used by
  `HubBootstrap.cs` for the picker in `Hub.unity` and by all four minigames for
  their HUDs — the one piece of UI that's still Unity Canvas rather than HTML,
  because a VR headset only ever renders what's drawn onto the WebGL canvas,
  never the surrounding webpage. That's true no matter which UI toolkit is
  used — it's not a choice made against HTML, it's why anything inside
  `Hub.unity` or a minigame scene specifically can't use it (unlike `StartScene`,
  which happens before a headset is ever attached).
- **`Assets/HubWorld/HubBootstrap.cs`** — `Hub.unity`'s entire content: a subject
  → category picker, built to match EcoLearn's own **Learn page** flow (see
  `EcoLearn/frontend/scripts/learn/learn.js`): pick a subject → pick a category
  → that category's minigame scene loads. No login gating (auth already
  happened in `StartScene`), and nothing else in the scene to hide or work
  around — unlike the old `World.unity`, `Hub.unity` was built for this app
  specifically, so there's no inherited classroom dressing to deactivate.
  - Builds a World Space Canvas (via `CanvasUIHelpers`) with two screens: **Choose a
    subject** (Math / Physics / Chemistry cards) and a **category picker** (rebuilt
    per subject when a card is tapped, three buttons each). Positioned relative to
    the `Hub Bootstrap` object.
  - **Every category loads a real scene** via `SceneManager.LoadScene(...)` — see the
    nine minigame scenes above for what each one runs.
  - **Guide narration**: if a guide NPC is present (see `ConvaiGuide.cs` —
    `Convai NPC Amelia`, added via `Tools > AI Learning Ecosystem > Add AI Tutor
    To Open Scene`), the Hub calls `ConvaiGuide.Speak(message)` at each step —
    welcoming the player, announcing the chosen subject's categories, and
    giving a hint just before loading a minigame scene. This now shows the
    line in her `NpcChatController` dialogue box (text, no voice) rather than
    Convai `TriggerSpeech` — see "NPC dialogue: Gemini chat +
    push-to-talk" below. Degrades silently (no error) if she isn't present or
    has no `NpcChatController` yet.
- **`Assets/HubWorld/HubNavigation.cs`, `NavTabBar.cs`, `NavBridge.cs`** — "back to
  the subject picker" navigation, at both layers this app has:
  - `HubNavigation.cs` is the shared static router: `GoHome()` loads `Hub.unity`
    if it isn't already the active scene. Deliberately simple — earlier this had
    a Subjects/World dual-mode concept mirroring EcoLearn's separate Learn/World
    routes, but that only made sense while a separate free-roam "World" scene
    existed; once `World.unity` was retired in favor of `Hub.unity` (login goes
    straight to the picker, nothing else to distinguish it from), the dual mode
    became pointless complexity and was removed along with it.
  - `NavTabBar.cs` is the **in-VR** panel — a small Canvas with the
    "AI LEARNING ECOSYSTEM" title and a single "< Back to Subjects" button,
    interactive via the same XR ray interactor every other Canvas UI in this
    project uses. Present in every minigame scene (not in `Hub.unity` itself —
    there's nowhere else to go from there). This exists because **a headset
    never renders the surrounding webpage** — only the in-VR Canvas UI is
    reachable once you're actually wearing one. **Parented to `Camera.main`**,
    not a fixed point in scene space — a first version pinned it to a fixed
    world position off to one side, which worked in a deliberately-framed
    screenshot but was genuinely easy to miss/lose track of during normal play
    (especially with the XR rig's hand models filling much of the frame).
    Camera-parenting keeps it in the same spot low in your view no matter where
    you're looking or standing, in every scene, with zero per-scene position
    tuning needed.
  - `NavBridge.cs` is the **flat-website** side: a `DontDestroyOnLoad` object
    created once on successful login (`StartAuthBridge.OnAuthenticated`) so the
    browser's persistent nav bar (`Assets/WebGLTemplates/EcoLearn/index.html`'s
    `#ui-nav`, hidden until login succeeds, then never hidden again since it's
    outside the Unity canvas entirely) can reach Unity via
    `unityGame.SendMessage('Nav Bridge', 'GoHome', '')` no matter which Unity
    scene is currently loaded.
- **`Assets/HubWorld/SceneNavHook.cs`** — a thin `NavTabBar.Build(...)` wrapper for
  scenes with no other AI Learning Ecosystem bootstrap script to hang it off:
  `NewtonsLaws.unity` (the raw ported Lumora Physics scene) and `World.unity`
  (see below). Attached to a GameObject directly in each scene file.
- **`Assets/HubWorld/ConvaiGuide.cs`** — the shared narration helper every minigame
  scene and the Hub call into: looks for `Convai NPC Amelia` by name, and if
  found with a `ConvaiNPC` component, speaks a line via `TriggerSpeech(...)`.
  A no-op if no guide is present — narration is optional
  everywhere, never required for a scene to work.
- **`World.unity`'s quest-adventure forest** (`Assets/HubWorld/World/`) — a walkable
  forest where quest-giver NPCs pose real-life problems tied to Math/Physics/
  Chemistry, and solving one means playing the actual minigame it's framed
  around. Reachable from `Hub`'s subject-picker screen via "Enter the Adventure
  World"; separate from (and unrelated to) the old `World.unity` retired earlier
  (see `_Archive/World_scene_backup/`).
  - **`WorldEnvironment.cs`** — procedurally lays out the forest at runtime: a
    ground plane, a walkable path connecting the spawn point through three
    quest clearings, and ~220 scattered environment props (trees/stones/
    mushrooms from `Assets/Forest Pack/`, crystals/runestones/bushes from
    `Assets/PolitePenguin/3DLowPolyMagicalForest/`) placed at random points
    with a minimum-spacing check against each other and an exclusion check
    against the path/clearings, so nothing overlaps or blocks walking. Prefab
    references are wired via GUID (same technique `HubWorldConfig` used in an
    earlier phase), not `Resources.Load`.
    **Both asset packs shipped with Built-in-Render-Pipeline `Standard` shader
    materials** (this project is URP) — the same class of bug the sky dome hit
    earlier, just at asset-import scale instead of one hand-written material.
    Fixed once, at the asset level: every `Standard`-shader material under
    `Assets/Forest Pack/` and `Assets/PolitePenguin/` was reassigned to
    `Universal Render Pipeline/Lit` (mapping `_MainTex`→`_BaseMap`,
    `_Color`→`_BaseColor`, `_Glossiness`→`_Smoothness`, carrying over
    emission/normal maps where present) — 35 materials total, done once so
    every prop instance (and any future scene reusing these packs) renders
    correctly, rather than a per-instance runtime workaround.
    **Hand-grab interactables**: small, hand-scale props (stones, mushrooms,
    ferns, flowers, glowing crystals — trees/bushes/logs/big rocks stay static
    scenery) get a `Rigidbody` + `XRGrabInteractable` at scatter time
    (`MakeGrabbable`, called from `ScatterProps`), so the player's hands
    (`Left/Right Direct Interactor` on the rig) can pick them up. Existing
    `MeshCollider`s are flipped to `convex = true` (required for a
    non-kinematic Rigidbody); the two prefabs that ship with no collider at
    all (`Plant004`/`Plant007`) get a `BoxCollider` sized from their renderer
    bounds instead, since `XRGrabInteractable` has nothing to hover/select
    without one. Live-verified in Play Mode: 106 grabbable instances spawned,
    zero without a collider, and a scripted `SelectEnter` on one confirmed
    `isSelected == true` (an actual grab succeeding), same as a hand
    physically picking it up would.
  - **Smooth locomotion fix**: the `Player Rig`'s `CharacterController` had
    `skinWidth = 0.08` against a `radius` of only `0.1` — 80% of the radius,
    way past Unity's own recommended 5–10%. An oversized skin width is a
    well-known cause of jerky, catch-on-every-ledge `CharacterController`
    movement; fixed by setting `skinWidth = 0.01`. `ContinuousMoveProvider`
    (3 m/s) and `ContinuousTurnProvider` (60°/s) were already continuous
    (not snap-turn/teleport-step), so this was the one concrete smoothness
    bug rather than a locomotion-mode change.
  - **Fixed at the source: `Assets/Scenes2/pefabs/player/PlayerPhysics.prefab`**
    (used by every scene in this project — `World.unity` and all 9 minigame
    scenes) shipped with ~20 "referenced script is missing" console warnings,
    inherited unchanged from Lumora. Root cause: Lumora's `HealthManager.cs`
    lived under `Assets/Scenes/` (a tree this project never ported — only
    `Assets/Scenes2/` was brought over in Phase 1), and two nested "Hand
    Presence" prefabs pointed at GUIDs that don't resolve to any asset at all,
    even in Lumora itself. None of these — a health pickup, a `GameManager`,
    a `StoreObjects` shop, a 9-slot inventory socket system, a second
    "Hand Presence" physics-hand rig, and Lumora's own quest-dialogue/loading
    canvases — are used anywhere by this project's own code (confirmed via
    grep) or make sense for an educational app with no combat/shop/inventory
    loop. Removed the dead subtrees and stripped the remaining missing-script
    components directly from the shared prefab asset (`PrefabUtility.
    LoadPrefabContents` → edit → `SaveAsPrefabAsset`) rather than patching
    each scene individually — verified zero missing components in both
    `World.unity` and `Addition.unity` (a minigame scene) afterward, and a
    live Play Mode pass confirmed the visible hand models
    (`Left/Right Hand Model`, with their `SkinnedMeshRenderer`s) and both
    `XRDirectInteractor`s survived untouched.
  - **`WorldMusicDirector.cs`** — background audio from the Complete Mysterious
    Forest Game Music Pack: a looped Exploration Cue as ambient music, occasional
    one-shot forest SFX for atmosphere, and a Victory stinger played once if
    `QuestLog` shows a quest was just completed (consumed on `Start()`, so it
    plays exactly once on the trip back from a minigame, not on every visit).
    Tension/Action Battle cues from the same pack aren't wired up yet — available
    for a future puzzle-timer or wrong-answer state, not needed for this pass.
  - **`QuestGiver.cs`** — one per quest-giver NPC: a floating nameplate/blurb
    panel (`CanvasUIHelpers`, hidden until you interact), an `XRSimpleInteractable`
    select interaction (same primitive as `AnswerTarget.cs` — point ray, pull
    trigger), a scripted opening line on interact via `NpcChatController.Say(...)`
    (see "NPC dialogue" below — no LLM call for the blurb itself, just shows it
    in the NPC's dialogue box), and an "Accept Quest" button that loads the target
    minigame scene via
    `SceneManager.LoadScene(...)` — exactly like `HubBootstrap.LoadMinigame`
    does, just reached by walking up to an NPC instead of clicking a picker
    button. If `QuestLog.IsComplete(targetScene)` is already true, the panel
    instead reads "...Solved" with a "Replay Quest" button.
  - **`QuestLog.cs`** — tiny `PlayerPrefs`-backed static class: `MarkComplete`/
    `IsComplete`, keyed by scene name. Hooked into the `onComplete` callbacks
    already present in `MathTopicSceneStarter.cs`, `ChemistryTopicSceneStarter.cs`,
    `ElectricityGame.cs`, and `LeversGame.cs` (one extra line next to the
    existing `ConvaiGuide.Speak(...)` call in each) — additive, doesn't change
    their standalone behavior when reached directly from `Hub` instead of a quest.
  - **Initial quest set** (3 to start, same pattern extends to the other 6
    minigames later by adding more `QuestGiver` instances): **Neko** (quest
    label "Anna Reed") at "Whispering Grove" (Math → `Addition`, splitting
    supply crates before a storm), **Shinobu** (quest label "Sakura") at
    "Alchemist's Hollow" (Chemistry → `Diatomic`, combining raw elements into
    a remedy), **Steve** at "The Old Rope Bridge" (Physics → `Levers`,
    balancing counterweights, unchanged).
  - **Character source for Neko/Shinobu**: `Assets/CharacterImports/` —
    rigged FBX models + idle/talk animation clips copied from
    `ref/Crimson-Valor/assets` (the `anim`/`main` rigged variants, not the
    unrigged `export` ones — those are static mesh-only and were caught and
    swapped out during import). Materials/textures came from the correctly
    wired versions already set up in `ref/Crimson-Valor/CrimsonValor` (the
    reference project's own Unity import); the FBX's own auto-generated
    materials imported with every texture slot null (URP `Lit` shader, but no
    `_BaseMap`), so those were replaced by name-matching each
    `SkinnedMeshRenderer`'s materials against the ones copied in under
    `Assets/CharacterImports/{Neko,Shinobu}/Materials/`, copied together with
    their `.meta` files so the material→texture GUID references resolve.
    Each has its own `AnimatorController` (`Assets/CharacterImports/
    Animators/{Neko,Shinobu}.controller`) with an Idle default state and a
    trigger-driven secondary state (Talk for Neko, Focus for Shinobu).
    Dialogue runs through `NpcChatController`/`GeminiChatClient` (see below),
    not Convai — the `ConvaiNPC`/`ConvaiLipSync`/`ConvaiHeadTracking`/
    `ConvaiBlinkingHandler`/`ConvaiActionsHandler`/`ConvaiGroupNPCController`
    components these NPCs (and Steve) briefly carried have been removed from
    all three in `World.unity`, live-verified in Play Mode. A third character,
    Anya, was evaluated for the Physics quest-giver but every available export
    (rigged, full, basemesh) was missing its body/skin mesh entirely — not
    fixable without Blender — so Steve stays as-is for Physics.
- **NPC dialogue: Gemini chat + push-to-talk** (`Assets/HubWorld/Chat/`) —
  replaces Convai for all NPC speech, since Convai's runtime never worked in a
  WebGL build anyway (see the top of this README). An earlier pass used a free
  Hugging Face model instead; replaced with Gemini at the user's request for
  more consistent free-tier availability. Wired live into `World.unity`'s three
  quest-givers and verified in Play Mode (see "Verification" below).
  - **`GeminiChatConfig.cs`/`.asset`** — blank on purpose, same placeholder
    pattern as `ConvaiAPIKey.asset`/`FirebaseWebConfig.asset`. Paste a free key
    from `aistudio.google.com/apikey` into `apiKey` (real Gemini keys start
    with `AIzaSy...` — if what you have doesn't match that shape it's a
    different kind of token and won't authenticate here). `modelId` defaults
    to `gemini-2.0-flash`. `apiBaseUrl` points at Gemini's REST API
    (`generativelanguage.googleapis.com/v1beta/models/`).
  - **`GeminiChatClient.cs`** — a plain `UnityWebRequest` POST to
    `{apiBaseUrl}{modelId}:generateContent`, with the key in an
    `x-goog-api-key` header (no jslib needed — Gemini's API supports direct
    browser-side calls, so this runs unchanged in the Editor and in a WebGL
    build). Converts the rolling `ChatMessage` history into Gemini's
    `contents`/`systemInstruction` shape (`assistant` → `model` role). Like
    every other client-only key in this project, it travels from the player's
    browser — there's no server in this static-Vercel-deploy architecture to
    hide it behind; restrict the key to the Generative Language API in Google
    Cloud Console if you want to limit its scope.
  - **`NpcChatController.cs`** — one per talking NPC. Owns a small
    world-space **dialogue box** (separate from `QuestGiver`'s quest-accept
    panel) showing a rolling `You: .../<Name>: ...` log and a status hint
    ("Hold B to talk" / "Listening..." / "<Name> is thinking..."). `Say(line)`
    shows a scripted line with no LLM call (used for quest blurbs and
    `ConvaiGuide`-style narration); `SendPlayerMessage(text)` sends the
    player's text plus a rolling conversation history (capped at 6 turns) to
    Gemini and shows the reply — **text only, no voice synthesis**, per
    design. A short per-NPC `personaPrompt` (set via `QuestGiver.personaPrompt`
    for quest-givers) becomes the system prompt, so replies address whatever
    the player actually said/asked rather than being canned lines.
  - **`PushToTalkController.cs`** + **`Assets/Plugins/WebGL/
    SpeechRecognitionPlugin.jslib`** — hold the right controller's B/secondary
    button (or the `B` key on desktop) to talk to whichever NPC's dialogue box
    is currently open (`NpcChatController.Active`). Speech-to-text is the
    browser's native Web Speech API (`SpeechRecognition`/
    `webkitSpeechRecognition`) — free, no server, no extra API key, but
    WebGL-only (browsers don't expose it to the Unity Editor). In the Editor /
    non-WebGL Play Mode, holding the talk button instead opens a small typed-text
    fallback box (release to submit) so the flow is still testable without a
    browser. Not every browser supports Web Speech (notably Firefox) — that
    reports `OnSpeechError` with a message shown in the dialogue box's hint
    line rather than failing silently. A `Push To Talk` GameObject (exact name
    required — the jslib targets it by name) with this component is in
    `World.unity`.
  - **`ConvaiGuide.cs`** kept its class/method name (`ConvaiGuide.Speak(...)`)
    so every existing call site (`HubBootstrap`, `MathTopicSceneStarter.cs`,
    `ChemistryTopicSceneStarter.cs`, `ElectricityGame.cs`, `LeversGame.cs`)
    needed no changes — only the backing implementation moved to
    `NpcChatController.Say(...)`.
  - **Live-verified in `World.unity`**: opened Neko's dialogue box, sent a
    player message, confirmed the panel shows `You: ...` then a graceful
    `The AI tutor isn't configured yet - paste a free Gemini API key into
    GeminiChatConfig.asset.` line (no crash, no console error) since
    `GeminiChatConfig.asset` ships with `apiKey` blank on purpose — paste in
    a real key (see "One-time setup" below) to get real replies.
- **Subject teacher classrooms: `MathClassroom`/`PhysicsClassroom`/
  `ChemistryClassroom`** (`Assets/PlatformScenes/Classrooms/`) — one classroom
  per subject, each with its own AI teacher who roams the room instead of
  standing still, and can walk over to a real demo prop and talk about it.
  Reachable from `Hub.unity`'s category screen for each subject ("Meet Mr.
  Sharma" / "Meet Mrs. Iyer" / "Meet Mr. Rao"). **This is the one part of the
  project that uses the real Convai SDK** (per explicit request — Gemini
  chat/dialogue above stays as-is for `World.unity`'s quest-givers), so it
  inherits Convai's one hard limitation already documented at the top of this
  README: `Convai.Runtime.asmdef` excludes Convai's whole runtime from WebGL
  builds (no WebAssembly gRPC) — these three teachers will stand around
  silently in an actual deployed WebGL build (their wander/animation/overlay
  behavior still works, since none of that is Convai-specific) until Convai
  ships browser support, or the classrooms get switched to the same
  Gemini-backed system as everything else.
  - **Teacher models** (`Assets/CharacterImports/Teachers/`): `MathTeacher.fbx`
    (`indian-man-in-kurta`) and `PhysicsTeacher.fbx` (`indian-woman-in-saree`),
    both Humanoid-rigged successfully and driving duplicated copies of
    Convai's own `Masculine`/`Feminine NPC Animator.controller` (from
    `Assets/Convai/Art/Resources/`) — those ship with exactly the
    Idle/Talking/Dance/Walking/Picking Up/Jumping/Crouch states
    `ConvaiActionsHandler` expects by default, so nothing needed rebuilding
    from scratch. A `Point` state was added to both duplicates
    (`Assets/CharacterImports/Animators/Teacher{Masculine,Feminine}
    .controller`), reusing the `Pointing.fbx` clip already imported for Neko —
    the one genuinely custom animation, registered as its own Convai action
    (see below). The third provided model (`north-detroit-become-human`,
    a sci-fi android bust) turned out to have **zero skeleton/bones** — a
    single static combined mesh, same dead-end class of bug as the earlier
    Anya model — so the Chemistry teacher reuses Convai's own **Mike Carter**
    demo character prefab wholesale instead (already rigged, already
    Convai-ready, already has that same Masculine animator).
  - **`ClassroomEnvironment.cs`** (`Assets/HubWorld/Teachers/`) — one script,
    three scene instances with different Inspector values (same pattern as
    `WorldEnvironment.cs`). At runtime it builds the room (`Classroom.fbx` +
    `DeskChair.fbx` desks), instantiates each subject's demo props (Math:
    a fruit basket; Physics: a toy car + a see-saw — `truck.fbx` turned out
    to have zero-size geometry in every one of its 84 meshes, a broken
    import, so it's not used; Chemistry: a beaker + an Erlenmeyer flask,
    reusing the lab glassware already ported from Lumora in Phase 1, since no
    new chemistry model was provided), registers them with Convai's own
    `ConvaiInteractablesData`/`ConvaiActionsHandler` system
    (`Assets/Convai/Scripts/Runtime/Features/Actions/`) so the AI can
    reference them by name in conversation, bakes a runtime `NavMeshSurface`
    (from the newly-added `com.unity.ai.navigation` package — the
    Editor-only `NavMeshBuilder` API doesn't exist in a WebGL player, so
    baking has to happen this way to survive an actual build), and builds
    the teacher NPC itself with the full Convai component stack.
    **Everything is built while the teacher GameObject is inactive, then
    activated last** — `AddComponent` fires `Awake`/`OnEnable` synchronously,
    and `ConvaiActionsHandler.actionMethods`/`ConvaiNPC.characterID` are only
    assigned *after* the corresponding `AddComponent` call, so building
    active would NRE on every one of them. Two more real bugs surfaced and
    got fixed here, not just in the new teachers' setup:
    - No scene in this project (including `World.unity`) actually places a
      `ConvaiNPCManager` — a plain singleton `ConvaiNPC.OnEnable()`
      dereferences unconditionally. It may have gone unnoticed elsewhere
      because the static `Instance` field can survive across Play Mode
      sessions without a domain reload. `ClassroomEnvironment` creates one
      if missing before activating any teacher.
    - A reused Convai demo prefab (Mike Carter) carries a
      `ConvaiGroupNPCController` for NPC-to-NPC conversation, a feature this
      project doesn't use and whose own singleton
      (`NPC2NPCConversationManager`) isn't set up anywhere either —
      `ClassroomEnvironment` strips that component off before activation.
  - **`TeacherWander.cs`** — periodically walks the teacher to a random point
    around the classroom on the same `NavMeshAgent`/`Animator` Convai's own
    actions drive, so they're never just standing in one spot. Subscribes to
    `ConvaiActionsHandler`'s `ActionStarted`/`ActionEnded` events and pauses
    itself for the duration of any real Convai action (`ResetPath()` on
    start) so the two never fight over the agent's destination — wandering
    is strictly the "nothing better to do" fallback.
  - **`TeacherActionOverlay.cs`** — the "custom animations with text overlays
    and sparkling effects" piece: also listening to the same
    `ActionStarted`/`ActionEnded` events, it shows a floating world-space
    text bubble with a flavor line per action ("Let's take a look over
    here...\n(Fruit Basket)" for `MoveTo`, etc. — see the `Flavor` dictionary)
    and fires a short gold `ParticleSystem` burst at the teacher or the
    target prop, both driven purely by reacting to Convai's own events —
    nothing about Convai's action logic itself was touched.
  - **Borrowed Convai character personas**: none of these three teachers has
    a custom-authored Convai character (that requires creating one on
    Convai's own dashboard, which needs your account, not something doable
    from here) — they reuse existing Convai demo character IDs instead:
    Math → Amelia's ID, Physics → Missy's ID, Chemistry → Mike Carter's own
    (native) ID. Conversation will reflect whatever persona/knowledge those
    demo characters already have configured on Convai's side, not a
    Math/Physics/Chemistry-tutor persona specifically — for that, create
    three real characters at
    [convai.com](https://convai.com) and swap in their IDs via each
    classroom scene's `Classroom Environment` → `Teacher Character ID` field.
  - **Live-verified in all three classrooms**: Play Mode in each, confirmed
    zero console errors beyond the same pre-existing benign ones documented
    elsewhere (XR audio driver, blend-shape-less `ConvaiBlinkingHandler`
    skip). Watched the Math teacher wander on his own, then scripted a
    `ConvaiActionsHandler.actionResponseList.Add("Move to Fruit Basket")` to
    simulate what the real AI would trigger from conversation — confirmed
    the overlay bubble showed the correct target name, the teacher walked
    over via `NavMeshAgent`, and `TeacherWander` correctly stayed out of the
    way for the whole sequence. Two real bugs only surfaced in the Chemistry
    classroom specifically and got fixed at the source: the ported lab
    glassware FBX files (`Beaker.fbx`, `Erlenmeyer_flask.fbx`, `liquids.fbx`)
    weren't marked **Read/Write Enabled**, which the runtime `NavMeshSurface`
    bake needs to read `MeshCollider` geometry (works in the Editor either
    way, silently fails in an actual player build — fixed by enabling it on
    all three).
- **`Assets/HubWorld/Games/`** — the actual minigames, one script per scene:
  - **`MathBlockTossGame.cs`** — a simplified port of EcoLearn's `mathGame.js`: an
    equation appears (using only the operation for the scene's fixed topic —
    Addition, Subtraction, or Multiplication), select the number block with the
    right answer, five rounds of increasing difficulty. Also supports a fourth,
    currently-unused `MixedReview` topic if a fourth Math scene is ever added.
  - **`ChemistryMoleculeGame.cs`** — a simplified port of EcoLearn's
    `chemistryGame.js`: a target formula from the scene's fixed topic is shown
    (H₂, O₂ for Diatomic; H₂O, CO₂, NH₃, CH₄ for Compounds; HCl, NaOH, NaCl for
    Acids & Bases), select the atoms that belong to it out of a pool that also has a
    few distractor elements drawn only from that topic's own elements.
  - **`ElectricityGame.cs`** (new, no EcoLearn equivalent) — "Complete the Circuit":
    same select-the-right-pieces mechanic as the chemistry game, themed for
    electronics. Five circuits of increasing complexity (a simple torch circuit, a
    series circuit with two bulbs, one with a protective resistor, one with a fuse,
    one combining a switch with two bulbs), each teaching what that addition does.
  - **`LeversGame.cs`** (new, no EcoLearn equivalent) — "Balance the Lever": a known
    weight sits a known distance from the fulcrum, select the counterweight that
    balances it at a given distance (torque: F₁·d₁ = F₂·d₁). A visual seesaw tilts
    toward whichever side is heavier and settles level on the correct answer. Five
    rounds of increasing difficulty.
  - **`MathTopicSceneStarter.cs`** / **`ChemistryTopicSceneStarter.cs`** — tiny
    per-scene bootstrap components: a public `topic` field (set differently in the
    Inspector for each of the three scenes per subject) that starts the matching
    game via `StartWith(topic)` and adds a "Back to Learn Hub" button.
    `ElectricityGame`/`LeversGame` don't need a topic (one variant each) so they
    start themselves directly and include their own back button.
  - `AnswerTarget.cs` is the shared selectable-prop helper all four games build on —
    select (ray/poke) interaction rather than a hand-tracked grab-and-throw, since
    that's the interaction this project's XR rig is already proven to support.
  - All four are intentionally simpler than EcoLearn's source games (no lives/boss
    rounds/particle effects) — this keeps the core "select the right answer" loop of
    each game intact and testable in Editor Play Mode.

## One-time setup (do this first, inside the Unity Editor)

1. Open the project in Unity 6000.3.13f1 (or later 6000.3.x). Let Package Manager
   resolve the new WebXR packages, and let the ported Convai/Scenes2 assets reimport —
   you'll likely see a one-time "Upgrade materials to URP" or similar prompt on first
   open; accept it.
2. **Set your Gemini API key.** `Assets/HubWorld/Chat/GeminiChatConfig.asset`
   is intentionally blank. Get a free key from
   [aistudio.google.com/apikey](https://aistudio.google.com/apikey) and paste
   it into the asset's `apiKey` field (this has to be done by hand in the Unity
   Inspector — API keys should never be pasted into files by an AI assistant on
   your behalf). Real Gemini keys start with `AIzaSy...`. Check that `modelId`
   (defaults to `gemini-2.0-flash`) is still current — Google occasionally
   renames/retires model ids, swap it for another chat-capable Gemini model if
   needed. Without a key, NPC dialogue boxes show "The AI tutor isn't configured
   yet" instead of a reply — this is already live-verified working in
   `World.unity`'s three quest-givers (Neko/Shinobu/Steve). (Convai's own API
   key asset, `Assets/Resources/ConvaiAPIKey.asset`, is no longer used by
   *this* dialogue system — see the top of this README for why Convai was
   replaced there. It's still needed for the three classroom teachers below.)
3. **Set your Convai API key for the classroom teachers.**
   `Assets/Resources/ConvaiAPIKey.asset` is intentionally blank, same
   placeholder pattern as everywhere else. Open the Convai setup window
   (menu appears after import, or `Window > Convai`) and enter your own key,
   or edit the asset's `APIKey` field directly. Without a key, `MathClassroom`/
   `PhysicsClassroom`/`ChemistryClassroom`'s teachers will still roam and
   animate (that part doesn't depend on Convai being configured) but won't
   respond to anything said to them. Optionally, create three real Convai
   characters at [convai.com](https://convai.com) with actual Math/Physics/
   Chemistry-tutor personas and paste their IDs into each classroom scene's
   `Classroom Environment` component → `Teacher Character Id` field — out of
   the box these three borrow existing Convai demo character IDs (Amelia,
   Missy, Mike Carter), so conversation reflects whatever persona those demo
   characters already have, not a subject-tutor persona specifically. Remember:
   this only works in the Unity Editor / a native build, not the deployed
   WebGL site — see the top of this README.
4. **Optionally add an AI guide to `Hub.unity` or any minigame scene.**
   `ConvaiGuide` (used by both `HubBootstrap` and every minigame scene) looks for a
   GameObject named `Convai NPC Amelia` and, if she has an `NpcChatController`, shows
   narration lines in her dialogue box. None of these scenes ship with a guide NPC
   by default — they were all built deliberately minimal — open any of them, run
   `Tools > AI Learning Ecosystem > Add AI Tutor To Open Scene`, then add an
   `NpcChatController` component (with `GeminiChatConfig` assigned) to the
   placed NPC if you want narration there too, then save the scene.
5. **Enable the WebXR loader for the WebGL build target.** Project Settings > XR
   Plug-in Management > WebGL tab > check "WebXR Export". (OpenXR stays the loader for
   any native/Quest build target — this only affects WebGL.) This is now also
   wired up in `Assets/XR/XRGeneralSettingsPerBuildTarget.asset` (see the fix
   below), so it should already show as configured — this step is really just
   "verify," not "do."
   - **Fixed a real headset/controller-tracking bug**: `XRGeneralSettings`' `Automatic
     Loading`/`Automatic Running` were both **off** for every platform (Standalone,
     Android, and WebGL had no XR settings at all). With those off, Unity's XR
     subsystems never actually start when the app runs — no head tracking, no
     controller tracking, nothing — regardless of a headset being connected, so
     nothing was interactable no matter what. Fixed by enabling both flags for
     Standalone/Android via `XRGeneralSettingsPerBuildTarget`
     (`SettingsForBuildTarget(...).Manager.automaticLoading/automaticRunning = true`),
     and creating + wiring up WebGL's XR settings from scratch (it had none) with
     the WebXR loader assigned via `XRPackageMetadataStore.AssignLoader(...)`.
     Live-verified in Play Mode: `XRGeneralSettings.Instance.Manager
     .isInitializationComplete` is now `true` with `activeLoader = OpenXRLoader`,
     which it was not before this fix.
   - **Fixed the controller click binding.** The shared `PlayerPhysics.prefab`'s
     main `XRRayInteractor` (on `VR_Player` — the one every Canvas button in
     this entire project is clicked through: Hub, minigames, quest panels,
     dialogue boxes, classrooms, `StartSceneNav`'s tabs) had **no select/activate
     action bound at all** (`m_SelectInput`/`m_ActivateInput` both null) and no
     `m_RayOriginTransform`, so it couldn't be aimed by a hand or clicked by any
     button, independent of the auto-start fix above. Fixed by binding its select
     input to `XRI Right Interaction/Activate` (the **trigger**, not grip — grip
     stays exclusive to the Direct Interactors' physical near-hand grabbing, so
     "point with the ray and pull the trigger" and "reach out and squeeze grip to
     grab" don't collide) and setting `m_RayOriginTransform` to the `RightHand`
     transform so the ray actually aims where the right controller points.
     Fixed once on the shared prefab, so it applies to every scene automatically.
   - **Enabled the real hand meshes and made them animate.** `PlayerPhysics`
     ships with proper skinned "Left/Right Hand Model" hands (fingers, not the
     plastic controller model) whose Animators already had a correctly-built
     `Grip`/`Trigger` blend tree — but were **inactive by default**, and nothing
     fed those two float parameters (the original driving script is one more of
     Lumora's pre-existing missing-script gaps: it simply doesn't exist anywhere
     in the ported assets). Added `Assets/HubWorld/HandPoseAnimator.cs` (reads
     the same grip/trigger analog actions the interactors already use and feeds
     them into the Animator each frame), attached it to both hand models,
     activated the hand models, and deactivated the plastic controller visual
     meshes so the real hands are what's actually shown. Live-verified in Play
     Mode: screenshot confirms real hands render (not controllers), and both
     `HandPoseAnimator`s resolve non-null grip/trigger action references.
6. **Set your Firebase web config.** `Assets/HubWorld/FirebaseWebConfig.asset` ships
   with only `projectId` filled in (`ai-learning-ecosystem`, from the web app's
   `.firebaserc`) — the rest (`apiKey`, `authDomain`, `storageBucket`,
   `messagingSenderId`, `appId`) are blank on purpose, since none of those values exist
   anywhere in this repo. Get them from
   [Firebase Console](https://console.firebase.google.com) → Project Settings →
   General → Your apps → SDK setup and configuration, for the `ai-learning-ecosystem`
   project, and paste them into that asset. Until you do, the login screen will show
   "Firebase isn't configured yet" for real accounts — the `admin`/`admin` test login
   still works without it. Also make sure **Google** is enabled as a sign-in provider
   under Authentication → Sign-in method, and that `backend/firestore.rules` (in the
   `EcoLearn` folder) has been deployed, since Firestore will reject the
   `users`/`loginLogs` writes otherwise.
6. **The login screen only exists in a real WebGL build**, not in Editor Play Mode —
   it's the HTML overlay from the custom template (step above), which only renders
   when a browser is actually hosting the build; the Editor's Game view never runs a
   browser, so there's no way to make it appear there, ever — this isn't a bug or
   something missing, it's an inherent property of every WebXR/WebGL app, EcoLearn's
   own Three.js site included. To test login for real (including `admin`/`admin`), do
   a WebGL build (`File > Build Settings > WebGL > Build And Run`, or build then open
   the output over a local server — opening `index.html` via a raw `file://` double-
   click fails, browsers block the `.wasm`/data file loads over `file://` due to CORS).
   Google sign-in specifically also needs that real browser context (it opens a popup).
   **For quick iteration on everything *after* login** (the Learn Hub picker,
   minigames, Convai guide) without rebuilding each time: press Play on `StartScene`
   in the Editor, then press **Space** — `StartAuthBridge` has an Editor-only debug
   shortcut that simulates an admin login and jumps straight to `World`. This only
   exists in the Editor (compiled out of every real build) and only tests what
   happens *after* login, not the login screen itself.

## Building for the web

1. File > Build Settings > select **WebGL** > Switch Platform.
2. Build to `Builds/WebGL` (already `.gitignore`d, and already the directory
   `vercel.json` points at — use that exact folder name so deployment works without
   extra config).
3. Compression format is set to **Disabled** in Player Settings, so the build serves
   correctly from any static host with zero custom header config (avoids the common
   Unity-WebGL gzip/brotli `Content-Encoding` mismatch on static hosts). This makes
   the build larger; switch to Gzip/Brotli later and add matching `Content-Encoding`
   headers in `vercel.json` once you want to optimize size.
4. If you enable WebGL multithreading later, you'll also need
   `Cross-Origin-Opener-Policy`/`Cross-Origin-Embedder-Policy` headers in
   `vercel.json` — left out for now since they can break cross-origin API calls
   (Firebase, the Gemini API) unless those endpoints send matching
   CORP/CORS headers.

## Deploying to Vercel

From this folder (`AI Learning Ecosystem/`), after building:

```bash
npx vercel --prod
```

`vercel.json` already points Vercel's output directory at `Builds/WebGL`, so this
deploys the build as-is — no build command needed on Vercel's side, the Unity build
happens locally/in Editor beforehand.

## Notes

- Test WebXR in a browser that supports it (Chrome/Edge desktop with a headset
  connected, or the Quest Browser). Desktop browsers without a headset still render
  in 2D; VR entry will report unsupported.
- The sibling `EcoLearn/` folder at the repo root (formerly `AI-Learning-Ecosystem`) is
  a separate Three.js/Vite WebXR app that shares the same Firebase project (same
  `users`/`loginLogs` Firestore writes) but isn't otherwise part of this Unity build.
- The "Forgot password?" link is present (copied from the real markup) but not wired
  to Firebase yet — clicking it shows an honest "not available in this build yet"
  status instead of silently doing nothing. Easy to add later as another
  `FirebaseAuthBridge` method calling Firebase's `sendPasswordResetEmail`.
- The Learn Hub picker canvas and any active minigame's HUD are placed relative to
  the `Hub Bootstrap` object (or the minigame's own bootstrap object), which is
  already aligned with the XR rig's spawn transform — worth checking in Play Mode
  that it's actually in view; move the object if not. This no longer affects login
  (that's a page-level HTML overlay now, not tied to 3D position at all) — only the
  Learn Hub/minigame Canvas content.
- Unlike the web app, a blocked Google sign-in popup here does not fall back to a
  full-page redirect (that would tear down the running Unity WebGL instance) — it just
  reports an error asking you to allow popups and try again.
- **The custom WebGL template is required for login to work at all** — if Player
  Settings ever gets switched back to the default template, the HTML overlay (and the
  `Start Auth Bridge` GameObject name the overlay's JS targets) won't exist, so
  `unityInstance.SendMessage('Start Auth Bridge', ...)` will silently no-op. Keep
  `webGLTemplate: PROJECT:EcoLearn` in Player Settings > Resolution and Presentation.
- Font/copy/color fidelity is solved for the login screen specifically (it's the
  site's actual CSS/fonts, byte-for-byte). It's still an approximation for the Learn
  Hub's picker UI and all four minigames' HUDs, which are necessarily
  Canvas/TextMeshPro (currently `LiberationSans.ttf`, since Canvas/TMP can't load
  real CSS web fonts) — a headset only ever renders what's drawn onto the WebGL
  canvas, never the surrounding webpage, so HTML/CSS is not an option there
  regardless of what's used for `StartScene`.
- Guide narration only works in a given scene once a guide NPC is actually present
  there with an `NpcChatController` (step 3 above) and a Gemini API key is set
  (step 2) — `ConvaiGuide.Speak` looks her up by name and simply does nothing if she
  isn't found or has no `NpcChatController`, so every scene works fine without her,
  just silently, without narration. None of the scenes have a guide NPC by default —
  `Hub.unity` included — until you add one.
