# MakoUI — desktop UIs (Dear ImGui)

Immediate-mode GUI for tools and apps. Widgets are drawn every frame; the
functions return the new value / whether they were activated.

```mako
using MakoUI;

main() {
    MakoUI.init("My App", 900, 600);
    MakoUI.theme_mako();                 # cherry blossom theme

    count = 0;
    while MakoUI.running() {
        MakoUI.begin();

        MakoUI.begin_window("Controls");
        MakoUI.text("Count: {count}");
        if MakoUI.button("Increment") { count = count + 1; }
        MakoUI.end_window();

        MakoUI.end();
    }
}
```

## Lifecycle & windows

`init(title, w, h)` · `running()` · `begin()` / `end()` ·
`begin_window(title)` / `end_window()` · `set_window_size(w, h)` ·
`set_window_pos(x, y)` · `begin_window_menu(title)` (window with a menu bar)

## Widgets

Value widgets **return the updated value** — assign it back:

```mako
name    = MakoUI.input_text("Name", name);
volume  = MakoUI.slider("Volume", volume, 0, 1);
enabled = MakoUI.checkbox("Enabled", enabled);
choice  = MakoUI.combo("Fruit", choice, ["Apple", "Banana", "Cherry"]);
```

| Widget | Notes |
|---|---|
| `text(s)` / `text_colored(s, r, g, b)` | Labels |
| `button(label)` / `small_button(label)` | `true` when clicked |
| `checkbox(label, value)` | Returns new bool |
| `slider(label, value, min, max)` / `slider_int(...)` | Returns new value |
| `drag(label, value, speed)` / `drag_int(...)` | Drag number fields |
| `input_text(label, value)` / `input_text_multi(label, value, lines)` | Text fields |
| `input_number(label, value)` | Numeric field |
| `combo(label, index, items_list)` | Dropdown, returns new index |
| `progress(fraction)` | Progress bar 0–1 |
| `collapsing(label)` | Collapsible header — `true` while open |

## Layout

`separator()` · `same_line()` · `spacing()` · `new_line()`

## Menus

```mako
if MakoUI.begin_main_menu_bar() {
    if MakoUI.begin_menu("File") {
        if MakoUI.menu_item("Quit", "Ctrl+Q") { ... }
        MakoUI.end_menu();
    }
    MakoUI.end_main_menu_bar();
}
```

Per-window menu bars: `begin_menu_bar()` / `end_menu_bar()` inside a window
created with `begin_window_menu`.

## Popups & modals

```mako
if MakoUI.button("Delete...") { MakoUI.open_popup("confirm"); }
if MakoUI.begin_modal("confirm") {
    MakoUI.text("Are you sure?");
    if MakoUI.button("Yes") { MakoUI.close_popup(); }
    MakoUI.end_popup();
}
```

## Tables

```mako
if MakoUI.begin_table("data", 3) {
    MakoUI.table_column("Name");
    MakoUI.table_column("Score");
    MakoUI.table_column("Rank");
    MakoUI.table_header_row();
    for row in rows {
        MakoUI.table_next_row();
        MakoUI.table_next_col(); MakoUI.text(row["name"]);
        MakoUI.table_next_col(); MakoUI.text("{row[\"score\"]}");
        MakoUI.table_next_col(); MakoUI.text("{row[\"rank\"]}");
    }
    MakoUI.end_table();
}
```

## Tooltips, queries, themes

- `tooltip(text)` — attach to the previous widget (shows on hover);
  `set_tooltip(text)` — unconditional
- `is_hovered()` / `is_clicked()` — state of the previous widget
- `is_key_pressed(key)` · `get_time()` · `framerate()`
- Themes: `theme_dark()` · `theme_light()` · `theme_mako()` (cherry blossom)
- Fine styling: `push_color(idx, r, g, b, a)` / `pop_color(n)` ·
  `push_var(idx, value)` / `pop_var(n)`

See `examples/ui_demo.mko` for all of it in one app.
