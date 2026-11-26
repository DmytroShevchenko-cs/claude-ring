namespace Loupedeck.ClaudeRingPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Threading;

    // Listens to ring events inside LogiPluginService.
    // Every event goes to the log. The event named resetEvent means "the ring was closed":
    // if the plugin did not close it on purpose, onClosedByUser runs.
    internal static class RingWatcher
    {
        private static readonly List<(Object Target, EventInfo Event, Delegate Handler)> Subscriptions = new();
        private static Int64 _expectReopenUntilTicks;
        private static String _resetEvent;
        private static Action _onClosedByUser;

        public static void Start(String resetEvent, Action onClosedByUser)
        {
            Stop();
            _resetEvent = resetEvent;
            _onClosedByUser = onClosedByUser;
            try
            {
                var deviceManager = RingReopener.FindDeviceManager();
                var handler = RingReopener.FindRadialMenuHandler(deviceManager);
                Subscribe(handler, "MouseEnter");
                Subscribe(handler, "MouseLeave");
                var device = deviceManager.GetType().GetProperty("RadialMenuDevice")?.GetValue(deviceManager);
                if (device != null) Subscribe(device, "DeviceRedrawRequested");
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Не удалось подписаться на события кольца, автосброса уровня не будет");
            }
        }

        public static void Stop()
        {
            foreach (var (target, evt, handler) in Subscriptions)
            {
                try { evt.RemoveEventHandler(target, handler); }
                catch (Exception ex) { PluginLog.Error(ex, $"Не удалось отписаться от события {evt.Name}"); }
            }
            Subscriptions.Clear();
        }

        // Call before the plugin re-opens the ring: the next close is ours, not the user's.
        public static void ExpectPluginReopen() =>
            Interlocked.Exchange(ref _expectReopenUntilTicks, DateTime.UtcNow.AddSeconds(3).Ticks);

        private static void Subscribe(Object target, String eventName)
        {
            try
            {
                var evt = target.GetType().GetEvent(eventName);
                if (evt == null)
                {
                    PluginLog.Warning($"Событие {eventName} не найдено у {target.GetType().Name}");
                    return;
                }
                var sink = new Sink(eventName);
                var handler = Delegate.CreateDelegate(evt.EventHandlerType, sink, typeof(Sink).GetMethod(nameof(Sink.Handle)));
                evt.AddEventHandler(target, handler);
                Subscriptions.Add((target, evt, handler));
                PluginLog.Info($"Подписка на событие кольца: {eventName}");
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"Не удалось подписаться на событие {eventName}");
            }
        }

        private static void OnEvent(String name)
        {
            try
            {
                if (name == "DeviceRedrawRequested") PluginLog.Verbose($"Событие кольца: {name}");
                else PluginLog.Info($"Событие кольца: {name}");
                if (String.IsNullOrEmpty(_resetEvent) || name != _resetEvent) return;
                if (Interlocked.Exchange(ref _expectReopenUntilTicks, 0) > DateTime.UtcNow.Ticks) return;
                _onClosedByUser?.Invoke();
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"Ошибка в обработчике события кольца {name}");
            }
        }

        // Binds to any EventHandler<T> because T derives from EventArgs.
        private sealed class Sink
        {
            private readonly String _name;
            public Sink(String name) => _name = name;
            public void Handle(Object sender, EventArgs e) => OnEvent(_name);
        }
    }
}
