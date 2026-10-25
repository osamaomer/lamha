/* Renders icons/icon.svg to the PNGs the desktop app needs (npm run icons):
 * assets/icon-256.png (window + installer) and assets/tray-32.png (notification area).
 * Drawn on a <canvas> in a hidden window, so nothing has to be painted on screen. */
"use strict";
const { app, BrowserWindow } = require("electron");
const fs = require("node:fs");
const path = require("node:path");

const svg = fs.readFileSync(path.join(__dirname, "..", "..", "icons", "icon.svg"), "utf8");
const out = path.join(__dirname, "..", "assets");

const draw = size => `new Promise((resolve, reject) => {
  const img = new Image();
  img.onload = () => {
    const c = document.createElement("canvas");
    c.width = c.height = ${size};
    c.getContext("2d").drawImage(img, 0, 0, ${size}, ${size});
    resolve(c.toDataURL("image/png").split(",")[1]);
  };
  img.onerror = () => reject(new Error("svg did not load"));
  img.src = "data:image/svg+xml;base64,${Buffer.from(svg).toString("base64")}";
})`;

app.whenReady().then(async () => {
  const timer = setTimeout(() => { console.error("timed out"); app.exit(1); }, 20000);
  fs.mkdirSync(out, { recursive: true });
  const win = new BrowserWindow({ show: false });
  await win.loadURL("about:blank");
  for (const [name, size] of [["icon-256.png", 256], ["tray-32.png", 32]]) {
    fs.writeFileSync(path.join(out, name), Buffer.from(await win.webContents.executeJavaScript(draw(size)), "base64"));
  }
  clearTimeout(timer);
  console.log("icons written to", out);
  app.exit(0);
});
