namespace Loupedeck.ClaudeRingPlugin
{
    using System;
    using System.Linq;
    using System.Threading;
    using ClaudeRing.Core;

    public class ClaudeRingPlugin : Plugin
    {
        public override Boolean UsesApplicationApiOnly => true;

        public override Boolean HasNoApplication => true;

        internal static RingMenu Menu { get; private set; }

        internal static Settings CurrentSettings { get; private set; }

        internal static event Action SlotsChanged;

        // Serializes config reloads with each other and with Unload.
        private static readonly Object ReloadLock = new Object();

        private static ProcessRunner _runner;

        private static Launcher _launcher;

        private static ConfigWatcher _configWatcher;

        public ClaudeRingPlugin()
        {
            PluginLog.Init(this.Log);
            PluginResources.Init(this.Assembly);
        }

        public override void Load()
        {
            try
            {
                _runner = new ProcessRunner();
                _launcher = new Launcher(_runner, Thread.Sleep);
                var settings = ReadSettings();
                if (settings == null)
                {
                    PluginLog.Warning("Конфиг с ошибками, беру значения по умолчанию");
                    settings = new Settings();
                }
                CurrentSettings = settings;
                Menu = CreateMenu(settings);
                Refresh();

                var labels = Menu.Render().Select(s => s.Label).Where(l => !String.IsNullOrEmpty(l)).ToList();
                PluginLog.Info($"Открытых чатов: {labels.Count}: {String.Join(", ", labels)}");

                RingWatcher.Start(settings.ResetOnRingEvent, onClosedByUser: Refresh);
                lock (ReloadLock)
                {
                    _configWatcher = ConfigWatcher.Start(Settings.DefaultPath, ReloadSettings);
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Плагин не загрузился");
            }
        }

        public override void Unload()
        {
            lock (ReloadLock)
            {
                try { _configWatcher?.Dispose(); }
                catch (Exception ex) { PluginLog.Error(ex, "Не удалось остановить слежение за конфигом"); }
                _configWatcher = null;
            }

            try { RingWatcher.Stop(); }
            catch (Exception ex) { PluginLog.Error(ex, "Не удалось отписаться от событий кольца"); }

            // Statics may survive a plugin reload, so drop old references.
            SlotsChanged = null;
            Menu = null;
        }

        // Reads open chats again and shows the root level.
        internal static void Refresh()
        {
            try
            {
                var menu = Menu;
                if (menu == null)
                {
                    return;
                }
                menu.Reset();
                if (menu.LastLoadError != null)
                {
                    PluginLog.Error(menu.LastLoadError, "Не удалось прочитать открытые чаты");
                }
                SlotsChanged?.Invoke();
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Не удалось обновить слоты");
            }
        }

        internal static void Execute(MenuAction action)
        {
            try
            {
                switch (action)
                {
                    case MenuAction.FocusChat a: _launcher.FocusChat(a.Tab); break;
                    case MenuAction.OpenIde a: _launcher.OpenIde(a.Ide, a.Repos); break;
                    case MenuAction.OpenUrl a: _launcher.OpenUrl(a.Url); break;
                    case MenuAction.OpenUrls a: _launcher.OpenUrls(a.Urls); break;
                    case MenuAction.Reveal a: _launcher.Reveal(a.Paths); break;
                    case MenuAction.Run a: _launcher.Run(a.File, a.Args); break;
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"Действие не выполнилось: {Describe(action)}");
            }
        }

        // Records print lists as type names, so show programs and paths in full.
        private static String Describe(MenuAction action) => action switch
        {
            MenuAction.Run a => $"{a.File} {String.Join(" ", a.Args)}",
            MenuAction.OpenIde a => $"{IdeLauncher.DisplayName(a.Ide)}: {String.Join(" ", a.Repos.Select(r => r.Path))}",
            _ => action.ToString(),
        };

        // Called by ConfigWatcher on a timer thread after the config file changes.
        // A config with errors is not applied: the previous settings and menu stay.
        private static void ReloadSettings()
        {
            try
            {
                lock (ReloadLock)
                {
                    if (_configWatcher == null)
                    {
                        return;
                    }

                    var settings = ReadSettings();
                    if (settings == null)
                    {
                        PluginLog.Warning("Новый конфиг с ошибками, оставляю прежние настройки");
                        return;
                    }

                    var previous = CurrentSettings;
                    var menu = CreateMenu(settings);
                    menu.Reset();
                    if (menu.LastLoadError != null)
                    {
                        PluginLog.Error(menu.LastLoadError, "Не удалось прочитать открытые чаты");
                    }
                    CurrentSettings = settings;
                    Menu = menu;
                    if (previous?.ResetOnRingEvent != settings.ResetOnRingEvent)
                    {
                        RingWatcher.Start(settings.ResetOnRingEvent, onClosedByUser: Refresh);
                    }
                    PluginLog.Info("Конфиг перечитан");
                }
                SlotsChanged?.Invoke();
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Не удалось перечитать конфиг");
            }
        }

        // Writes warnings and errors to the log. Null when the config has errors.
        private static Settings ReadSettings()
        {
            var result = SettingsLoader.Load(Settings.DefaultPath);
            foreach (var warning in result.Warnings)
            {
                PluginLog.Warning($"Конфиг: {warning}");
            }
            foreach (var error in result.Errors)
            {
                PluginLog.Error($"Ошибка в конфиге: {error}");
            }
            return result.Settings;
        }

        private static RingMenu CreateMenu(Settings settings)
        {
            var strings = Strings.For(settings.Language, () => SystemLanguage.Read(_runner));
            return new RingMenu(ChatSource.CreateDefault(settings, _runner), new MenuBuilder(settings, strings));
        }
    }
}
