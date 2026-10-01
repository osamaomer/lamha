// Builds the helper that reads the sentence around a selection (uia-helper.cs → bin/lamha-uia.exe) with Windows'
// own C# compiler: .NET Framework 4.8 comes with every Windows 10 and 11 (and GitHub's Windows machines), so nothing
// is installed and users' PCs never compile anything. A window-less program (winexe): no console window, no conhost.
//   node scripts/build-helper.mjs           build when the source is newer than the program (npm start, dist, release)
//   buildHelper({ out, force })             the same from another script (tools/test-desktop.mjs builds its own copy)
import { spawnSync } from "node:child_process";
import { existsSync, mkdirSync, statSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const desktop = fileURLToPath(new URL("..", import.meta.url));
export const SOURCE = path.join(desktop, "uia-helper.cs");
export const OUT = path.join(desktop, "bin", "lamha-uia.exe");

export function buildHelper({ out = OUT, force = false } = {}) {
  if (process.platform !== "win32") return null; // the helper is Windows UI Automation
  if (!force && existsSync(out) && statSync(out).mtimeMs >= statSync(SOURCE).mtimeMs) return out;
  const fw = path.join(process.env.SystemRoot || "C:\\Windows", "Microsoft.NET", "Framework64", "v4.0.30319");
  const csc = path.join(fw, "csc.exe");
  if (!existsSync(csc)) throw new Error("the C# compiler of .NET Framework 4 isn't on this PC: " + csc);
  mkdirSync(path.dirname(out), { recursive: true });
  const refs = ["WPF\\UIAutomationClient.dll", "WPF\\UIAutomationTypes.dll", "WPF\\WindowsBase.dll", "System.Web.Extensions.dll"];
  const r = spawnSync(csc, ["/nologo", "/optimize+", "/target:winexe", "/out:" + out, ...refs.map(d => "/r:" + path.join(fw, d)), SOURCE], { encoding: "utf8" });
  if (r.status !== 0) throw new Error("building the helper failed:\n" + (r.stdout || "") + (r.stderr || ""));
  return out;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const out = buildHelper({ force: process.argv.includes("--force") });
  console.log(out ? "helper: " + path.relative(desktop, out) : "helper: not built (Windows only)");
}
