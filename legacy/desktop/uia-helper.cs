// Lamha desktop — the helper that reads the sentence around a selection in another app (uia-context.js starts it),
// so a lookup there can pick the meaning that fits, as the browser extension does with the page's text. Windows UI
// Automation (what screen readers use) answers. Built by scripts/build-helper.mjs with Windows' own C# compiler
// (.NET Framework 4.8, on every Windows 10 and 11). It replaced a PowerShell script doing the same: about 4 MB in
// Task Manager instead of ~30, and it starts in a fraction of a second instead of about one.
// Input: one JSON line per question { id, hwnd, text }. Output: one JSON line per answer { id, before?, after?, error? }.
// Privacy: it reads only ~400 characters on each side, only when the app's selection is the text Lamha just copied,
// and logs nothing; an error is answered with its type only (a message could quote the app's text).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Automation;
using System.Windows.Automation.Text;

static class LamhaUia {
  const int Chars = 400;
  static readonly Condition TextCond = new AndCondition(
    new PropertyCondition(AutomationElement.IsTextPatternAvailableProperty, true),
    new OrCondition(
      new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document),
      new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)));

  static string Norm(string s) { return Regex.Replace(s ?? "", @"\s+", " ").Trim(); }

  // The range of el's selection whose text is want, or null.
  static TextPatternRange SelectionIn(AutomationElement el, string want) {
    object p;
    if (!el.TryGetCurrentPattern(TextPattern.Pattern, out p)) return null;
    foreach (TextPatternRange r in ((TextPattern)p).GetSelection()) if (Norm(r.GetText(2000)) == want) return r;
    return null;
  }

  static string[] Around(long hwnd, string want, int n) {
    AutomationElement win = AutomationElement.FromHandle(new IntPtr(hwnd));
    int owner = win.Current.ProcessId;
    AutomationElement f = AutomationElement.FocusedElement;
    if (f == null || f.Current.ProcessId != owner) return null; // focus moved to another program: read nothing
    // 1. the focused element or one of its ancestors (Word, Notepad, most edit boxes)
    TextPatternRange r = null;
    AutomationElement e = f;
    for (int i = 0; i < 15 && e != null && r == null; i++) {
      r = SelectionIn(e, want);
      if (r == null) {
        e = TreeWalker.ControlViewWalker.GetParent(e);
        if (e != null && e.Current.ProcessId != owner) e = null;
      }
    }
    // 2. a document below the focused pane or in the window (Chromium apps: VS Code, Teams, Slack, browsers)
    if (r == null) {
      Stopwatch clock = Stopwatch.StartNew();
      foreach (AutomationElement root in new[] { f, win }) {
        foreach (AutomationElement d in root.FindAll(TreeScope.Descendants, TextCond)) {
          r = SelectionIn(d, want);
          if (r != null || clock.ElapsedMilliseconds > 500) break;
        }
        if (r != null || clock.ElapsedMilliseconds > 500) break;
      }
    }
    if (r == null) return null;
    TextPatternRange b = r.Clone();
    b.MoveEndpointByRange(TextPatternRangeEndpoint.End, r, TextPatternRangeEndpoint.Start);
    b.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -n);
    TextPatternRange a = r.Clone();
    a.MoveEndpointByRange(TextPatternRangeEndpoint.Start, r, TextPatternRangeEndpoint.End);
    a.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, n);
    return new[] { b.GetText(n + 16), a.GetText(n + 16) };
  }

  static void Main() {
    UTF8Encoding utf8 = new UTF8Encoding(false);
    TextReader input = new StreamReader(Console.OpenStandardInput(), utf8);
    TextWriter output = new StreamWriter(Console.OpenStandardOutput(), utf8);
    JavaScriptSerializer json = new JavaScriptSerializer();
    string line;
    while ((line = input.ReadLine()) != null) {
      Dictionary<string, object> answer = new Dictionary<string, object>();
      answer["id"] = 0;
      try {
        Dictionary<string, object> q = json.Deserialize<Dictionary<string, object>>(line);
        answer["id"] = q.ContainsKey("id") ? q["id"] : 0;
        long hwnd = q.ContainsKey("hwnd") ? Convert.ToInt64(q["hwnd"]) : 0;
        string text = q.ContainsKey("text") ? Norm(Convert.ToString(q["text"])) : "";
        string[] ctx = hwnd != 0 && text != "" ? Around(hwnd, text, Chars) : null;
        if (ctx != null) { answer["before"] = ctx[0]; answer["after"] = ctx[1]; }
      } catch (Exception err) {
        answer["error"] = err.GetType().Name;
      }
      output.WriteLine(json.Serialize(answer));
      output.Flush();
    }
  }
}
