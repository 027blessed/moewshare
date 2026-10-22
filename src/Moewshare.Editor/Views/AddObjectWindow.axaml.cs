using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Moewshare.Editor.ViewModels;

namespace Moewshare.Editor.Views;

public sealed partial class ObjectTemplateItemVM : ObservableObject
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string Icon { get; init; } = "";
    public string Category { get; init; } = "";
    public string TagColor { get; init; } = "#FCE8EC";
    public string TagForeground { get; init; } = "#8B1E2D";

    bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(CardBackground));
                OnPropertyChanged(nameof(CardBorderBrush));
                OnPropertyChanged(nameof(CardBorderThickness));
            }
        }
    }

    public IBrush CardBackground => IsSelected ? new SolidColorBrush(Color.Parse("#FFF0F3")) : Brushes.White;
    public IBrush CardBorderBrush => IsSelected ? new SolidColorBrush(Color.Parse("#E05263")) : new SolidColorBrush(Color.Parse("#F1D9DB"));
    public Avalonia.Thickness CardBorderThickness => IsSelected ? new Avalonia.Thickness(2) : new Avalonia.Thickness(1);
}

public partial class AddObjectWindow : Window
{
    readonly EditorViewModel? _editor;
    string _selectedCategory = "Все";
    ObjectTemplateItemVM? _selectedTemplate;

    readonly List<ObjectTemplateItemVM> _allTemplates =
    [
        new()
        {
            Id = "box",
            Title = "Куб (Box)",
            Category = "3D Объекты",
            Icon = "📦",
            Description = "3D примитив для стен, ящиков, блоков и платформ",
            TagColor = "#E8F4F8",
            TagForeground = "#1B4F72"
        },
        new()
        {
            Id = "sphere",
            Title = "Сфера (Sphere)",
            Category = "3D Объекты",
            Icon = "🔮",
            Description = "3D шар для мячей, снарядов, планет или персонажей",
            TagColor = "#E8F4F8",
            TagForeground = "#1B4F72"
        },
        new()
        {
            Id = "cylinder",
            Title = "Цилиндр (Cylinder)",
            Category = "3D Объекты",
            Icon = "🥫",
            Description = "3D цилиндр для колонн, столбов, бочек и труб",
            TagColor = "#E8F4F8",
            TagForeground = "#1B4F72"
        },
        new()
        {
            Id = "plane",
            Title = "Плоскость (Plane)",
            Category = "3D Объекты",
            Icon = "🟩",
            Description = "Горизонтальная поверхность пола или земли",
            TagColor = "#E8F4F8",
            TagForeground = "#1B4F72"
        },
        new()
        {
            Id = "sprite",
            Title = "2D Спрайт (Sprite)",
            Category = "2D & Спрайты",
            Icon = "🖼️",
            Description = "2D изображение (PNG с прозрачностью) с авто-поворотом к камере",
            TagColor = "#FCE8EC",
            TagForeground = "#8B1E2D"
        },
        new()
        {
            Id = "platform",
            Title = "Платформа (Platform)",
            Category = "2D & Спрайты",
            Icon = "🧱",
            Description = "Твёрдый блок с коллизией для прыжков и ходьбы персонажей",
            TagColor = "#FCE8EC",
            TagForeground = "#8B1E2D"
        },
        new()
        {
            Id = "player_platformer",
            Title = "Игрок Платформера",
            Category = "Геймплей & Свет",
            Icon = "🏃",
            Description = "2D персонаж с физикой, прыжками и контроллером движения",
            TagColor = "#E8F8F5",
            TagForeground = "#0E6251"
        },
        new()
        {
            Id = "player_topdown",
            Title = "Игрок Top-Down",
            Category = "Геймплей & Свет",
            Icon = "🕹️",
            Description = "Персонаж с плавным перемещением во все 4 стороны (W/A/S/D)",
            TagColor = "#E8F8F5",
            TagForeground = "#0E6251"
        },
        new()
        {
            Id = "coin",
            Title = "Монетка (Coin)",
            Category = "Геймплей & Свет",
            Icon = "🪙",
            Description = "Вращающийся предмет со скриптом вращения Spin для сбора",
            TagColor = "#FEF9E7",
            TagForeground = "#7D6608"
        },
        new()
        {
            Id = "light",
            Title = "Источник света (Light)",
            Category = "Геймплей & Свет",
            Icon = "💡",
            Description = "Точечный источник света для реалистичного освещения 3D сцены",
            TagColor = "#FEF9E7",
            TagForeground = "#7D6608"
        },
        new()
        {
            Id = "canvas",
            Title = "UI Холст (Canvas)",
            Category = "UI Интерфейс",
            Icon = "📱",
            Description = "Адаптивный экранный холст интерфейса под ПК и смартфоны",
            TagColor = "#EBF5FB",
            TagForeground = "#1B4F72"
        },
        new()
        {
            Id = "ui_text",
            Title = "UI Текст (Text)",
            Category = "UI Интерфейс",
            Icon = "📝",
            Description = "Экранная надпись для счёта очков, таймера, диалогов и подсказок",
            TagColor = "#EBF5FB",
            TagForeground = "#1B4F72"
        },
        new()
        {
            Id = "ui_image",
            Title = "UI Картинка (Image)",
            Category = "UI Интерфейс",
            Icon = "🖼️",
            Description = "Элемент интерфейса: иконка, полоса здоровья, рамка или плашка",
            TagColor = "#EBF5FB",
            TagForeground = "#1B4F72"
        },
        new()
        {
            Id = "ui_button",
            Title = "UI Кнопка (Button)",
            Category = "UI Интерфейс",
            Icon = "🔘",
            Description = "Кнопка с анимацией нажатия и триггерами (Прыжок, Рестарт, Алерт)",
            TagColor = "#EBF5FB",
            TagForeground = "#1B4F72"
        }
    ];

    readonly ObservableCollection<ObjectTemplateItemVM> _displayTemplates = [];

    public AddObjectWindow() : this(null!) { }

    public AddObjectWindow(EditorViewModel editor)
    {
        InitializeComponent();
        _editor = editor;

        TemplatesItemsControl.ItemsSource = _displayTemplates;

        if (_editor?.Selected is not null)
        {
            MakeChildCheckBox.IsVisible = true;
            MakeChildCheckBox.Content = $"🔗 Сделать дочерним для: «{_editor.Selected.Name}»";
            MakeChildCheckBox.IsChecked = false;
        }
        else
        {
            MakeChildCheckBox.IsVisible = false;
        }

        SearchBox.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == nameof(TextBox.Text))
            {
                ApplyFilter();
            }
        };

        UpdateCategoryButtons();
        ApplyFilter();

        // Default selection: Box
        var defaultItem = _displayTemplates.FirstOrDefault();
        if (defaultItem is not null)
        {
            SelectTemplate(defaultItem);
        }
    }

    void UpdateCategoryButtons()
    {
        BtnCatAll.Classes.Set("active", _selectedCategory == "Все");
        BtnCat3D.Classes.Set("active", _selectedCategory == "3D Объекты");
        BtnCat2D.Classes.Set("active", _selectedCategory == "2D & Спрайты");
        BtnCatUi.Classes.Set("active", _selectedCategory == "UI Интерфейс");
        BtnCatGame.Classes.Set("active", _selectedCategory == "Геймплей & Свет");
    }

    void ApplyFilter()
    {
        var q = (SearchBox.Text ?? "").Trim().ToLowerInvariant();
        _displayTemplates.Clear();

        foreach (var t in _allTemplates)
        {
            if (_selectedCategory != "Все" && t.Category != _selectedCategory)
                continue;

            if (!string.IsNullOrEmpty(q) &&
                !t.Title.ToLowerInvariant().Contains(q) &&
                !t.Description.ToLowerInvariant().Contains(q) &&
                !t.Category.ToLowerInvariant().Contains(q) &&
                !t.Id.ToLowerInvariant().Contains(q))
            {
                continue;
            }

            _displayTemplates.Add(t);
        }

        ItemsCountText.Text = $"{_displayTemplates.Count} доступно";

        if (_selectedTemplate is not null && !_displayTemplates.Contains(_selectedTemplate))
        {
            SelectTemplate(_displayTemplates.FirstOrDefault());
        }
    }

    void SelectTemplate(ObjectTemplateItemVM? item)
    {
        if (_selectedTemplate is not null)
        {
            _selectedTemplate.IsSelected = false;
        }
        _selectedTemplate = item;
        if (_selectedTemplate is not null)
        {
            _selectedTemplate.IsSelected = true;
            BtnCreate.Content = $"+ Создать {_selectedTemplate.Title}";
            BtnCreate.IsEnabled = true;
        }
        else
        {
            BtnCreate.Content = "+ Создать объект";
            BtnCreate.IsEnabled = false;
        }
    }

    void OnCategoryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string cat)
        {
            _selectedCategory = cat;
            UpdateCategoryButtons();
            ApplyFilter();
        }
    }

    void OnCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is ObjectTemplateItemVM item)
        {
            SelectTemplate(item);
        }
    }

    void OnCardDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border border && border.DataContext is ObjectTemplateItemVM item)
        {
            SelectTemplate(item);
            CreateAndClose();
        }
    }

    void OnCreateClick(object? sender, RoutedEventArgs e) => CreateAndClose();

    void CreateAndClose()
    {
        if (_selectedTemplate is null || _editor is null) return;
        bool asChild = MakeChildCheckBox.IsChecked == true;
        _editor.AddFromTemplate(_selectedTemplate.Id, asChild);
        Close();
    }

    void OnCancelClick(object? sender, RoutedEventArgs e) => Close();
}
