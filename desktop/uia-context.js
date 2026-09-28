/* Lamha desktop — a hidden helper process for reading other apps, with two jobs:
 * 1. The sentence around a selection in another app, so a lookup there can pick the meaning that fits
 *    ("Understand words from their sentence"), as the browser extension does with the page's text.
 * 2. The Write button on a double-click in an empty text box: a low-level mouse hook that reports left-button
 *    presses (main.js pairs them into double-clicks, see double-click.js), and the question "is the focused box
 *    under this double-click empty?" (and, in a browser, "did the Lamha extension show its own button there?").
 * Windows UI Automation (what screen readers use) answers the questions. It all runs in one long-lived, hidden
 * PowerShell process: the managed UIA client is reliable, a slow or hung app can't block Lamha's main process, and
 * after the first start each question takes a few tens of milliseconds. The mouse hook is a few lines of C#, compiled
 * by Windows' own .NET the first time it's needed; it runs on its own thread here, so every mouse event on the PC
 * costs microseconds and never waits for Lamha's main process.
 * Privacy: the sentence is read only on the lookup shortcut, only ~400 characters on each side, only when the app's
 * selection is the text Lamha just copied. The hook sees mouse buttons only, never the keyboard. A text box is read
 * only to know whether it's empty (its text never leaves this process). Nothing is logged. */
"use strict";
const { spawn } = require("node:child_process");
const path = require("node:path");

const CHARS = 400;

// Input: one JSON line per question: { id, hwnd, text } (the sentence), { id, kind: "mouse", on } (the hook),
// { id, kind: "field", x, y, names, wait } (the box under a double-click).
// Output: one JSON line per answer { id, … , error? }, plus { down: { t, x, y } } for each left-button press while
// the hook is on (physical screen pixels; t is Windows' millisecond tick count).
const SCRIPT = String.raw`
$ErrorActionPreference = 'Stop'
[Console]::InputEncoding = New-Object Text.UTF8Encoding $false
[Console]::OutputEncoding = New-Object Text.UTF8Encoding $false
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$TP = [System.Windows.Automation.TextPattern]::Pattern
$VP = [System.Windows.Automation.ValuePattern]::Pattern
$ReadOnly = [System.Windows.Automation.TextPattern]::IsReadOnlyAttribute
$Start = [System.Windows.Automation.Text.TextPatternRangeEndpoint]::Start
$End = [System.Windows.Automation.Text.TextPatternRangeEndpoint]::End
$Char = [System.Windows.Automation.Text.TextUnit]::Character
$Walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
$Scope = [System.Windows.Automation.TreeScope]
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
      foreach ($d in $root.FindAll($Scope::Descendants, $TextCond)) {
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

# ---- the Write button on a double-click ----

# The low-level mouse hook, on its own thread with its own message loop. Its callback only queues left-button presses;
# another thread writes them out, so the callback returns at once (Windows drops hooks that are slow).
$MouseSource = @'
using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;

public static class LamhaOut {
  static readonly object Gate = new object();
  public static void Line(string s) { lock (Gate) { Console.Out.WriteLine(s); Console.Out.Flush(); } }
}

public static class LamhaMouse {
  [StructLayout(LayoutKind.Sequential)] struct POINT { public int x; public int y; }
  [StructLayout(LayoutKind.Sequential)] struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData; public uint flags; public uint time; public IntPtr extra; }
  [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public POINT pt; }
  delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
  [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int idHook, HookProc fn, IntPtr hMod, uint threadId);
  [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
  [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);
  [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
  [DllImport("user32.dll")] static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);
  [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
  [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
  [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string name);

  static readonly HookProc Proc = Hook; // kept alive: Windows calls this delegate, the GC must not collect it
  static readonly BlockingCollection<string> Downs = new BlockingCollection<string>();
  static IntPtr hook = IntPtr.Zero;
  static uint threadId;
  static Thread loop, writer;

  // Physical pixels everywhere: the hook's points and UI Automation's rectangles then agree on scaled screens.
  public static bool DpiAware() {
    try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return true; } catch (EntryPointNotFoundException) { }
    return SetProcessDPIAware();
  }

  static IntPtr Hook(int nCode, IntPtr wParam, IntPtr lParam) {
    if (nCode >= 0 && wParam.ToInt64() == 0x0201) { // WM_LBUTTONDOWN
      MSLLHOOKSTRUCT s = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
      Downs.Add("{\"down\":{\"t\":" + s.time + ",\"x\":" + s.pt.x + ",\"y\":" + s.pt.y + "}}");
    }
    return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
  }

  public static bool Start() {
    if (loop != null) return true;
    if (writer == null) {
      writer = new Thread(() => { foreach (string line in Downs.GetConsumingEnumerable()) LamhaOut.Line(line); });
      writer.IsBackground = true;
      writer.Start();
    }
    ManualResetEvent ready = new ManualResetEvent(false);
    Thread t = new Thread(() => {
      threadId = GetCurrentThreadId();
      hook = SetWindowsHookEx(14 /* WH_MOUSE_LL */, Proc, GetModuleHandle(null), 0);
      ready.Set();
      if (hook == IntPtr.Zero) return;
      MSG m;
      while (GetMessage(out m, IntPtr.Zero, 0, 0) > 0) { }
      UnhookWindowsHookEx(hook);
      hook = IntPtr.Zero;
    });
    t.IsBackground = true;
    t.Start();
    ready.WaitOne(3000);
    if (hook == IntPtr.Zero) return false;
    loop = t;
    return true;
  }

  public static void Stop() {
    if (loop == null) return;
    PostThreadMessage(threadId, 0x0012 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero);
    loop.Join(1000);
    loop = null;
  }
}
'@
$script:mouseLoaded = $false

function Say([string]$s) {
  if ($script:mouseLoaded) { [LamhaOut]::Line($s) } else { [Console]::Out.WriteLine($s); [Console]::Out.Flush() }
}

function MouseHook([bool]$on) {
  if (-not $script:mouseLoaded) {
    if (-not $on) { return $false }
    Add-Type -TypeDefinition $MouseSource # about a second, once
    [void][LamhaMouse]::DpiAware()
    $script:mouseLoaded = $true
  }
  if ($on) { return [LamhaMouse]::Start() }
  [LamhaMouse]::Stop()
  return $false
}

# Did the Lamha extension show its Write button in this web page? It's drawn at the end of the page, so only the
# document's last few children are searched (a whole page could take long).
function LamhaButton($doc, $names) {
  $conds = @($names | ForEach-Object { New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, [string]$_) })
  $cond = if ($conds.Count -gt 1) { New-Object System.Windows.Automation.OrCondition($conds) } else { $conds[0] }
  $child = $Walker.GetLastChild($doc)
  for ($k = 0; $k -lt 4 -and $child; $k++) {
    if ($names -contains $child.Current.Name) { return $true }
    if ($child.FindFirst($Scope::Descendants, $cond)) { return $true }
    $child = $Walker.GetPreviousSibling($child)
  }
  return $false
}

# The focused text box under a double-click at (x, y) when nothing is written in it:
# @{ empty; web (inside a web page); lamha (the extension's own button showed) }, @{ empty = $false }, or $null.
function Field([int]$x, [int]$y, $names, [int]$wait) {
  $clock = [Diagnostics.Stopwatch]::StartNew()
  Start-Sleep -Milliseconds 60 # let the app handle the click first
  $f = $A::FocusedElement
  if (-not $f) { return $null }
  $c = $f.Current
  if (($c.ControlType -ne $CT::Edit -and $c.ControlType -ne $CT::Document) -or $c.IsPassword -or -not $c.IsEnabled) { return $null }
  $r = $c.BoundingRectangle
  if ($r.IsEmpty -or $x -lt $r.Left - 2 -or $x -gt $r.Right + 2 -or $y -lt $r.Top - 2 -or $y -gt $r.Bottom + 2) { return $null } # the click was elsewhere
  $p = $null
  if ($f.TryGetCurrentPattern($VP, [ref]$p)) {
    if ($p.Current.IsReadOnly) { return $null }
    $text = $p.Current.Value
  } elseif ($f.TryGetCurrentPattern($TP, [ref]$p)) {
    if ($p.DocumentRange.GetAttributeValue($ReadOnly) -eq $true) { return $null }
    $text = $p.DocumentRange.GetText(64)
  } else { return $null }
  if ($text -and $text.Trim()) { return @{ empty = $false } }
  $out = @{ empty = $true; web = $false }
  if ($c.FrameworkId -eq 'Chrome' -or $c.FrameworkId -eq 'Gecko') { # web content (browsers, Electron apps), not a browser's own address bar
    $doc = $f; $owner = $c.ProcessId
    for ($i = 0; $i -lt 40 -and $doc -and $doc.Current.ControlType -ne $CT::Document; $i++) {
      $doc = $Walker.GetParent($doc)
      if ($doc -and $doc.Current.ProcessId -ne $owner) { $doc = $null }
    }
    if ($doc) {
      $out.web = $true
      if ($names) {
        $left = $wait - $clock.ElapsedMilliseconds
        if ($left -gt 0) { Start-Sleep -Milliseconds $left } # the extension draws its button on the double-click's release
        $out.lamha = LamhaButton $doc @($names)
      }
    }
  }
  return $out
}

while ($null -ne ($line = [Console]::In.ReadLine())) {
  $out = @{ id = 0 }
  try {
    $q = $line | ConvertFrom-Json
    $out.id = $q.id
    if ($q.kind -eq 'mouse') { $out.on = MouseHook ([bool]$q.on) }
    elseif ($q.kind -eq 'field') { $fld = Field ([int]$q.x) ([int]$q.y) $q.names ([int]$q.wait); if ($fld) { $out.field = $fld } }
    else {
      $ctx = Around ([int64]$q.hwnd) (Norm $q.text) ${CHARS}
      if ($ctx) { $out.before = $ctx.before; $out.after = $ctx.after }
    }
  } catch { $out.error = $_.Exception.GetType().Name } # the type only: a message could quote the app's text
  Say ($out | ConvertTo-Json -Compress)
}
`;

class UiaContext {
  constructor() {
    this.proc = null;
    this.pending = new Map(); // id → resolve
    this.seq = 0;
    this.buf = "";
    /** ({ t, x, y }) for each left-button press while watchMouse(true) is in force. */
    this.onMouseDown = () => {};
    this.mouseWanted = false;
    this.restarts = 0;
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
        if (msg && msg.down) { this.onMouseDown(msg.down); continue; }
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
      // it crashed or was ended from outside while the Write button needs it: start again, a few times at most
      if (this.mouseWanted && this.restarts++ < 5) setTimeout(() => { if (this.mouseWanted && !this.proc) this.watchMouse(true); }, 3000).unref();
    };
    proc.on("exit", gone);
    proc.on("error", gone);
    proc.stdin.on("error", () => {}); // the helper exited while we wrote: `gone` answers for it
  }

  /** One question to the helper; resolves to its answer, or null (no helper, timed out). Never rejects. */
  ask(question, timeout) {
    this.start();
    if (!this.proc) return Promise.resolve(null);
    const id = ++this.seq;
    return new Promise(resolve => {
      const timer = setTimeout(() => { this.pending.delete(id); resolve(null); }, timeout);
      this.pending.set(id, msg => { clearTimeout(timer); resolve(msg); });
      this.proc.stdin.write(JSON.stringify({ id, ...question }) + "\n");
    });
  }

  /**
   * { before, after } around `text` selected in window `hwnd`, or null (the app has no accessible text, its selection
   * isn't `text`, it's too slow, or the helper isn't available). Never rejects.
   */
  async around(hwnd, text, timeout = 700) {
    if (!hwnd || !text) return null;
    const msg = await this.ask({ hwnd, text }, timeout);
    return msg && typeof msg.before === "string" && typeof msg.after === "string" ? { before: msg.before, after: msg.after } : null;
  }

  /** Turns the mouse hook on or off. Resolves to whether it's on (false: the helper or the hook isn't available). */
  async watchMouse(on) {
    this.mouseWanted = !!on;
    if (!on && !this.proc) return false;
    const msg = await this.ask({ kind: "mouse", on: !!on }, 15000); // the first time compiles the hook
    return !!(msg && msg.on);
  }

  /**
   * The focused text box under a double-click at (x, y) in physical pixels: { empty, web, lamha } when it's empty
   * (lamha: Lamha's browser extension showed its own button there, looked for by `names`), { empty: false } when
   * something is written in it, or null (not a text box, not under the click, no answer).
   */
  async field(x, y, names = [], timeout = 1500) {
    const msg = await this.ask({ kind: "field", x: Math.round(x), y: Math.round(y), names, wait: 350 }, timeout);
    const f = msg && msg.field;
    return f && typeof f === "object" ? { empty: f.empty === true, web: f.web === true, lamha: f.lamha === true } : null;
  }

  stop() {
    const proc = this.proc;
    this.proc = null;
    this.mouseWanted = false;
    for (const done of this.pending.values()) done(null);
    this.pending.clear();
    if (proc) { try { proc.stdin.end(); proc.kill(); } catch (_) { /* already gone */ } }
  }
}

module.exports = { UiaContext, SCRIPT, CHARS };
