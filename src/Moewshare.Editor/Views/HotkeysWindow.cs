using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Moewshare.Editor.Services;

namespace Moewshare.Editor.Views;

/// <summary>
/// Справка по горячим клавишам: сочетания сразу для Windows и macOS,
/// колонка текущей системы подсвечена. Есть поиск по действию.
/// </summary>
public sealed class HotkeysWindow : Window
{
    sealed record Row(string Action, string Win, string Mac);
    sealed record Section(string Title, Row[] Rows);

    static readonly Section[] Sections =
    [
        new("Общие", [
            new("Сохранить сцену", "Ctrl+S", "⌘S"),
            new("Отменить", "Ctrl+Z", "⌘Z"),
            new("Повторить", "Ctrl+Y  ·  Ctrl+Shift+Z", "⇧⌘Z"),
            new("Открыть сцену", "Ctrl+O", "⌘O"),
            new("Настройки движка", "Ctrl+,", "⌘,"),
            new("Переключить светлую / тёмную тему", "Ctrl+Shift+L", "⇧⌘L"),
            new("Эта справка", "F1  ·  Ctrl+/", "F1  ·  ⌘/"),
        ]),
        new("Игра", [
            new("Запустить / остановить", "F5  ·  Ctrl+P", "F5  ·  ⌘P"),
            new("Остановить", "Shift+F5", "⇧F5"),
            new("Собрать игру для Web", "Ctrl+B", "⌘B"),
        ]),
        new("Объекты сцены", [
            new("Добавить объект…", "Ctrl+Shift+A", "⇧⌘A"),
            new("Выделить все объекты", "Ctrl+A", "⌘A"),
            new("Добавить к выделению", "Ctrl / Shift + клик", "⌘ / ⇧ + клик"),
            new("Снять выделение", "Esc", "Esc"),
            new("Копировать / Вырезать / Вставить", "Ctrl+C / Ctrl+X / Ctrl+V", "⌘C / ⌘X / ⌘V"),
            new("Дублировать", "Ctrl+D", "⌘D"),
            new("Удалить", "Delete", "⌫  ·  ⌘⌫"),
            new("Переименовать", "F2", "F2"),
            new("Скрыть / показать", "H", "H"),
            new("Граф логики выбранного объекта", "Ctrl+Shift+E", "⇧⌘E"),
        ]),
        new("Трансформация и сцена 3D", [
            new("Выбор / Перемещение / Вращение / Масштаб", "Q / W / E / R", "Q / W / E / R"),
            new("Фокус на объекте", "F", "F"),
            new("Полёт камеры", "ПКМ + W A S D, Q / E — вниз / вверх", "ПКМ + W A S D, Q / E — вниз / вверх"),
            new("Ускорение полёта", "Shift", "⇧"),
            new("Орбита вокруг сцены", "Alt + ЛКМ  ·  СКМ", "⌥ + ЛКМ  ·  СКМ"),
            new("Панорама", "Shift + СКМ", "⇧ + СКМ"),
            new("Приближение", "Колесо мыши", "Колесо / жест трекпада"),
        ]),
        new("Редактор событий (Event Graph)", [
            new("Выделить рамкой", "ЛКМ по пустому месту и тянуть", "ЛКМ по пустому месту и тянуть"),
            new("Добавить / убрать из выделения", "Shift / Ctrl + клик", "⇧ / ⌘ + клик"),
            new("Выделить все блоки", "Ctrl+A", "⌘A"),
            new("Удалить выделенные", "Delete  ·  Backspace", "⌫"),
            new("Копировать / Вырезать / Вставить", "Ctrl+C / Ctrl+X / Ctrl+V", "⌘C / ⌘X / ⌘V"),
            new("Дублировать выделенные", "Ctrl+D", "⌘D"),
            new("Объединить в группу", "Ctrl+G", "⌘G"),
            new("Расформировать группу", "Ctrl+Shift+G", "⇧⌘G"),
            new("Сдвинуть выделенные", "Стрелки (Shift — крупный шаг)", "Стрелки (⇧ — крупный шаг)"),
            new("Показать все / только выделенные", "F  ·  Shift+F", "F  ·  ⇧F"),
            new("Масштаб 100% / + / −", "Ctrl+0 / Ctrl+= / Ctrl+−", "⌘0 / ⌘= / ⌘−"),
            new("Панорама", "ПКМ / СКМ + тянуть  ·  Пробел + ЛКМ", "ПКМ / СКМ + тянуть  ·  Пробел + ЛКМ"),
            new("Каталог блоков в точке", "Двойной клик по полотну", "Двойной клик по полотну"),
            new("Контекстное меню", "ПКМ (без перетаскивания)", "ПКМ  ·  ⌃ + клик"),
        ]),
        new("Панели", [
            new("Сцена 3D / События / Иерархия / Инспектор", "Ctrl+1 / 2 / 3 / 4", "⌘1 / 2 / 3 / 4"),
            new("Ресурсы / Консоль / Запуск & Web", "Ctrl+5 / 6 / 7", "⌘5 / 6 / 7"),
            new("Свернуть / развернуть нижнюю панель", "Ctrl+J", "⌘J"),
        ]),
    ];

    readonly StackPanel _list = new() { Spacing = 14 };

    public HotkeysWindow()
    {
        Title = "⌨️ Горячие клавиши — Moewshare";
        Width = 760;
        Height = 640;
        MinWidth = 560;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var search = new TextBox { Watermark = "🔍 Найти действие или клавишу…", Margin = new Thickness(0, 0, 0, 12) };
        search.TextChanged += (_, _) => Build(search.Text);

        var title = new TextBlock { Text = "Горячие клавиши", Classes = { "title" } };
        var subtitle = new TextBlock
        {
            Text = Hk.IsMac
                ? "Вы на macOS — основная клавиша команд ⌘ (Command). Колонка macOS подсвечена."
                : "Вы на Windows — основная клавиша команд Ctrl. Колонка Windows подсвечена.",
            Classes = { "caption" },
            Margin = new Thickness(0, 2, 0, 12)
        };

        var close = new Button { Content = "Закрыть", Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        close.Click += (_, _) => Close();

        var header = new StackPanel();
        header.Children.Add(title);
        header.Children.Add(subtitle);
        header.Children.Add(search);

        var root = new DockPanel { Margin = new Thickness(20) };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(close);
        root.Children.Add(new ScrollViewer
        {
            Content = _list,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        });
        Content = root;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        };
        Opened += (_, _) => search.Focus();
        Build(null);
    }

    void Build(string? filter)
    {
        _list.Children.Clear();
        var q = filter?.Trim() ?? "";
        foreach (var section in Sections)
        {
            var rows = section.Rows.Where(r => q.Length == 0 ||
                r.Action.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.Win.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.Mac.Contains(q, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (rows.Length == 0) continue;
            _list.Children.Add(BuildSection(section.Title, rows));
        }
        if (_list.Children.Count == 0)
            _list.Children.Add(new TextBlock { Text = "Ничего не найдено", Classes = { "caption" }, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 30) });
    }

    static Control BuildSection(string title, Row[] rows)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("2*,1.4*,1.4*"),
            ColumnSpacing = 10,
            RowSpacing = 6
        };

        void AddRow(int r, Control a, Control b, Control c)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetRow(a, r); Grid.SetColumn(a, 0);
            Grid.SetRow(b, r); Grid.SetColumn(b, 1);
            Grid.SetRow(c, r); Grid.SetColumn(c, 2);
            grid.Children.Add(a); grid.Children.Add(b); grid.Children.Add(c);
        }

        AddRow(0,
            new TextBlock { Text = title, Classes = { "h" } },
            ColumnHeader("Windows", !Hk.IsMac),
            ColumnHeader("macOS", Hk.IsMac));

        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            AddRow(i + 1,
                new TextBlock { Text = row.Action, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5 },
                Keys(row.Win, !Hk.IsMac),
                Keys(row.Mac, Hk.IsMac));
        }

        return new Border { Classes = { "card" }, Child = grid };
    }

    static Control ColumnHeader(string text, bool current)
    {
        var tb = new TextBlock { Text = current ? $"{text}  · ваша система" : text, Classes = { "label" }, VerticalAlignment = VerticalAlignment.Center };
        if (current) tb.Classes.Add("accent");
        return tb;
    }

    /// <summary>Сочетание в виде «клавиш» (Border.kbd).</summary>
    static Control Keys(string combo, bool current)
    {
        var tb = new TextBlock { Text = combo, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        var border = new Border { Classes = { "kbd" }, Child = tb, HorizontalAlignment = HorizontalAlignment.Left };
        border.Opacity = current ? 1.0 : 0.6;
        return border;
    }
}
