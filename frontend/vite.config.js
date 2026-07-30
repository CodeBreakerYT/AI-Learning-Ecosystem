import { defineConfig } from "vite";

// GitHub Pages serves this project from a /AI-Learning-Ecosystem/ subpath;
// Netlify (and local dev) serve it from the domain root. Vite's `base` has
// to match wherever the build actually lands, so pick it from the env each
// platform sets during its own build rather than hardcoding one value that
// only works for one of the two targets.
const base = process.env.GITHUB_ACTIONS ? "/AI-Learning-Ecosystem/" : "/";

export default defineConfig({
  base,
  server: {
    host: true,
    port: 5173
  },
  build: {
    outDir: "dist",
    assetsDir: "assets",
    rollupOptions: {
      output: {
        // three and firebase dominate the bundle size — splitting them into
        // their own vendor chunks keeps the app code cacheable separately
        // and avoids one ~1MB catch-all chunk.
        manualChunks(id) {
          if (!id.includes("node_modules")) return undefined;
          if (id.includes("three")) return "vendor-three";
          if (id.includes("firebase") || id.includes("@firebase")) return "vendor-firebase";
          return undefined;
        }
      }
    }
  }
});
