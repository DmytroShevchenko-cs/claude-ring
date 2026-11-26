namespace Loupedeck.ClaudeRingPlugin
{
    using System;
    using System.IO;
    using System.Threading;

    // Watches the config file. Calls onChanged once the file has been quiet for 300 ms,
    // because editors often save in several steps (temp file, rename, write).
    internal sealed class ConfigWatcher : IDisposable
    {
        private const Int32 DebounceMs = 300;

        private readonly FileSystemWatcher _watcher;

        private readonly Timer _timer;

        private ConfigWatcher(String path, Action onChanged)
        {
            this._timer = new Timer(_ => RunSafely(onChanged), null, Timeout.Infinite, Timeout.Infinite);
            this._watcher = new FileSystemWatcher(Path.GetDirectoryName(path), Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            };
            this._watcher.Changed += this.OnChanged;
            this._watcher.Created += this.OnChanged;
            this._watcher.Deleted += this.OnChanged;
            this._watcher.Renamed += this.OnChanged;
            this._watcher.Error += (_, e) => PluginLog.Error(e.GetException(), "Слежение за конфигом прервалось");
            this._watcher.EnableRaisingEvents = true;
        }

        // Null when the config folder does not exist or cannot be watched: then the config is read only at start.
        public static ConfigWatcher Start(String path, Action onChanged)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir))
                {
                    PluginLog.Info($"Папки {dir} нет, изменения конфига не отслеживаются");
                    return null;
                }
                var watcher = new ConfigWatcher(path, onChanged);
                PluginLog.Info($"Слежу за конфигом {path}");
                return watcher;
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Не удалось следить за конфигом");
                return null;
            }
        }

        public void Dispose()
        {
            this._watcher.Dispose();
            this._timer.Dispose();
        }

        // Every event moves the timer, so only the last event of a burst counts.
        private void OnChanged(Object sender, FileSystemEventArgs e)
        {
            try
            {
                this._timer.Change(DebounceMs, Timeout.Infinite);
            }
            catch (ObjectDisposedException)
            {
                // The plugin is unloading.
            }
        }

        private static void RunSafely(Action onChanged)
        {
            try
            {
                onChanged();
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Ошибка при перечитывании конфига");
            }
        }
    }
}
