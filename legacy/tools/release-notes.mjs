// The GitHub release notes of one version, from shared/changelog.js: English, then Arabic, as Markdown.
// The release workflow runs it; it fails (exit 1) when the version has no entry, so no release goes out without notes.
//   node tools/release-notes.mjs 1.9.3 > release-notes.md
import { readFileSync } from "node:fs";
import vm from "node:vm";

/** The changelog, as the app reads it. */
export function changelog() {
  const ctx = vm.createContext({});
  vm.runInContext(readFileSync(new URL("../shared/changelog.js", import.meta.url), "utf8") + "\n;globalThis.log = LamhaChangelog;", ctx);
  return ctx.log;
}

export function releaseNotes(version) {
  const entry = changelog().find(e => e.v === version);
  if (!entry) return null;
  return [
    "## What's new", "", ...entry.notes.map(n => `- ${n[1]}`), "",
    '<div dir="rtl">', "", "## ما الجديد", "", ...entry.notes.map(n => `- ${n[0]}`), "", "</div>", ""
  ].join("\n");
}

if (process.argv[1] && import.meta.url.endsWith(process.argv[1].replace(/\\/g, "/").split("/").pop())) {
  const version = String(process.argv[2] || "").replace(/^v/, "");
  const notes = releaseNotes(version);
  if (!notes) {
    console.error(`shared/changelog.js has no entry for ${version || "(no version given)"}: add one before releasing.`);
    process.exit(1);
  }
  process.stdout.write(notes);
}
