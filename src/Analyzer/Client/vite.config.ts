import { defineConfig } from "vite";
import { readFileSync, copyFileSync, mkdirSync } from "node:fs";
import { resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = dirname(fileURLToPath(import.meta.url));
const pkg = JSON.parse(
  readFileSync(resolve(__dirname, "package.json"), "utf-8"),
) as { version: string };

const outDir = resolve(__dirname, "../wwwroot/App_Plugins/Analyzer");

export default defineConfig({
  define: {
    __ANALYZER_VERSION__: JSON.stringify(pkg.version),
  },
  build: {
    outDir,
    emptyOutDir: true,
    lib: {
      entry: resolve(__dirname, "src/index.ts"),
      formats: ["es"],
      fileName: () => "analyzer.js",
    },
    rollupOptions: {
      // Copy the umbraco-package.json manifest alongside the bundle so
      // the host Umbraco picks it up automatically (FR-006).
      output: {
        assetFileNames: "[name][extname]",
      },
    },
  },
  plugins: [
    {
      name: "copy-umbraco-package-manifest",
      closeBundle() {
        mkdirSync(outDir, { recursive: true });
        copyFileSync(
          resolve(__dirname, "public/umbraco-package.json"),
          resolve(outDir, "umbraco-package.json"),
        );
      },
    },
  ],
  test: {
    environment: "jsdom",
    globals: true,
  },
});
