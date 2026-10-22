using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Moewshare.Editor.Configuration;
using Moewshare.Editor.Services;
using Moewshare.Editor.ViewModels;

namespace Moewshare.Editor.Views;

public partial class SettingsWindow : Window
{
    private readonly EditorViewModel? _editor;
    private EditorSettings _settings = new();

    public SettingsWindow() : this(null!) { }

    public SettingsWindow(EditorViewModel? editor)
    {
        InitializeComponent();
        _editor = editor;
        _settings = EditorSettingsService.Load();

        LoadValuesToUi();

        if (_editor?.McpServer != null)
        {
            LogsItemsControl.ItemsSource = _editor.McpServer.LiveLogs;
            _editor.McpServer.StatusChanged += UpdateMcpStatusUi;
            UpdateMcpStatusUi();
        }
    }

    private void LoadValuesToUi()
    {
        // General
        BoxAutoSave.Text = _settings.AutoSaveIntervalMinutes.ToString();
        CheckShowGrid.IsChecked = _settings.ShowGrid;
        CheckSnapToGrid.IsChecked = _settings.SnapToGrid;
        BoxGridSize.Text = _settings.GridSize.ToString("0.0");

        // Render
        BoxFpsLimit.Text = _settings.FpsLimit.ToString();
        CheckAntiAliasing.IsChecked = _settings.AntiAliasing;

        // MCP
        CheckMcpEnabled.IsChecked = _settings.McpEnabled;
        CheckMcpAutoStart.IsChecked = _settings.AutoStartMcp;
        BoxMcpPort.Text = _settings.McpPort.ToString();
    }

    private void UpdateMcpStatusUi()
    {
        var isRunning = _editor?.McpServer?.IsRunning ?? false;
        var port = _editor?.McpServer?.Port ?? _settings.McpPort;

        if (isRunning)
        {
            BadgeServerStatus.Background = Brush.Parse("#E8F8F5");
            TextServerStatus.Text = $"🟢 Активен (порт {port})";
            TextServerStatus.Foreground = Brush.Parse("#27AE60");
            TextServerEndpoint.Text = $"Эндпоинт: http://127.0.0.1:{port}/mcp";
            BtnToggleServer.Content = "Остановить сервер";
            McpHeaderBadge.Text = "🟢 AI & MCP подключен";
        }
        else
        {
            BadgeServerStatus.Background = Brush.Parse("#F2F4F4");
            TextServerStatus.Text = "⚪ Остановлен";
            TextServerStatus.Foreground = Brush.Parse("#7F8C8D");
            TextServerEndpoint.Text = "Сервер отключен в настройках";
            BtnToggleServer.Content = "Запустить сервер";
            McpHeaderBadge.Text = "⚪ MCP сервер выключен";
        }
    }

    private async void OnRestartMcpClick(object? sender, RoutedEventArgs e)
    {
        if (_editor?.McpServer == null) return;

        if (_editor.McpServer.IsRunning)
        {
            await _editor.McpServer.StopAsync();
            StatusInfoText.Text = "MCP сервер остановлен.";
        }
        else
        {
            if (int.TryParse(BoxMcpPort.Text, out var port) && port > 1024 && port < 65535)
            {
                await _editor.McpServer.StartAsync(port);
                StatusInfoText.Text = $"MCP сервер запущен на порту {port}!";
            }
            else
            {
                StatusInfoText.Text = "Неверный номер порта (используйте от 1025 до 65534).";
            }
        }

        UpdateMcpStatusUi();
    }

    private async void OnConfigureAntigravityClick(object? sender, RoutedEventArgs e)
    {
        var port = _editor?.McpServer?.Port ?? 45124;
        if (int.TryParse(BoxMcpPort.Text, out var customPort)) port = customPort;

        var success = await MoewshareMcpServer.ConfigureAntigravityAsync(port);
        if (success)
        {
            StatusInfoText.Text = "🎉 Конфигурация успешно добавлена в Antigravity! Теперь ассистент может управлять редактором.";
            _editor?.Say("Конфигурация MCP для Antigravity успешно сохранена (~/.gemini/config/mcp_config.json).", ConsoleLevel.Success);
        }
        else
        {
            StatusInfoText.Text = "Не удалось автоматически записать конфиг. Используйте кнопку «Скопировать JSON».";
        }
    }

    private async void OnCopyJsonClick(object? sender, RoutedEventArgs e)
    {
        var port = _editor?.McpServer?.Port ?? 45124;
        if (int.TryParse(BoxMcpPort.Text, out var customPort)) port = customPort;

        var json = "{\n  \"mcpServers\": {\n    \"moewshare\": {\n      \"url\": \"http://127.0.0.1:" + port + "/mcp\"\n    }\n  }\n}";

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard != null)
        {
            await topLevel.Clipboard.SetTextAsync(json);
            StatusInfoText.Text = "📋 Конфигурация MCP скопирована в буфер обмена!";
        }
    }

    private void OnOpenInBrowserClick(object? sender, RoutedEventArgs e)
    {
        var port = _editor?.McpServer?.Port ?? 45124;
        if (int.TryParse(BoxMcpPort.Text, out var customPort)) port = customPort;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = $"http://127.0.0.1:{port}/status",
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void OnClearMcpLogClick(object? sender, RoutedEventArgs e)
    {
        _editor?.McpServer?.LiveLogs.Clear();
        StatusInfoText.Text = "Лог запросов очищен.";
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (int.TryParse(BoxAutoSave.Text, out var autoSave)) _settings.AutoSaveIntervalMinutes = autoSave;
        _settings.ShowGrid = CheckShowGrid.IsChecked ?? true;
        _settings.SnapToGrid = CheckSnapToGrid.IsChecked ?? true;
        if (double.TryParse(BoxGridSize.Text, out var gridSize)) _settings.GridSize = gridSize;

        if (int.TryParse(BoxFpsLimit.Text, out var fps)) _settings.FpsLimit = fps;
        _settings.AntiAliasing = CheckAntiAliasing.IsChecked ?? true;

        _settings.McpEnabled = CheckMcpEnabled.IsChecked ?? true;
        _settings.AutoStartMcp = CheckMcpAutoStart.IsChecked ?? true;
        if (int.TryParse(BoxMcpPort.Text, out var port)) _settings.McpPort = port;

        EditorSettingsService.Save(_settings);
        StatusInfoText.Text = "Настройки успешно сохранены!";
        _editor?.Say("Настройки редактора сохранены.", ConsoleLevel.Info);
        Close();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
