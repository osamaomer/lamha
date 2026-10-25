// Lamha 2's interface strings, for moving them word for word into Lamha 3's .resw files.
//
//   node tools/Strings/lamha2-strings.mjs dump                      writes tools/Strings/lamha2-strings.json
//   node tools/Strings/lamha2-strings.mjs find <text>               keys whose Arabic or English contains <text>
//   node tools/Strings/lamha2-strings.mjs resw Name=key [Name=key…]  the <data> lines for both .resw files
//
// The strings come from legacy/shared/i18n.js, options/i18n-options.js, desktop/renderer/i18n-desktop.js and
// shared/lamha-ai.js (its ERRORS and CATEGORIES, named "ERRORS.<code>.title|hint" and "CATEGORIES.<name>.label|tip").
// Strings that are functions (Arabic counting, mostly) are listed apart: their C# port is written by hand.
// The JSON is what tests/Lamha.Core.Tests/Strings/ResourcesTests.cs checks the .resw files against.

import fs from "node:fs";
import path from "node:path";
import vm from "node:vm";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const legacy = path.join(here, "..", "..", "legacy");
const jsonFile = path.join(here, "lamha2-strings.json");

function load() {
  const ctx = vm.createContext({});
  const read = f => fs.readFileSync(path.join(legacy, f), "utf8");
  // i18n.js keeps its table private: expose it to read every key.
  const i18n = read("shared/i18n.js").replace("const S = Object.create(null);", "const S = globalThis.__S = Object.create(null);");
  vm.runInContext(i18n, ctx, { filename: "shared/i18n.js" });
  for (const f of ["options/i18n-options.js", "desktop/renderer/i18n-desktop.js", "shared/lamha-ai.js"]) {
    vm.runInContext(read(f), ctx, { filename: f });
  }

  const strings = {};
  const functions = {};
  // A function that is only a template (v => `… ${v.n} … ${num(v.n)} …`) is a string with {n} placeholders.
  const template = f => {
    if (typeof f !== "function") return f;
    const m = /^v => `([^`]*)`$/.exec(String(f));
    if (!m || /\$\{(?!(?:num\()?v\.\w+\)?\})/.test(m[1])) return null;
    return m[1].replace(/\$\{(?:num\()?v\.(\w+)\)?\}/g, "{$1}");
  };
  const put = (key, ar, en) => {
    const [a, e] = [template(ar), template(en)];
    if (a == null || e == null) functions[key] = [String(ar), String(en)];
    else strings[key] = [a, e];
  };

  for (const [key, [ar, en]] of Object.entries(vm.runInContext("__S", ctx))) put(key, ar, en);

  const ai = vm.runInContext("LamhaAI", ctx);
  // ERRORS is Arabic; its English twin is private, so read it through errorInfo().
  for (const code of Object.keys(ai.ERRORS)) {
    const [arTitle, arHint] = ai.errorInfo(code, "Claude", "ar");
    const [enTitle, enHint] = ai.errorInfo(code, "Claude", "en");
    put(`ERRORS.${code}.title`, arTitle, enTitle);
    put(`ERRORS.${code}.hint`, arHint, enHint);
  }
  const fallbackAr = ai.errorInfo("?", "Claude", "ar");
  const fallbackEn = ai.errorInfo("?", "Claude", "en");
  put("ERRORS.other.title", fallbackAr[0], fallbackEn[0]);
  put("ERRORS.other.hint", fallbackAr[1], fallbackEn[1]);
  for (const name of Object.keys(ai.CATEGORIES)) {
    put(`CATEGORIES.${name}.label`, ai.catLabel(name, "ar"), ai.catLabel(name, "en"));
    put(`CATEGORIES.${name}.tip`, ai.catTip(name, "ar"), ai.catTip(name, "en"));
  }

  return { strings, functions };
}

const sorted = o => Object.fromEntries(Object.entries(o).sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0)));
const xml = s => s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");

const [command, ...args] = process.argv.slice(2);
if (command === "dump") {
  const { strings, functions } = load();
  const out = {
    about: "Lamha 2's interface strings [Arabic, English], made by tools/Strings/lamha2-strings.mjs dump. Don't edit by hand.",
    strings: sorted(strings),
    functions: sorted(functions),
  };
  fs.writeFileSync(jsonFile, JSON.stringify(out, null, 1) + "\n");
  console.log(`${Object.keys(strings).length} strings and ${Object.keys(functions).length} functions → ${path.relative(process.cwd(), jsonFile)}`);
} else if (command === "find" && args.length > 0) {
  const { strings, functions } = load();
  const needle = args.join(" ").toLowerCase();
  for (const [key, pair] of Object.entries({ ...strings, ...functions })) {
    if (key.toLowerCase().includes(needle) || pair.some(s => s.toLowerCase().includes(needle))) console.log(`${key}\n  ar: ${pair[0]}\n  en: ${pair[1]}`);
  }
} else if (command === "resw" && args.length > 0) {
  const { strings, functions } = load();
  const lines = [[], []];
  for (const arg of args) {
    const [name, key] = arg.split("=");
    const pair = strings[key];
    if (!pair) {
      console.error(functions[key] ? `${key} is a function: port it by hand` : `${key}: no such string`);
      process.exitCode = 1;
      continue;
    }
    // Lamha 2's {name} placeholders become {0}, {1}… in the order they first appear in the English.
    const names = [...new Set([...pair[1].matchAll(/\{(\w+)\}/g)].map(m => m[1]))];
    const numbered = s => names.reduce((t, n, i) => t.replaceAll(`{${n}}`, `{${i}}`), s);
    pair.forEach((value, i) => {
      lines[i].push(`  <data name="${name}" xml:space="preserve"><value>${xml(numbered(value))}</value><comment>${key}</comment></data>`);
    });
  }
  console.log("Strings/en/Resources.resw:\n" + lines[1].join("\n") + "\n\nStrings/ar/Resources.resw:\n" + lines[0].join("\n"));
} else {
  console.log("usage: node tools/Strings/lamha2-strings.mjs dump | find <text> | resw Name=key …");
  process.exitCode = 1;
}
