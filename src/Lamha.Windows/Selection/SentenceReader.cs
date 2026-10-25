using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using global::Windows.Win32;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.System.Com;
using global::Windows.Win32.System.Variant;
using global::Windows.Win32.UI.Accessibility;
using Lamha.Core.Dictionary;

namespace Lamha.Windows.Selection;

/// <summary>
/// Reads the sentence around a word selected in another program, so a lookup can pick the meaning that fits ("Understand
/// words from their sentence"), as the browser extension did with the page's text. Windows UI Automation (what screen
/// readers use) answers. Lamha 2 ran this in a helper process (desktop/uia-helper.cs) so a hung program couldn't block
/// it; here it runs on its own thread with UI Automation's own timeouts, and a lookup waits for it at most a moment.
/// </summary>
/// <remarks>
/// Privacy: it reads only ~400 characters on each side, only on the lookup shortcut, only when the program's selection
/// is the text Lamha just copied, and only while focus is still in that program. Nothing is logged.
/// </remarks>
public sealed partial class SentenceReader : IDisposable
{
    /// <summary>Characters read on each side of the word.</summary>
    public const int Chars = 400;

    /// <summary>A question this old means the worker is stuck in a frozen program: the next one gets a new worker.</summary>
    private static readonly TimeSpan Stuck = TimeSpan.FromSeconds(3);

    private readonly Lock _gate = new();
    private Worker? _worker;

    /// <summary>
    /// The text before and after <paramref name="text"/>, selected in <paramref name="window"/>; null when the program
    /// has no accessible text, its selection isn't that text, it's too slow (<paramref name="timeout"/>), or focus moved.
    /// </summary>
    public async Task<WordContext?> AroundAsync(nint window, string text, TimeSpan timeout)
    {
        var want = Norm(text);
        if (window == 0 || want.Length == 0)
        {
            return null;
        }

        var question = new Question(window, want);
        lock (_gate)
        {
            if (_worker is null || _worker.IsStuck)
            {
                _worker?.Abandon();
                _worker = new Worker();
            }

            _worker.Ask(question);
        }

        var done = await Task.WhenAny(question.Answer.Task, Task.Delay(timeout)).ConfigureAwait(false);
        return done == question.Answer.Task ? await question.Answer.Task.ConfigureAwait(false) : null;
    }

    /// <summary>Starts the worker ahead of the first lookup, so it doesn't pay UI Automation's start-up.</summary>
    public void Warm()
    {
        lock (_gate)
        {
            _worker ??= new Worker();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _worker?.Abandon();
            _worker = null;
        }
    }

    /// <summary>Whitespace collapsed and trimmed, as both sides compare it.</summary>
    internal static string Norm(string? s) => Spaces().Replace(s ?? "", " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    private sealed record Question(nint Window, string Want)
    {
        public TaskCompletionSource<WordContext?> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>One thread in the COM multithreaded apartment (as UI Automation clients should be), answering in order.</summary>
    private sealed unsafe class Worker
    {
        private readonly BlockingCollection<Question> _questions = [];
        private long _busySince;

        public Worker()
        {
            var thread = new Thread(Run) { IsBackground = true, Name = "Lamha UI Automation" };
            thread.Start();
        }

        public bool IsStuck => Interlocked.Read(ref _busySince) is var since and > 0 && Environment.TickCount64 - since > Stuck.TotalMilliseconds;

        public void Ask(Question question) => _questions.Add(question);

        /// <summary>No more questions: the thread ends once its current one returns.</summary>
        public void Abandon() => _questions.CompleteAdding();

        private void Run()
        {
            PInvoke.CoInitializeEx(COINIT.COINIT_MULTITHREADED);
            IUIAutomation2* uia = null;
            try
            {
                CUIAutomation8.CreateInstance(out uia).ThrowOnFailure();

                // A program that doesn't answer is given up on after a second (UI Automation's default is 20).
                uia->ConnectionTimeout = 1000;
                uia->TransactionTimeout = 1000;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                uia = null;
            }

            try
            {
                foreach (var q in _questions.GetConsumingEnumerable())
                {
                    Interlocked.Exchange(ref _busySince, Environment.TickCount64);
                    try
                    {
                        q.Answer.TrySetResult(uia is null ? null : Around(uia, q.Window, q.Want));
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        q.Answer.TrySetResult(null); // the program's text can't be read: the lookup goes on without it
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _busySince, 0);
                    }
                }
            }
            finally
            {
                if (uia is not null)
                {
                    uia->Release();
                }

                _questions.Dispose();
                PInvoke.CoUninitialize();
            }
        }

        /// <summary>Lamha 2's uia-helper.cs <c>Around()</c>: the selection's range, then the text on each side of it.</summary>
        private static WordContext? Around(IUIAutomation2* uia, nint hwnd, string want)
        {
            IUIAutomationElement* window = null, focused = null;
            IUIAutomationTextRange* range = null;
            try
            {
                window = uia->ElementFromHandle((HWND)hwnd);
                var owner = window->CurrentProcessId;
                focused = uia->GetFocusedElement();
                if (focused == null || focused->CurrentProcessId != owner)
                {
                    return null; // focus moved to another program: read nothing
                }

                // 1. The focused element or one of its ancestors (Word, Notepad, most edit boxes).
                range = InAncestors(uia, focused, owner, want);

                // 2. A document below the focused pane or in the window (Chromium apps: VS Code, Teams, browsers).
                if (range == null)
                {
                    range = InDescendants(uia, focused, window, want);
                }

                if (range == null)
                {
                    return null;
                }

                var before = Side(range, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start);
                var after = Side(range, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End);
                return new WordContext(before, after);
            }
            finally
            {
                Release(range);
                Release(focused);
                Release(window);
            }
        }

        private static IUIAutomationTextRange* InAncestors(IUIAutomation2* uia, IUIAutomationElement* focused, int owner, string want)
        {
            var walker = uia->ControlViewWalker;
            var element = focused;
            focused->AddRef();
            IUIAutomationTextRange* range = null;
            try
            {
                for (var i = 0; i < 15 && element != null && range == null; i++)
                {
                    range = SelectionIn(element, want);
                    if (range == null)
                    {
                        var parent = walker->GetParentElement(element);
                        Release(element);
                        element = parent;
                        if (element != null && element->CurrentProcessId != owner)
                        {
                            Release(element);
                            element = null;
                        }
                    }
                }

                return range;
            }
            finally
            {
                Release(element);
                walker->Release();
            }
        }

        private static IUIAutomationTextRange* InDescendants(IUIAutomation2* uia, IUIAutomationElement* focused, IUIAutomationElement* window, string want)
        {
            VARIANT yes = default;
            yes.vt = VARENUM.VT_BOOL;
            yes.boolVal = VARIANT_BOOL.VARIANT_TRUE;
            VARIANT document = default;
            document.vt = VARENUM.VT_I4;
            document.lVal = (int)UIA_CONTROLTYPE_ID.UIA_DocumentControlTypeId;
            VARIANT edit = default;
            edit.vt = VARENUM.VT_I4;
            edit.lVal = (int)UIA_CONTROLTYPE_ID.UIA_EditControlTypeId;

            IUIAutomationCondition* hasText = null, isDocument = null, isEdit = null, kind = null, condition = null;
            try
            {
                hasText = uia->CreatePropertyCondition(UIA_PROPERTY_ID.UIA_IsTextPatternAvailablePropertyId, yes);
                isDocument = uia->CreatePropertyCondition(UIA_PROPERTY_ID.UIA_ControlTypePropertyId, document);
                isEdit = uia->CreatePropertyCondition(UIA_PROPERTY_ID.UIA_ControlTypePropertyId, edit);
                kind = uia->CreateOrCondition(isDocument, isEdit);
                condition = uia->CreateAndCondition(hasText, kind);

                var clock = Stopwatch.StartNew();
                foreach (var root in new[] { (nint)focused, (nint)window })
                {
                    var found = ((IUIAutomationElement*)root)->FindAll(TreeScope.TreeScope_Descendants, condition);
                    if (found == null)
                    {
                        continue;
                    }

                    try
                    {
                        for (var i = 0; i < found->Length && clock.ElapsedMilliseconds <= 500; i++)
                        {
                            var element = found->GetElement(i);
                            var range = SelectionIn(element, want);
                            Release(element);
                            if (range != null)
                            {
                                return range;
                            }
                        }
                    }
                    finally
                    {
                        found->Release();
                    }

                    if (clock.ElapsedMilliseconds > 500)
                    {
                        break;
                    }
                }

                return null;
            }
            finally
            {
                Release(condition);
                Release(kind);
                Release(isEdit);
                Release(isDocument);
                Release(hasText);
            }
        }

        /// <summary>The range of <paramref name="element"/>'s selection whose text is <paramref name="want"/>, or null.</summary>
        private static IUIAutomationTextRange* SelectionIn(IUIAutomationElement* element, string want)
        {
            var iid = typeof(IUIAutomationTextPattern).GUID;
            var pattern = (IUIAutomationTextPattern*)element->GetCurrentPatternAs(UIA_PATTERN_ID.UIA_TextPatternId, &iid);
            if (pattern == null)
            {
                return null;
            }

            IUIAutomationTextRangeArray* selection = null;
            try
            {
                selection = pattern->GetSelection();
                if (selection == null)
                {
                    return null;
                }

                for (var i = 0; i < selection->Length; i++)
                {
                    var range = selection->GetElement(i);
                    if (Norm(Text(range, 2000)) == want)
                    {
                        return range;
                    }

                    Release(range);
                }

                return null;
            }
            finally
            {
                Release(selection);
                pattern->Release();
            }
        }

        /// <summary>
        /// The text before (<paramref name="side"/> Start) or after (End) the range: a copy of it collapsed to that end,
        /// then stretched 400 characters outward.
        /// </summary>
        private static string Side(IUIAutomationTextRange* range, TextPatternRangeEndpoint side)
        {
            var piece = range->Clone();
            try
            {
                if (side == TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start)
                {
                    piece->MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, range, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start);
                    piece->MoveEndpointByUnit(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, TextUnit.TextUnit_Character, -Chars);
                }
                else
                {
                    piece->MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, range, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End);
                    piece->MoveEndpointByUnit(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, TextUnit.TextUnit_Character, Chars);
                }

                return Text(piece, Chars + 16);
            }
            finally
            {
                piece->Release();
            }
        }

        private static string Text(IUIAutomationTextRange* range, int max)
        {
            var text = range->GetText(max);
            try
            {
                return text.ToString() ?? "";
            }
            finally
            {
                PInvoke.SysFreeString(text);
            }
        }

        private static void Release<T>(T* p)
            where T : unmanaged
        {
            if (p != null)
            {
                ((IUnknown*)p)->Release();
            }
        }
    }
}
