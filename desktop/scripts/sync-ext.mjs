// Copies the extension files the desktop app uses into desktop/ext/ before packaging (npm run dist).
// While developing (npm start) the app reads them straight from the repository root instead.
import { cpSync, rmSync, mkdirSync } from "node:fs";

const root = new URL("../../", import.meta.url);
const dest = new URL("../ext/", import.meta.url);
const ITEMS = ["background.js", "local-dict.js", "shared", "content", "popup", "options", "icons", "dict"];

rmSync(dest, { recursive: true, force: true });
mkdirSync(dest, { recursive: true });
for (const item of ITEMS) cpSync(new URL(item, root), new URL(item, dest), { recursive: true });
console.log("copied", ITEMS.join(", "), "→ desktop/ext/");
