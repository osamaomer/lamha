// Command-line choices shared by the test files: `node tools/test-<name>.mjs [-q] [text]`.
// text: run only the tests whose name contains it (any case). -q: print only failures and the total.
// Other flags (test-writing's --ollama, --gemini) are left to the file.
const args = process.argv.slice(2);
export const quiet = args.includes("-q");
export const only = args.filter(a => !a.startsWith("-")).join(" ").toLowerCase();
export const wanted = name => !only || String(name).toLowerCase().includes(only);
export const report = line => { if (!quiet || line.includes("✗")) console.log(line); };
export const title = line => { if (!quiet && !only) console.log(line); };
export const notRun = n => n ? `, ${n} not run (filter "${only}")` : "";
