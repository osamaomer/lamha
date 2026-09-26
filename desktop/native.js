/* Lamha desktop — the few Windows (user32) calls needed to work with other apps:
 * which window is in front, whether modifier keys are held, synthetic key presses, bringing a window forward,
 * and for clipboard history: clipboard change notifications, formats and the owning program.
 * Uses koffi (prebuilt FFI, nothing to compile). */
"use strict";
const koffi = require("koffi");

const user32 = koffi.load("user32.dll");
const kernel32 = koffi.load("kernel32.dll");

// HWNDs are handled as pointer-sized integers
const GetForegroundWindow = user32.func("intptr_t __stdcall GetForegroundWindow()");
const SetForegroundWindow = user32.func("bool __stdcall SetForegroundWindow(intptr_t hWnd)");
const BringWindowToTop = user32.func("bool __stdcall BringWindowToTop(intptr_t hWnd)");
const IsWindow = user32.func("bool __stdcall IsWindow(intptr_t hWnd)");
const GetAsyncKeyState = user32.func("int16_t __stdcall GetAsyncKeyState(int vKey)");
const keybd_event = user32.func("void __stdcall keybd_event(uint8_t bVk, uint8_t bScan, uint32_t dwFlags, uintptr_t dwExtraInfo)");
const GetWindowThreadProcessId = user32.func("uint32_t __stdcall GetWindowThreadProcessId(intptr_t hWnd, _Out_ uint32_t *lpdwProcessId)");
const AttachThreadInput = user32.func("bool __stdcall AttachThreadInput(uint32_t idAttach, uint32_t idAttachTo, bool fAttach)");
const GetClassNameW = user32.func("int __stdcall GetClassNameW(intptr_t hWnd, uint16_t *lpClassName, int nMaxCount)");
const GetCurrentThreadId = kernel32.func("uint32_t __stdcall GetCurrentThreadId()");

// clipboard history (clipboard-monitor.js)
const AddClipboardFormatListener = user32.func("bool __stdcall AddClipboardFormatListener(intptr_t hwnd)");
const RemoveClipboardFormatListener = user32.func("bool __stdcall RemoveClipboardFormatListener(intptr_t hwnd)");
const GetClipboardSequenceNumber = user32.func("uint32_t __stdcall GetClipboardSequenceNumber()");
const GetClipboardOwner = user32.func("intptr_t __stdcall GetClipboardOwner()");
const RegisterClipboardFormatW = user32.func("uint32_t __stdcall RegisterClipboardFormatW(str16 lpszFormat)");
const IsClipboardFormatAvailable = user32.func("bool __stdcall IsClipboardFormatAvailable(uint32_t format)");
const OpenClipboard = user32.func("bool __stdcall OpenClipboard(intptr_t hWndNewOwner)");
const CloseClipboard = user32.func("bool __stdcall CloseClipboard()");
const EmptyClipboard = user32.func("bool __stdcall EmptyClipboard()");
const GetClipboardData = user32.func("intptr_t __stdcall GetClipboardData(uint32_t uFormat)");
const SetClipboardData = user32.func("intptr_t __stdcall SetClipboardData(uint32_t uFormat, intptr_t hMem)");
const GlobalAlloc = kernel32.func("intptr_t __stdcall GlobalAlloc(uint32_t uFlags, size_t dwBytes)");
const GlobalLock = kernel32.func("void * __stdcall GlobalLock(intptr_t hMem)");
const GlobalUnlock = kernel32.func("bool __stdcall GlobalUnlock(intptr_t hMem)");
const GlobalSize = kernel32.func("size_t __stdcall GlobalSize(intptr_t hMem)");
const RtlMoveMemory = kernel32.func("void __stdcall RtlMoveMemory(void *dst, const void *src, size_t n)");
const OpenProcess = kernel32.func("intptr_t __stdcall OpenProcess(uint32_t dwDesiredAccess, bool bInheritHandle, uint32_t dwProcessId)");
const QueryFullProcessImageNameW = kernel32.func("bool __stdcall QueryFullProcessImageNameW(intptr_t hProcess, uint32_t dwFlags, _Out_ uint16_t *lpExeName, _Inout_ uint32_t *lpdwSize)");
const CloseHandle = kernel32.func("bool __stdcall CloseHandle(intptr_t hObject)");

const VK = { SHIFT: 0x10, CONTROL: 0x11, MENU: 0x12, LWIN: 0x5b, RWIN: 0x5c, RIGHT: 0x27, A: 0x41, C: 0x43, V: 0x56 };
const KEYUP = 0x0002;
const MODIFIERS = [VK.MENU, VK.SHIFT, VK.CONTROL, VK.LWIN, VK.RWIN];

const sleep = ms => new Promise(r => setTimeout(r, ms));
const isDown = vk => (GetAsyncKeyState(vk) & 0x8000) !== 0;

/** Waits until the user has let go of Alt/Shift/Ctrl/Win (the shortcut keys), then releases any still held. */
async function releaseModifiers(timeout = 800) {
  const until = Date.now() + timeout;
  while (MODIFIERS.some(isDown) && Date.now() < until) await sleep(15);
  for (const vk of MODIFIERS) if (isDown(vk)) keybd_event(vk, 0, KEYUP, 0);
}

/** Presses Ctrl+<key> in whatever window has the keyboard focus. */
function ctrlChord(vk) {
  keybd_event(VK.CONTROL, 0, 0, 0);
  keybd_event(vk, 0, 0, 0);
  keybd_event(vk, 0, KEYUP, 0);
  keybd_event(VK.CONTROL, 0, KEYUP, 0);
}

/** A single key press (used by the self-test, e.g. Right arrow to clear a selection). */
function tap(vk) {
  keybd_event(vk, 0, 0, 0);
  keybd_event(vk, 0, KEYUP, 0);
}

function className(hwnd) {
  const buf = new Uint16Array(256);
  const n = GetClassNameW(hwnd, buf, 256);
  return String.fromCharCode(...buf.subarray(0, Math.max(0, n)));
}

/** Terminals: Ctrl+C would interrupt the running program instead of copying. */
function isTerminal(hwnd) {
  return /^(ConsoleWindowClass|CASCADIA_HOSTING_WINDOW_CLASS|mintty|VirtualConsoleClass|PuTTY)$/.test(className(hwnd));
}

const threadOf = hwnd => GetWindowThreadProcessId(hwnd, [0]);

/** Brings `hwnd` to the front even when Windows' focus-stealing rules would refuse
 *  (attach to the current foreground thread's input for a moment). */
function forceForeground(hwnd) {
  if (!hwnd || !IsWindow(hwnd)) return false;
  if (GetForegroundWindow() === hwnd) return true;
  if (SetForegroundWindow(hwnd) && GetForegroundWindow() === hwnd) return true;
  const fg = GetForegroundWindow();
  const me = GetCurrentThreadId();
  const other = fg ? threadOf(fg) : 0;
  const attached = other && other !== me && AttachThreadInput(me, other, true);
  BringWindowToTop(hwnd);
  SetForegroundWindow(hwnd);
  if (attached) AttachThreadInput(me, other, false);
  return GetForegroundWindow() === hwnd;
}

/** BrowserWindow.getNativeWindowHandle() Buffer → HWND number */
const hwndOf = win => {
  const b = win.getNativeWindowHandle();
  return b.length >= 8 ? Number(b.readBigUInt64LE(0)) : b.readUInt32LE(0);
};

/* ---- clipboard history: formats, owner process, and a raw writer for the self-test ---- */

const formatIds = new Map();
/** Id of a registered clipboard format by name ("HTML Format", "Clipboard Viewer Ignore" …); 0 on failure. */
function clipboardFormat(name) {
  if (!formatIds.has(name)) formatIds.set(name, RegisterClipboardFormatW(name));
  return formatIds.get(name);
}
const hasClipboardFormat = name => { const id = clipboardFormat(name); return !!id && IsClipboardFormatAvailable(id); };

/** Opens the clipboard, retrying while another app holds it (3 × 50 ms, busy-wait: it's rare and short). */
function openClipboard(hwnd) {
  for (let i = 0; i < 4; i++) {
    if (OpenClipboard(hwnd || 0)) return true;
    const until = Date.now() + 50;
    while (Date.now() < until) { /* wait */ }
  }
  return false;
}

/** A DWORD-valued format's value (e.g. CanIncludeInClipboardHistory); null when absent or unreadable. */
function clipboardDword(name, hwnd) {
  const id = clipboardFormat(name);
  if (!id || !IsClipboardFormatAvailable(id) || !openClipboard(hwnd)) return null;
  try {
    const h = GetClipboardData(id);
    if (!h || GlobalSize(h) < 4) return null;
    const p = GlobalLock(h);
    if (!p) return null;
    try { return koffi.decode(p, "uint32_t"); } finally { GlobalUnlock(h); }
  } finally {
    CloseClipboard();
  }
}

/** Lowercase exe name of the process that owns `hwnd`, "lamha" for this app, or "" if unknown. */
function processNameOf(hwnd) {
  if (!hwnd) return "";
  const pidOut = [0];
  GetWindowThreadProcessId(hwnd, pidOut);
  const pid = pidOut[0];
  if (!pid) return "";
  if (pid === process.pid) return "lamha";
  const h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
  if (!h) return "";
  try {
    const buf = new Uint16Array(1024);
    const size = [buf.length];
    if (!QueryFullProcessImageNameW(h, 0, buf, size)) return "";
    const full = String.fromCharCode(...buf.subarray(0, size[0]));
    return full.slice(full.lastIndexOf("\\") + 1).toLowerCase();
  } finally {
    CloseHandle(h);
  }
}

function globalCopy(bytes) {
  const h = GlobalAlloc(0x0002 /* GMEM_MOVEABLE */, bytes.length);
  const p = GlobalLock(h);
  RtlMoveMemory(p, bytes, bytes.length);
  GlobalUnlock(h);
  return h;
}

/** Self-test only: puts `text` on the clipboard together with raw formats ({ name: DWORD }), in one transaction. */
function writeClipboardRaw(text, dwords = {}, hwnd = 0) {
  if (!openClipboard(hwnd)) return false;
  try {
    EmptyClipboard();
    SetClipboardData(13 /* CF_UNICODETEXT */, globalCopy(Buffer.from(text + "\0", "utf16le")));
    for (const [name, value] of Object.entries(dwords)) {
      const b = Buffer.alloc(4);
      b.writeUInt32LE(value);
      SetClipboardData(clipboardFormat(name), globalCopy(b));
    }
    return true;
  } finally {
    CloseClipboard();
  }
}

module.exports = {
  VK, sleep, isDown, releaseModifiers, ctrlChord, tap, isTerminal, className, forceForeground, hwndOf,
  hasClipboardFormat, clipboardDword, processNameOf, writeClipboardRaw,
  clipboardSeq: () => GetClipboardSequenceNumber(),
  clipboardOwner: () => GetClipboardOwner(),
  addClipboardListener: hwnd => AddClipboardFormatListener(hwnd),
  removeClipboardListener: hwnd => RemoveClipboardFormatListener(hwnd),
  foreground: () => GetForegroundWindow(),
  isWindow: hwnd => !!hwnd && IsWindow(hwnd)
};
