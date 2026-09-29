/* Lamha desktop — the Firefox extension's link to this app (native messaging).
 *
 * Firefox starts a "native messaging host" named com.artworklab.lamha, found through
 * HKCU\Software\Mozilla\NativeMessagingHosts\com.artworklab.lamha → a manifest JSON → host.bat. The host is a small
 * PowerShell script (Lamha.exe can't be it: its run-as-Node switch is off on purpose) that passes each message on to
 * this app over a named pipe and each answer back: Firefox speaks 4-byte-length + JSON on stdin/stdout, the pipe one
 * JSON line each way, the first line being a secret token (so only a host this app wrote can talk to it).
 * The app is started (in the tray) only for a message that asks for it (`launch: true`: opening an article), never
 * for the card's background lookups.
 *
 * Files, all in %APPDATA%\Lamha\firefox: com.artworklab.lamha.json, host.bat, host.ps1, config.json (pipe, token,
 * how to start the app). Only the extension lamha@artwork-lab may start the host. */
"use strict";
const fs = require("node:fs");
const net = require("node:net");
const path = require("node:path");
const crypto = require("node:crypto");
const { execFile } = require("node:child_process");
const { EventEmitter } = require("node:events");

const HOST = "com.artworklab.lamha";
const EXTENSION_ID = "lamha@artwork-lab";
const REG_KEY = "HKCU\\Software\\Mozilla\\NativeMessagingHosts\\" + HOST;
const MAX_LINE = 1100 * 1024; // the host passes on messages up to 1 MB (a page of the review deck, when the two decks meet)
const MAX_REPLY = 1000 * 1024; // Firefox refuses messages from a host over 1 MB (bytes: Arabic takes two each)

const HOST_BAT = [
  "@echo off",
  "rem Lamha: Firefox starts this to reach the Windows app (native messaging). Written by the app; see host.ps1.",
  "\"%SystemRoot%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe\" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"%~dp0host.ps1\"",
  ""
].join("\r\n");

const HOST_SCRIPT = String.raw`# Lamha: the Firefox extension's link to the Windows app, written by the app (desktop/native-bridge.js).
# Firefox talks to this over stdin/stdout (each message: a 4-byte length, then UTF-8 JSON); it passes every message on
# to the running app over a named pipe, one line each way, and sends the answer back.
$ErrorActionPreference = "Stop"
$cfg = $null
$utf8 = New-Object System.Text.UTF8Encoding($false)
$stdin = [Console]::OpenStandardInput()
$stdout = [Console]::OpenStandardOutput()

function Read-Exactly([int]$n) {
  $buf = New-Object byte[] $n
  $got = 0
  while ($got -lt $n) {
    $r = $stdin.Read($buf, $got, $n - $got)
    if ($r -le 0) { return $null }
    $got += $r
  }
  return ,$buf
}
function Send([string]$json) {
  $bytes = $utf8.GetBytes($json)
  $stdout.Write([BitConverter]::GetBytes([int]$bytes.Length), 0, 4)
  $stdout.Write($bytes, 0, $bytes.Length)
  $stdout.Flush()
}
# the app's pipe; with $launch, the app is started in the tray when it isn't running, and waited for (20 s at most).
# config.json is read again each time: the app writes a new pipe name and token whenever it starts.
function Open-App([bool]$launch) {
  for ($i = 0; $i -lt 80; $i++) {
    $script:cfg = Get-Content -Raw -Encoding UTF8 (Join-Path $PSScriptRoot "config.json") | ConvertFrom-Json
    $p = New-Object System.IO.Pipes.NamedPipeClientStream(".", $cfg.pipe, [System.IO.Pipes.PipeDirection]::InOut)
    try { $p.Connect(100); return $p } catch { $p.Dispose() }
    if (-not $launch) { return $null }
    if ($i -eq 0) { try { Start-Process -FilePath $cfg.app -ArgumentList $cfg.args } catch { return $null } }
    Start-Sleep -Milliseconds 150
  }
  return $null
}

$pipe = $null; $reader = $null; $writer = $null
while ($true) {
  $head = Read-Exactly 4
  if ($null -eq $head) { break }
  $n = [BitConverter]::ToInt32($head, 0)
  if ($n -le 0 -or $n -gt 1048576) { break }
  $body = Read-Exactly $n
  if ($null -eq $body) { break }
  $raw = $utf8.GetString($body)
  $msg = $raw | ConvertFrom-Json
  if ($null -eq $pipe -or -not $pipe.IsConnected) {
    $pipe = Open-App ($msg.launch -eq $true)
    if ($null -ne $pipe) {
      $reader = New-Object System.IO.StreamReader($pipe, $utf8)
      $writer = New-Object System.IO.StreamWriter($pipe, $utf8)
      $writer.AutoFlush = $true
      $writer.WriteLine((@{ token = $cfg.token } | ConvertTo-Json -Compress))
    }
  }
  $line = $null
  if ($null -ne $pipe) {
    try { $writer.WriteLine($raw); $line = $reader.ReadLine() } catch { $line = $null }
    if ($null -eq $line) { $pipe.Dispose(); $pipe = $null }
  }
  if ($null -eq $line) { $line = (@{ id = $msg.id; ok = $false; error = "no_app" } | ConvertTo-Json -Compress) }
  Send $line
}
if ($null -ne $pipe) { $pipe.Dispose() }
`;

/** reg.exe, quietly. */
function reg(args) {
  return new Promise((resolve, reject) => {
    execFile("reg.exe", args, { windowsHide: true, timeout: 15000 }, err => (err ? reject(err) : resolve()));
  });
}

class NativeBridge extends EventEmitter {
  /**
   * dir: where the host's files go. launch: { app, args } to start this app in the tray. handlers: { type: async msg → data }.
   * register(manifestPath) / unregister(): the registry key (reg.exe by default; the tests pass their own).
   */
  constructor({ dir, launch, handlers, register, unregister }) {
    super();
    this.dir = dir;
    this.launch = launch;
    this.handlers = handlers;
    this.register = register || (file => reg(["add", REG_KEY, "/ve", "/t", "REG_SZ", "/d", file, "/f"]));
    this.unregister = unregister || (() => reg(["delete", REG_KEY, "/f"]).catch(() => {}));
    this.pipe = ""; // chosen at random when it starts listening (enable)
    this.server = null;
    this.token = "";
    this.clients = new Set();
    this.lastSeen = 0;
  }

  get manifestPath() { return path.join(this.dir, HOST + ".json"); }
  get connected() { return this.clients.size > 0; }

  /** Writes the host's files, registers it for Firefox and listens. Again: refreshes them (the app may have moved). */
  async enable() {
    this.token = crypto.randomBytes(24).toString("hex");
    // A name nobody can guess (it's only in config.json, which only this Windows account can read): named pipes are
    // machine-wide, so a fixed one could be taken first by another account on the PC, which would then get the token
    // and the words looked up. A new one each time the app starts listening.
    if (!this.server) this.pipe = "lamha-firefox-" + crypto.randomBytes(16).toString("hex");
    await fs.promises.mkdir(this.dir, { recursive: true });
    const write = (name, text) => fs.promises.writeFile(path.join(this.dir, name), text, "utf8");
    await write("host.bat", HOST_BAT);
    await write("host.ps1", "﻿" + HOST_SCRIPT); // with a BOM: Windows PowerShell reads it as UTF-8
    // Start-Process joins its arguments with spaces: a path with spaces (the app folder, from source) needs its quotes
    const args = this.launch.args.map(a => (/[\s"]/.test(a) ? `"${a.replace(/"/g, "")}"` : a)).join(" ");
    await write("config.json", JSON.stringify({ pipe: this.pipe, token: this.token, app: this.launch.app, args }, null, 1));
    await write(HOST + ".json", JSON.stringify({
      name: HOST,
      description: "Lamha for Windows: the Firefox extension's link to the app",
      path: path.join(this.dir, "host.bat"),
      type: "stdio",
      allowed_extensions: [EXTENSION_ID]
    }, null, 1));
    await this.listen();
    await this.register(this.manifestPath);
  }

  /** Firefox can no longer start the host; the files go, and anyone connected is let go. */
  async disable() {
    await this.close();
    await this.unregister();
    await fs.promises.rm(this.dir, { recursive: true, force: true });
    this.emit("change");
  }

  listen() {
    if (this.server) return Promise.resolve();
    return new Promise((resolve, reject) => {
      const server = net.createServer(sock => this.accept(sock));
      server.once("error", reject);
      server.listen("\\\\.\\pipe\\" + this.pipe, () => { this.server = server; server.removeListener("error", reject); resolve(); });
    });
  }

  async close() {
    for (const c of this.clients) c.destroy();
    this.clients.clear();
    if (this.server) await new Promise(r => this.server.close(() => r()));
    this.server = null;
  }

  accept(sock) {
    let buf = "", trusted = false, busy = false;
    sock.setEncoding("utf8");
    sock.on("error", () => {});
    sock.on("close", () => { if (this.clients.delete(sock)) this.emit("change"); });
    const next = async () => { // one request at a time, in order: the host waits for each answer anyway
      if (busy) return;
      busy = true;
      let nl;
      while ((nl = buf.indexOf("\n")) >= 0 && !sock.destroyed) {
        const line = buf.slice(0, nl).replace(/\r$/, "");
        buf = buf.slice(nl + 1);
        let msg = null;
        try { msg = JSON.parse(line); } catch (_) { /* answered as unknown */ }
        if (!trusted) { // the first line: the token from config.json, or goodbye
          const t = msg && typeof msg.token === "string" ? Buffer.from(msg.token) : Buffer.alloc(0);
          const want = Buffer.from(this.token);
          if (!this.token || t.length !== want.length || !crypto.timingSafeEqual(t, want)) { sock.destroy(); break; }
          trusted = true;
          this.clients.add(sock);
          this.emit("change");
          continue;
        }
        const reply = await this.answer(msg);
        if (!sock.destroyed) sock.write(JSON.stringify(reply) + "\n");
      }
      busy = false;
    };
    sock.on("data", chunk => {
      buf += chunk;
      if (buf.length > MAX_LINE) { sock.destroy(); return; }
      next();
    });
  }

  async answer(msg) {
    const id = msg && (typeof msg.id === "number" || typeof msg.id === "string") ? msg.id : null;
    if (!msg || typeof msg.type !== "string" || !Object.hasOwn(this.handlers, msg.type)) return { id, ok: false, error: "unknown" };
    this.lastSeen = Date.now();
    try {
      const reply = { id, ok: true, data: await this.handlers[msg.type](msg) };
      return Buffer.byteLength(JSON.stringify(reply)) > MAX_REPLY ? { id, ok: false, error: "too_big" } : reply;
    } catch (err) {
      return { id, ok: false, error: (err && err.code) || "failed" };
    }
  }
}

module.exports = { NativeBridge, HOST, EXTENSION_ID, REG_KEY, HOST_SCRIPT, HOST_BAT };
