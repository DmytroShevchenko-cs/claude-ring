namespace ClaudeRing.Core;

// One item on a menu page: a final action or a submenu.
public sealed record MenuItem(string Label, MenuAction? Action = null, MenuPage? Submenu = null);

// Items of a page in slot order, without "back"; RingMenu adds it.
public sealed record MenuPage(IReadOnlyList<MenuItem> Items);
