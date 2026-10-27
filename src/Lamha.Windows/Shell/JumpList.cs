using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.System.Variant;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;
using Windows.Win32.UI.Shell.PropertiesSystem;

namespace Lamha.Windows.Shell;

/// <summary>A task in the jump list: its name, what Lamha is started with, and its icon (an .ico file).</summary>
public sealed record JumpTask(string Title, string Arguments, string IconPath);

/// <summary>
/// The jump list: what a right-click on Lamha's taskbar button (or its Start menu entry) offers under Tasks. Windows
/// keeps and draws it, so it costs Lamha nothing while it runs; each task starts Lamha with its arguments, which the
/// running Lamha receives as a second start. It's kept under the process's AppUserModelID: an installed Lamha's is
/// Velopack's (velopack.Lamha, the same as its shortcuts').
/// </summary>
public static unsafe class JumpList
{
    /// <summary>Replaces the tasks (in the interface's language), on a thread of its own. False when Windows said no.</summary>
    /// <param name="exe">Lamha's own path.</param>
    /// <param name="tasks">The tasks, in order.</param>
    public static Task<bool> SetAsync(string exe, IReadOnlyList<JumpTask> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        return Task.Run(() =>
        {
            PInvoke.CoInitializeEx(COINIT.COINIT_MULTITHREADED);
            try
            {
                Set(exe, tasks);
                return true;
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                return false;
            }
            finally
            {
                PInvoke.CoUninitialize();
            }
        });
    }

    private static void Set(string exe, IReadOnlyList<JumpTask> tasks)
    {
        ICustomDestinationList* list = null;
        IObjectArray* removed = null;
        IObjectCollection* collection = null;
        try
        {
            DestinationList.CreateInstance(out list).ThrowOnFailure();
            list->BeginList(out _, out removed); // a task can't be removed by the user, so nothing to read here
            EnumerableObjectCollection.CreateInstance(out collection).ThrowOnFailure();
            foreach (var task in tasks)
            {
                var link = Link(exe, task);
                try
                {
                    collection->AddObject((IUnknown*)link);
                }
                finally
                {
                    link->Release();
                }
            }

            list->AddUserTasks((IObjectArray*)collection);
            list->CommitList();
        }
        catch
        {
            if (list is not null)
            {
                list->AbortList();
            }

            throw;
        }
        finally
        {
            if (collection is not null)
            {
                collection->Release();
            }

            if (removed is not null)
            {
                removed->Release();
            }

            if (list is not null)
            {
                list->Release();
            }
        }
    }

    /// <summary>A task is a shell link to Lamha with its arguments; its name is the link's title.</summary>
    private static IShellLinkW* Link(string exe, JumpTask task)
    {
        ShellLink.CreateInstance(out IShellLinkW* link).ThrowOnFailure();
        IPropertyStore* store = null;
        try
        {
            link->SetPath(exe);
            link->SetArguments(task.Arguments);
            link->SetIconLocation(task.IconPath, 0);
            link->QueryInterface(out store).ThrowOnFailure();
            fixed (char* title = task.Title)
            {
                var value = default(PROPVARIANT);
                value.Anonymous.Anonymous.vt = VARENUM.VT_LPWSTR;
                value.Anonymous.Anonymous.Anonymous.pwszVal = new PWSTR(title); // copied by SetValue
                store->SetValue(PInvoke.PKEY_Title, value);
            }

            store->Commit();
            return link;
        }
        catch
        {
            link->Release();
            throw;
        }
        finally
        {
            if (store is not null)
            {
                store->Release();
            }
        }
    }
}
