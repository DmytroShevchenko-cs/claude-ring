namespace Loupedeck.ClaudeRingPlugin
{
    using System;
    using System.Linq;
    using System.Reflection;
    using System.Threading.Tasks;

    // Opens the Actions Ring from inside LogiPluginService.
    // The service keeps its main object in a static field of ServiceTelemetrySender.
    // Field names are obfuscated, so we search by type.
    internal static class RingReopener
    {
        private static object _handler;

        public static void ShowAfter(Int32 delayMs) =>
            Task.Run(async () =>
            {
                await Task.Delay(delayMs);
                if (!TryShow(out var error))
                {
                    PluginLog.Error($"Кольцо не открылось: {error}");
                }
            });

        public static Boolean TryShow(out String error)
        {
            error = null;
            try
            {
                var handler = _handler ??= FindRadialMenuHandler(FindDeviceManager());
                var show = handler.GetType().GetMethod("OnShowDevice", new[] { typeof(Boolean) })
                    ?? throw new MissingMemberException(handler.GetType().Name, "OnShowDevice");
                show.Invoke(handler, new Object[] { true });
                return true;
            }
            catch (Exception ex)
            {
                _handler = null;
                error = ex.GetBaseException().Message;
                return false;
            }
        }

        internal static Object FindRadialMenuHandler(Object deviceManager) =>
            (deviceManager.GetType().GetProperty("RadialMenuMessageHandler")
                ?? throw new MissingMemberException(deviceManager.GetType().Name, "RadialMenuMessageHandler"))
            .GetValue(deviceManager);

        internal static Object FindDeviceManager()
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "LoupedeckService")
                ?? throw new MissingMemberException("AppDomain", "LoupedeckService assembly");
            var telemetry = assembly.GetType("Loupedeck.Service.ServiceTelemetrySender", throwOnError: true);
            var serviceField = telemetry
                .GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                .FirstOrDefault(f => f.FieldType.FullName == "Loupedeck.LoupedeckService")
                ?? throw new MissingMemberException("ServiceTelemetrySender", "static LoupedeckService field");
            var service = serviceField.GetValue(null)
                ?? throw new InvalidOperationException("LoupedeckService is not created yet");
            return (service.GetType().GetProperty("Loupedeck7DeviceManager")
                ?? throw new MissingMemberException(service.GetType().Name, "Loupedeck7DeviceManager"))
                .GetValue(service);
        }
    }
}
