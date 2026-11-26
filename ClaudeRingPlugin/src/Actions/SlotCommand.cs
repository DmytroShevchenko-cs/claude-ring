namespace Loupedeck.ClaudeRingPlugin
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using ClaudeRing.Core;

    // Fixed ring slots. Labels come from RingMenu and change when the level changes.
    public class SlotCommand : PluginDynamicCommand
    {
        private const Int32 MaxSlots = 9;
        private const Int64 NewBatchGapMs = 1000;

        // Font sizes as a share of the image height.
        private const Double TopFontScale = 0.18;
        private const Double TopAreaScale = 0.30;
        private const Double FontShort = 0.42;   // up to 4 chars
        private const Double FontMedium = 0.34;  // up to 6 chars
        private const Double FontLong = 0.24;    // up to 9 chars
        private const Double FontVeryLong = 0.18;

        private static Int64 _lastLabelTick;

        public SlotCommand()
        {
            for (var i = 0; i < MaxSlots; i++)
            {
                this.AddParameter(i.ToString(), $"Claude слот {i + 1}", "Claude Ring");
            }

            ClaudeRingPlugin.SlotsChanged += () =>
            {
                try { this.ActionImageChanged(); }
                catch (Exception ex) { PluginLog.Error(ex, "Не удалось обновить подписи слотов"); }
            };
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
        {
            try
            {
                var now = Environment.TickCount64;
                var previous = Interlocked.Exchange(ref _lastLabelTick, now);
                if (now - previous > NewBatchGapMs)
                {
                    PluginLog.Info("Служба запросила подписи слотов (новая серия)");
                }

                var slots = ClaudeRingPlugin.Menu?.Render();
                if (slots == null || !Int32.TryParse(actionParameter, out var i) || i < 0 || i >= slots.Count)
                {
                    return " ";
                }
                return i < ClaudeRingPlugin.Config.SlotCount ? String.IsNullOrEmpty(slots[i].Label) ? " " : slots[i].Label : " ";
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Не удалось получить подпись слота");
                return " ";
            }
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            try
            {
                var label = this.GetCommandDisplayName(actionParameter, imageSize);
                if (String.IsNullOrWhiteSpace(label))
                {
                    // Transparent image: otherwise the service draws the default action name.
                    using var empty = new BitmapBuilder(imageSize);
                    empty.Clear(BitmapColor.Transparent);
                    return empty.ToImage();
                }

                using var b = new BitmapBuilder(imageSize);
                b.Clear(BitmapColor.Black);
                var h = b.Height;
                var w = b.Width;
                var (top, main) = SlotText.Format(label);

                var areaY = 0;
                var areaH = h;
                if (top != null)
                {
                    var topH = (Int32)(h * TopAreaScale);
                    b.DrawText(top, 0, 0, w, topH, BitmapColor.White, (Int32)(h * TopFontScale));
                    areaY = topH;
                    areaH = h - topH;
                }

                var lines = SplitLines(main);
                var size = (Int32)(h * FontScaleFor(lines.Max(l => l.Length)));
                var lineH = areaH / lines.Length;
                for (var n = 0; n < lines.Length; n++)
                {
                    b.DrawText(lines[n], 0, areaY + n * lineH, w, lineH, BitmapColor.White, size);
                }
                return b.ToImage();
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Не удалось нарисовать слот");
                return null;
            }
        }

        private static Double FontScaleFor(Int32 length) =>
            length <= 4 ? FontShort : length <= 6 ? FontMedium : length <= 9 ? FontLong : FontVeryLong;

        // A long text with spaces is split into two lines at the space closest to the middle.
        private static String[] SplitLines(String text)
        {
            if (text.Length <= 9 || !text.Contains(' '))
            {
                return new[] { text };
            }
            var middle = text.Length / 2;
            var at = -1;
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == ' ' && (at < 0 || Math.Abs(i - middle) < Math.Abs(at - middle)))
                {
                    at = i;
                }
            }
            return new[] { text.Substring(0, at), text.Substring(at + 1) };
        }

        protected override void RunCommand(String actionParameter)
        {
            try
            {
                if (ClaudeRingPlugin.Menu == null || !Int32.TryParse(actionParameter, out var i)
                    || i < 0 || i >= ClaudeRingPlugin.Config.SlotCount)
                {
                    return;
                }

                var result = ClaudeRingPlugin.Menu.Click(i);
                this.ActionImageChanged();
                if (result.Action != null)
                {
                    var action = result.Action;
                    // Run the action first, then reload chats (logs load errors, raises SlotsChanged).
                    Task.Run(() =>
                    {
                        ClaudeRingPlugin.Execute(action);
                        ClaudeRingPlugin.Refresh();
                    });
                }
                if (result.Reopen && ClaudeRingPlugin.Config.ReopenDelayMs >= 0)
                {
                    // The ring closes after this click; do not treat that close as "user closed it".
                    RingWatcher.ExpectPluginReopen();
                    RingReopener.ShowAfter(ClaudeRingPlugin.Config.ReopenDelayMs);
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Клик по слоту не обработан");
            }
        }
    }
}
