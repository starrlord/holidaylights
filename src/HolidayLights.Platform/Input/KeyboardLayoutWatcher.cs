using HolidayLights.Platform.Native;
using Microsoft.Win32;

namespace HolidayLights.Platform.Input;

/// <summary>
/// Notices when the user adds or removes keyboard layouts: <c>RegNotifyChangeKeyValue</c> on the per-user input method
/// keys (<c>HKCU\Keyboard Layout\Preload</c>, <c>HKCU\Control Panel\International\User Profile</c>), waited on by the
/// thread pool. The callback runs on a thread-pool thread and only says "maybe changed"; the owner compares the layouts.
/// </summary>
internal sealed class KeyboardLayoutWatcher : IDisposable
{
    private const string LogSource = "Platform.HotKeys";
    private static readonly string[] WatchedKeys = [@"Keyboard Layout\Preload", @"Control Panel\International\User Profile"];

    private readonly List<KeyWatch> watches = [];

    /// <summary>Starts watching (keys that do not exist are skipped).</summary>
    /// <param name="maybeChanged">Called on a thread-pool thread after a change.</param>
    /// <param name="log">The log.</param>
    public KeyboardLayoutWatcher(Action maybeChanged, IAppLog log)
    {
        foreach (string path in WatchedKeys)
        {
            RegistryKey? key = Registry.CurrentUser.OpenSubKey(path);
            if (key is null)
            {
                continue;
            }

            var watch = new KeyWatch(key, maybeChanged);
            if (watch.Arm())
            {
                watches.Add(watch);
            }
            else
            {
                log.Warn(LogSource, $"Cannot watch HKCU\\{path} for keyboard layout changes.");
                watch.Dispose();
            }
        }
    }

    /// <summary>Stops watching.</summary>
    public void Dispose()
    {
        foreach (KeyWatch watch in watches)
        {
            watch.Dispose();
        }

        watches.Clear();
    }

    private sealed class KeyWatch : IDisposable
    {
        private const uint Filter = NativeMethods.REG_NOTIFY_CHANGE_NAME | NativeMethods.REG_NOTIFY_CHANGE_LAST_SET | NativeMethods.REG_NOTIFY_THREAD_AGNOSTIC;

        private readonly RegistryKey key;
        private readonly AutoResetEvent signal = new(false);
        private readonly RegisteredWaitHandle wait;
        private readonly Action maybeChanged;
        private volatile bool disposed;

        public KeyWatch(RegistryKey key, Action maybeChanged)
        {
            this.key = key;
            this.maybeChanged = maybeChanged;
            wait = ThreadPool.RegisterWaitForSingleObject(signal, OnSignaled, null, Timeout.Infinite, executeOnlyOnce: false);
        }

        /// <summary>Requests the next notification (they are one-shot).</summary>
        public bool Arm() =>
            !disposed && NativeMethods.RegNotifyChangeKeyValue(key.Handle, watchSubtree: true, Filter, signal.SafeWaitHandle, asynchronous: true) == 0;

        public void Dispose()
        {
            disposed = true;

            // Wait for a running callback so it never touches the key after it is closed.
            using (var unregistered = new ManualResetEvent(false))
            {
                if (wait.Unregister(unregistered))
                {
                    unregistered.WaitOne(TimeSpan.FromSeconds(2));
                }
            }

            key.Dispose();
            signal.Dispose();
        }

        private void OnSignaled(object? state, bool timedOut)
        {
            if (disposed || !Arm())
            {
                return;
            }

            maybeChanged();
        }
    }
}
