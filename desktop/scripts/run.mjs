// Starts Electron with the given arguments (default: this app). Clears ELECTRON_RUN_AS_NODE, which
// VS Code sets for its own processes and which would make Electron behave as plain Node.
import { spawn } from "node:child_process";
import { createRequire } from "node:module";

const electron = createRequire(import.meta.url)("electron"); // path to electron.exe
const env = { ...process.env };
delete env.ELECTRON_RUN_AS_NODE;
const args = process.argv.slice(2);
const child = spawn(electron, args.length && !args[0].startsWith("--") ? args : [".", ...args], {
  cwd: new URL("..", import.meta.url), stdio: "inherit", env, windowsHide: false
});
child.on("exit", code => process.exit(code ?? 0));
