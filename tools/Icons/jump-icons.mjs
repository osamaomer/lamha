// The jump list's icons: the pane's coloured icons (docs/design/system.html, signed off in decision 0020) as .ico
// files for Windows, which draws the jump list itself and takes icons only from files.
//
//   node tools/Icons/jump-icons.mjs [edge.exe]
//
// Each icon is drawn by Edge (headless) at each size Windows asks a jump list for, so the small ones are drawn small
// rather than shrunk, and the PNGs are packed into one .ico per page in src/Lamha.App/Assets/Jump. Run it again when a
// pane icon changes in system.html (PaneIcon.cs draws the same icons in the app).
import { execFileSync } from "node:child_process";
import { mkdtempSync, readFileSync, writeFileSync, mkdirSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "../..");
const edge = process.argv[2] ?? "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe";
const pages = ["translate", "write", "review", "clipboard"];

// 100 %, 125 %, 150 %, 200 % and 250 % of the jump list's 16 px, and Windows' 48 for large icons.
const sizes = [16, 20, 24, 32, 40, 48];

// The page's own drawings: the gear helper, the id counter and the COLOR table, from its script.
const html = readFileSync(join(root, "docs/design/system.html"), "utf8");
const start = html.indexOf("let gid = 0;");
const end = html.indexOf("};", html.indexOf("const COLOR = {")) + 2;
if (start < 0 || end < 2) {
  throw new Error("system.html: the pane icons' COLOR table wasn't found");
}

const COLOR = new Function(`${html.slice(start, end)}\nreturn COLOR;`)();

const work = mkdtempSync(join(tmpdir(), "lamha-jump-"));
const out = join(root, "src/Lamha.App/Assets/Jump");
mkdirSync(out, { recursive: true });
try {
  for (const page of pages) {
    const pngs = sizes.map(size => {
      const svg = COLOR[page]().replace("<svg ", `<svg width="${size}" height="${size}" style="display:block" `);
      const file = join(work, `${page}-${size}.html`);
      writeFileSync(file, `<!doctype html><meta charset="utf-8"><body style="margin:0;background:transparent">${svg}</body>`);
      const png = join(work, `${page}-${size}.png`);
      execFileSync(edge, ["--headless", "--disable-gpu", "--hide-scrollbars", "--default-background-color=00000000",
        `--window-size=${size},${size}`, `--screenshot=${png}`, pathToFileURL(file).href], { stdio: "ignore" });
      return { size, data: readFileSync(png) };
    });
    writeFileSync(join(out, `${page}.ico`), ico(pngs));
    console.log(`${page}.ico: ${sizes.join(", ")}`);
  }
} finally {
  rmSync(work, { recursive: true, force: true });
}

/** An .ico of PNG images (Windows Vista and later read PNGs in an .ico): the header, one entry per image, the images. */
function ico(images) {
  const header = Buffer.alloc(6 + 16 * images.length);
  header.writeUInt16LE(0, 0); // reserved
  header.writeUInt16LE(1, 2); // an icon
  header.writeUInt16LE(images.length, 4);
  let offset = header.length;
  images.forEach(({ size, data }, i) => {
    const at = 6 + 16 * i;
    header.writeUInt8(size % 256, at); // 256 is written 0
    header.writeUInt8(size % 256, at + 1);
    header.writeUInt8(0, at + 2); // no palette
    header.writeUInt8(0, at + 3);
    header.writeUInt16LE(1, at + 4); // planes
    header.writeUInt16LE(32, at + 6); // bits per pixel
    header.writeUInt32LE(data.length, at + 8);
    header.writeUInt32LE(offset, at + 12);
    offset += data.length;
  });
  return Buffer.concat([header, ...images.map(i => i.data)]);
}
