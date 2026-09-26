/* Lamha desktop — the sentence around a selection in another app, so a lookup there can pick the meaning that fits
 * ("Understand words from their sentence"), as the browser extension does with the page's text.
 * Windows UI Automation (what screen readers use) is asked for the text before and after the app's current selection.
 * It runs in one long-lived, hidden PowerShell process: the managed UIA client is reliable, a slow or hung app can't
 * block Lamha's main process, and after the first start each question takes a few tens of milliseconds.
 * Privacy: only on the lookup shortcut, only ~400 characters on each side, only when the app's selection is the text
 * Lamha just copied; nothing is logged. */
"use strict";
const { spawn } = require("node:child_process");
const path = require("node:path");

const CHARS = 400;

// Input: one JSON line per question { id, hwnd, text }. Output: one JSON line { id, before?, after?, error? }.
const SCRIPT = String.raw`
$ErrorActionPreference = 'Stop'
[Console]::InputEncoding = New-Object Text.UTF8Encoding $false
[Console]::OutputEncoding = New-Object Text.UTF8Encoding $false
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$TP = [System.Windows.Automation.TextPattern]::Pattern
$Start = [System.Windows.Automation.Text.TextPatternRangeEndpoint]::Start
$End = [System.Windows.Automation.Text.TextPatternRangeEndpoint]::End
$Char = [System.Windows.Automation.Text.TextUnit]::Character
$Walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
$CT = [System.Windows.Automation.ControlType]
$TextConds = @(
  (New-Object System.Windows.Automation.PropertyCondition($A::IsTextPatternAvailableProperty, $true)),
  (New-Object System.Windows.Automation.OrCondition(
    (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, $CT::Document)),
    (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, $CT::Edit))))
)
$TextCond = New-Object System.Windows.Automation.AndCondition($TextConds)

function Norm([string]$s) { return ($s -replace '\s+', ' ').Trim() }

# The range of $el's selection whose text is $want, or $null.
function SelectionIn($el, [string]$want) {
  $p = $null
  if (-not $el.TryGetCurrentPattern($TP, [ref]$p)) { return $null }
  foreach ($r in $p.GetSelection()) { if ((Norm $r.GetText(2000)) -eq $want) { return $r } }
  return $null
}

function Around([int64]$hwnd, [string]$want, [int]$n) {
  $win = $A::FromHandle([IntPtr]$hwnd)
  $owner = $win.Current.ProcessId
  $f = $A::FocusedElement
  if (-not $f -or $f.Current.ProcessId -ne $owner) { return $null } # focus moved to another program: read nothing
  # 1. the focused element or one of its ancestors (Word, Notepad, most edit boxes)
  $r = $null; $e = $f
  for ($i = 0; $i -lt 15 -and $e -and -not $r; $i++) {
    $r = SelectionIn $e $want
    if (-not $r) { $e = $Walker.GetParent($e); if ($e -and $e.Current.ProcessId -ne $owner) { $e = $null } }
  }
  # 2. a document below the focused pane or in the window (Chromium apps: VS Code, Teams, Slack, browsers)
  if (-not $r) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    foreach ($root in @($f, $win)) {
      foreach ($d in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $TextCond)) {
        $r = SelectionIn $d $want
        if ($r -or $clock.ElapsedMilliseconds -gt 500) { break }
      }
      if ($r -or $clock.ElapsedMilliseconds -gt 500) { break }
    }
  }
  if (-not $r) { return $null }
  $b = $r.Clone(); $b.MoveEndpointByRange($End, $r, $Start); [void]$b.MoveEndpointByUnit($Start, $Char, -$n)
  $a = $r.Clone(); $a.MoveEndpointByRange($Start, $r, $End); [void]$a.MoveEndpointByUnit($End, $Char, $n)
  return @{ before = $b.GetText($n + 16); after = $a.GetText($n + 16) }
}

while ($null -ne ($line = [Console]::In.ReadLine())) {
  $out = @{ id = 0 }
  try {
    $q = $line | ConvertFrom-Json
    $out.id = $q.id
    $ctx = Around ([int64]$q.hwnd) (Norm $q.text) ${CHARS}
    if ($ctx) { $out.before = $ctx.before; $out.after = $ctx.after }
  } catch { $out.error = $_.Exception.GetType().Name } # the type only: a message could quote the app's text
  [Console]::Out.WriteLine(($out | ConvertTo-Json -Compress))
  [Console]::Out.Flush()
}
`;

class UiaContext {
  constructor() {
    this.proc = null;
    this.pending = new Map(); // id → resolve
    this.seq = 0;
    this.buf = "";
  }

  /** Starts the helper (takes about a second; done ahead of the first shortcut). */
  start() {
    if (this.proc || process.platform !== "win32") return;
    const exe = path.join(process.env.SystemRoot || "C:\\Windows", "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
    const args = ["-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", Buffer.from(SCRIPT, "utf16le").toString("base64")];
    let proc;
    try { proc = spawn(exe, args, { windowsHide: true, stdio: ["pipe", "pipe", "ignore"] }); } catch (_) { return; }
    this.proc = proc;
    proc.stdout.setEncoding("utf8");
    proc.stdout.on("data", chunk => {
      this.buf += chunk;
      let nl;
      while ((nl = this.buf.indexOf("\n")) >= 0) {
        const line = this.buf.slice(0, nl).trim();
        this.buf = this.buf.slice(nl + 1);
        let msg;
        try { msg = JSON.parse(line); } catch (_) { continue; }
        const done = this.pending.get(msg.id);
        if (done) { this.pending.delete(msg.id); done(msg); }
      }
    });
    const gone = () => {
      if (this.proc !== proc) return;
      this.proc = null;
      this.buf = "";
      for (const done of this.pending.values()) done(null);
      this.pending.clear();
    };
    proc.on("exit", gone);
    proc.on("error", gone);
    proc.stdin.on("error", () => {}); // the helper exited while we wrote: `gone` answers for it
  }

  /**
   * { before, after } around `text` selected in window `hwnd`, or null (the app has no accessible text, its selection
   * isn't `text`, it's too slow, or the helper isn't available). Never rejects.
   */
  around(hwnd, text, timeout = 700) {
    this.start();
    if (!this.proc || !hwnd || !text) return Promise.resolve(null);
    const id = ++this.seq;
    return new Promise(resolve => {
      const timer = setTimeout(() => { this.pending.delete(id); resolve(null); }, timeout);
      this.pending.set(id, msg => {
        clearTimeout(timer);
        resolve(msg && typeof msg.before === "string" && typeof msg.after === "string" ? { before: msg.before, after: msg.after } : null);
      });
      this.proc.stdin.write(JSON.stringify({ id, hwnd, text }) + "\n");
    });
  }

  stop() {
    const proc = this.proc;
    this.proc = null;
    for (const done of this.pending.values()) done(null);
    this.pending.clear();
    if (proc) { try { proc.stdin.end(); proc.kill(); } catch (_) { /* already gone */ } }
  }
}

module.exports = { UiaContext, SCRIPT, CHARS };
