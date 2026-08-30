# AI Learning Ecosystem

Step into the lesson — VR learning with an AI tutor by your side.

A VR learning app built in Unity, covering three subjects through interactive
minigames rather than lectures.

## Subjects

- **Math** — Math Cannon, Shooting Range, and more.
- **Physics** — Projectile Launcher, Newton's Laws of Motion.
- **Chemistry** — Molecule Builder, Chemical Reaction Lab.

Pick a subject from the Hub, then a minigame — each one teaches its concept
by having you actually do it (aim a cannon, balance an equation, build a
molecule), not just read about it.

## AI Tutor (Convai)

Every subject has its own AI teacher, powered by [Convai](https://convai.com).
Meet them from the Hub's category screen — they wander their classroom,
answer questions about the lesson in front of them, and respond in real time
via voice.

To use it: paste your own Convai API key and character IDs into
`ConvaiRuntimeSettings` on the `Start Auth Bridge` object in `StartScene`.
