namespace Loupedeck.ClaudeRingPlugin
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using ClaudeRing.Core;

    public class ClaudeRingPlugin : Plugin
    {
        public override Boolean UsesApplicationApiOnly => true;

        public override Boolean HasNoApplication => true;

        internal static RingMenu Menu { get; private set; }

        internal static RingConfig Config { get; private set; }

        internal static event Action SlotsChanged;

        private static Launcher _launcher;

        public ClaudeRingPlugin()
        {
            PluginLog.Init(this.Log);
            PluginResources.Init(this.Assembly);
        }

        public override void Load()
        {
            try
            {
                try
                {
                    Config = RingConfig.Load(RingConfig.DefaultPath);
                }
                catch (Exception ex) when (ex is JsonException || ex is IOException || ex is UnauthorizedAccessException)
                {
                    PluginLog.Error(ex, "Конфиг не прочитан, беру значения по умолчанию");
                    Config = new RingConfig();
                }

                var runner = new ProcessRunner();
                _launcher = new Launcher(runner, Config, Thread.Sleep);
                Menu = new RingMenu(ChatSource.CreateDefault(Config, runner), new Links(Config), Config);
                Refresh();

                var labels = Menu.Render().Select(s => s.Label).Where(l => !String.IsNullOrEmpty(l)).ToList();
                PluginLog.Info($"Открытых чатов: {labels.Count}: {String.Join(", ", labels)}");

                RingWatcher.Start(Config.ResetOnRingEvent, onClosedByUser: Refresh);
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Плагин не загрузился");
            }
        }

        public override void Unload()
        {
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
                Menu.Reset();
                if (Menu.LastLoadError != null)
                {
                    PluginLog.Error(Menu.LastLoadError, "Не удалось прочитать открытые чаты");
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
                    case MenuAction.OpenRider a: _launcher.OpenInRider(a.Repos); break;
                    case MenuAction.OpenUrl a: _launcher.OpenUrl(a.Url); break;
                    case MenuAction.OpenUrls a: _launcher.OpenUrls(a.Urls); break;
                    case MenuAction.Reveal a: _launcher.Reveal(a.Paths); break;
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"Действие не выполнилось: {action}");
            }
        }
    }
}
