# Spike results (Task 1), 2026-11-26

Setup: MX Master 4, Logi Options+ 2.9.1013853, LogiPluginService 6.4.2.3414, macOS.
Folder "Claude" in the general Actions Ring profile with 4 probe actions + 4 other actions.

| Question | Result |
|---|---|
| Does `RadialMenuMessageHandler.OnShowDevice(true)` (via reflection) re-open the ring? | Yes. 9 probe clicks, no errors in the log. |
| Delay | 0 ms and 150 ms feel the same, 600 ms is clearly slower. Chosen: `reopenDelayMs = 150`. |
| Where does the ring re-open? | At the mouse cursor. |
| Folder capacity | 8 visible, the editor allows a 9th. Chosen: `slotCount = 9`. |
| Close event | None. `MouseEnter` / `MouseLeave` never fired (open, click outside, hover, normal close). `DeviceRedrawRequested` fired only 3 times in pairs, while the folder was being edited in Options+, not on ring open/close. Chosen: `resetOnRingEvent = ""` (no auto reset: level stays until an action runs or "back" is used). |

Follow-up for Task 7: log `GetCommandDisplayName` calls to see if the service asks for labels on every ring open. If yes, it can tell "user opened the ring" from "plugin re-opened it".
