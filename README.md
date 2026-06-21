# AI-Learning-Ecosystem

A WebXR (WebGL VR) learning platform built with [Three.js](https://threejs.org/),
[Vite](https://vitejs.dev/), and [Firebase](https://firebase.google.com/) (free tier)
for auth + storage.

## Flow

1. **Home** — marketing landing page.
2. **Login / Register** — one page with a Login/Register toggle; both tabs support
   email+password and "Continue with Google" (Firebase Auth). Use **admin / admin** on
   the Login tab as a no-Firebase-required test login (see below).
3. **VR Setup** (auth-gated) — pick your headset, then **Attach** to request a real
   `immersive-vr` WebXR session via `navigator.xr`.
4. Once attached, put on the headset: the VR room (floor + lit cube) is rendered, and
   the thumbstick on either controller moves you around — controller models are drawn
   live via `XRControllerModelFactory`.

## Project structure

```
scripts/
  core/        renderer + WebXR session/controllers (xrManager.js), navigator.xr
               session request (xrSession.js), thumbstick locomotion (locomotion.js),
               router (router.js), Firebase init (firebase.js), auth state +
               route guard (authState.js), shared renderer/scene handle (xrState.js)
  mainPage/    landing page UI logic
  login/       login + register UI logic (email/password + Google, toggled tabs)
  contact/     contact form UI logic
  vrSetup/     headset select + Attach (WebXR session request) UI logic
assets/
  ui/          HTML overlay styles, icons, logo, Google icon
  models/      drop 3D models with animations here, one subfolder per page
                (mainPage/, login/, contact/) — loaded via GLTFLoader later
index.html     canvas + all UI overlay markup
```

The floor, lighting, and demo cube live permanently in `scripts/core/xrManager.js`
(not per-page) since that's the actual VR room you walk around in. Page modules only
own the 2D HTML overlay shown before/around the headset experience — swap the cube for
a `GLTFLoader` call against `assets/models/<page>/your-model.glb` once a real model with
animations is ready.

## Requirements

- Node.js 18+
- A WebXR-capable browser (Chrome/Edge on desktop with a VR headset connected, or the
  Meta Quest Browser) to actually enter VR. Desktop browsers without a headset will
  still render the scene in 2D and the Attach button will report VR as unsupported.

## Firebase setup

1. Create a free project at [console.firebase.google.com](https://console.firebase.google.com).
2. Enable **Authentication → Sign-in method → Email/Password** and **Google**.
3. Create a **Firestore Database** (used to store each user's chosen headset).
4. Copy `.env.example` to `.env` and fill in the values from your Firebase project
   settings (Project settings → General → Your apps → SDK setup and configuration).

Without a configured `.env`, real Firebase login/register/Google sign-in will show a
"Firebase isn't configured yet" error — but the dummy login below still works so you
can test the whole VR flow immediately.

## Test login (no Firebase required)

```
username: admin
password: admin
```

Enter this on the Login page to bypass Firebase entirely and go straight to VR Setup.

## What's actually persisted in Firebase

- **Authentication → Users**: every email/password registration and every Google
  sign-in creates a real Firebase Auth user (visible live in the console).
- **Firestore `users/{uid}`**: profile doc with `email`, `provider`, `lastLogin`
  (updated on every sign-in via `scripts/core/authState.js`) and `headset` (set once
  you attach a headset on the VR Setup page).
- **Firestore `loginLogs`**: one append-only row per sign-in (`uid`, `email`,
  `provider`, `at`) — a real login history, not just a single "last seen" field.
- **Session persistence**: the app waits for Firebase to restore an existing session
  (`onAuthStateChanged`) before its first route check, so a signed-in user reopening
  the site in a new tab lands on the real page, not bounced back to Login.
- The offline `admin`/`admin` test login never touches Firebase (no network call, no
  Firestore write) — it's purely a local bypass for testing the VR flow.

`firestore.rules` (included in the repo) restricts each user to their own `users/{uid}`
doc and only lets them *create* (never read/edit) `loginLogs` rows. Paste it into
**Firestore Database → Rules** in the console and click **Publish** — this also avoids
the default "test mode" rules expiring 30 days after project creation, which would
otherwise silently start rejecting every write in production.

## Develop

```bash
npm install
npm run dev
```

WebXR requires a secure context (HTTPS) to enter VR on a physical device. The Vite dev
server is plain HTTP, so for in-headset testing either:
- use a tunnel (e.g. `npx vite --host` + a tool like ngrok), or
- deploy a preview (see below) and open the deployed HTTPS URL on the headset.

## Build & deploy

```bash
npm run build      # outputs static site to dist/
npm run preview    # serve the production build locally
```

The repo includes `netlify.toml` (build command `npm run build`, publish dir `dist`) so
it deploys to Netlify out of the box — connect the repo or run `netlify deploy`. Any
static host (Vercel, GitHub Pages, Cloudflare Pages) works the same way since the build
output is a plain static `dist/` folder with relative asset paths.

### Avoiding deployment issues with Firebase

`.env` is gitignored on purpose (it's not committed), which means **the deployed build
won't have your Firebase keys unless you add them on the host too**. Two things to set
up once, before the first deploy:

1. **Add the env vars on your host.** In Netlify: Site settings → Environment variables
   → add all six `VITE_FIREBASE_*` keys from your `.env`. Other hosts have an equivalent
   "Environment variables" page. Without this, the deployed site silently falls back to
   the "Firebase isn't configured" error path (the dummy `admin`/`admin` login still
   works, but real auth won't).
2. **Authorize the deployed domain in Firebase.** Firebase console → Authentication →
   Settings → Authorized domains → add your Netlify/Vercel domain (e.g.
   `your-site.netlify.app`). Without this, Google sign-in's popup fails with
   `auth/unauthorized-domain` on the live site even though it works on `localhost`
   (which is authorized by default).

Both are one-time console steps — no code changes needed when you redeploy after.
