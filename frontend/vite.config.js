import { defineConfig } from "vite";

export default defineConfig({
  base: "/AI-Learning-Ecosystem/",
  server: {
    host: true,
    port: 5173
  },
  build: {
    outDir: "dist",
    assetsDir: "assets"
  }
});
