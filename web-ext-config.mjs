// Settings for `npx web-ext build | lint | run`: keep non-extension folders out of the add-on.
export default {
  ignoreFiles: [
    "desktop", "desktop/**",
    "store-assets", "store-assets/**",
    "tools", "tools/**",
    "dist-packs", "dist-packs/**",
    "web-ext-artifacts", "web-ext-artifacts/**",
    "node_modules", "node_modules/**",
    "README.md", "CLAUDE.md", "web-ext-config.mjs", "package.json", "package-lock.json",
    "translation-report.md" // tools/compare-translation.mjs writes it here (git-ignored, but a local build would pack it)
  ]
};
