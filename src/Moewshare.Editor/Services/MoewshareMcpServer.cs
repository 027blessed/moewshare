using System.Collections.ObjectModel;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using Moewshare.Editor.Configuration;
using Moewshare.Editor.ViewModels;
using Moewshare.Engine;
using Moewshare.Engine.Events;

namespace Moewshare.Editor.Services;

public sealed class MoewshareMcpServer : IDisposable
{
    private readonly EditorViewModel _editor;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private int _port = 45124;

    public bool IsRunning => _listener?.IsListening ?? false;
    public int Port => _port;
    public ObservableCollection<string> LiveLogs { get; } = [];

    public event Action<string>? LogMessage;
    public event Action? StatusChanged;

    public MoewshareMcpServer(EditorViewModel editor)
    {
        _editor = editor;
    }

    public async Task StartAsync(int port)
    {
        if (IsRunning && _port == port) return;
        await StopAsync();

        _port = port;
        _cts = new CancellationTokenSource();
        _listener = new HttpListener();

        try
        {
            _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
            _listener.Start();
            Log($"🟢 MCP Сервер запущен на http://127.0.0.1:{_port}/");
            StatusChanged?.Invoke();

            _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token));
        }
        catch (Exception ex)
        {
            Log($"❌ Ошибка запуска MCP сервера на порту {_port}: {ex.Message}");
            _listener = null;
            StatusChanged?.Invoke();
        }
    }

    public async Task StopAsync()
    {
        if (_listener == null) return;
        try
        {
            _cts?.Cancel();
            _listener.Stop();
            _listener.Close();
            if (_listenTask != null)
            {
                await Task.WhenAny(_listenTask, Task.Delay(500));
            }
        }
        catch
        {
            // ignore cleanup errors
        }
        finally
        {
            _listener = null;
            _cts?.Dispose();
            _cts = null;
            _listenTask = null;
            Log("⚪ MCP Сервер остановлен.");
            StatusChanged?.Invoke();
        }
    }

    private static async Task<T> RunOnUiAsync<T>(Func<T> action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return action();
        }

        try
        {
            var task = Dispatcher.UIThread.InvokeAsync(action).GetTask();
            var finished = await Task.WhenAny(task, Task.Delay(1000));
            if (finished == task)
            {
                return await task;
            }
        }
        catch { }

        // Fallback for headless tests or if dispatcher is not pumping
        return action();
    }

    private void Log(string msg)
    {
        var timestamped = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        if (Dispatcher.UIThread.CheckAccess())
        {
            LiveLogs.Insert(0, timestamped);
            if (LiveLogs.Count > 100) LiveLogs.RemoveAt(LiveLogs.Count - 1);
            LogMessage?.Invoke(timestamped);
        }
        else
        {
            try
            {
                Dispatcher.UIThread.Post(() =>
                {
                    LiveLogs.Insert(0, timestamped);
                    if (LiveLogs.Count > 100) LiveLogs.RemoveAt(LiveLogs.Count - 1);
                    LogMessage?.Invoke(timestamped);
                });
            }
            catch
            {
                LiveLogs.Insert(0, timestamped);
                if (LiveLogs.Count > 100) LiveLogs.RemoveAt(LiveLogs.Count - 1);
                LogMessage?.Invoke(timestamped);
            }
        }
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener != null && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequestAsync(context), ct);
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    Log($"⚠️ Ошибка в цикле прослушивания: {ex.Message}");
                }
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;

        // CORS headers
        res.Headers.Add("Access-Control-Allow-Origin", "*");
        res.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        res.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");

        if (req.HttpMethod == "OPTIONS")
        {
            res.StatusCode = 200;
            res.Close();
            return;
        }

        try
        {
            var rawPath = req.Url?.AbsolutePath ?? "/";

            if (req.HttpMethod == "GET" && (rawPath == "/" || rawPath == "/status"))
            {
                await HandleGetStatusAsync(res);
                return;
            }

            if (req.HttpMethod == "GET" && rawPath == "/api/scene")
            {
                await HandleGetSceneApiAsync(res);
                return;
            }

            if (req.HttpMethod == "POST")
            {
                using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                var body = await reader.ReadToEndAsync();

                if (rawPath == "/mcp" || rawPath == "/")
                {
                    await HandleMcpJsonRpcAsync(body, res);
                    return;
                }

                if (rawPath.StartsWith("/api/tools/"))
                {
                    var toolName = rawPath.Substring("/api/tools/".Length).TrimEnd('/');
                    await HandleDirectToolCallAsync(toolName, body, res);
                    return;
                }
            }

            // Not found
            res.StatusCode = 404;
            var notFoundBytes = Encoding.UTF8.GetBytes("{\"error\": \"Not Found\"}");
            res.ContentType = "application/json";
            await res.OutputStream.WriteAsync(notFoundBytes);
            res.Close();
        }
        catch (Exception ex)
        {
            try
            {
                res.StatusCode = 500;
                var errBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { error = ex.Message }));
                res.ContentType = "application/json";
                await res.OutputStream.WriteAsync(errBytes);
                res.Close();
            }
            catch { }
        }
    }

    private async Task HandleGetStatusAsync(HttpListenerResponse res)
    {
        var status = await RunOnUiAsync(() => new
        {
            status = "ok",
            server = "Moewshare Engine MCP Server",
            version = "0.1.0",
            mcp_endpoint = $"http://127.0.0.1:{_port}/mcp",
            project_folder = _editor.ProjectFolder,
            scene_name = _editor.CurrentScene.Name,
            objects_count = _editor.Objects.Count,
            is_running = _editor.IsRunning,
            selected_object = _editor.Selected?.Name
        });

        var json = JsonSerializer.Serialize(status, new JsonSerializerOptions { WriteIndented = true });
        var bytes = Encoding.UTF8.GetBytes(json);
        res.ContentType = "application/json; charset=utf-8";
        res.StatusCode = 200;
        await res.OutputStream.WriteAsync(bytes);
        res.Close();
    }

    private async Task HandleGetSceneApiAsync(HttpListenerResponse res)
    {
        var sceneData = await RunOnUiAsync(() =>
        {
            return _editor.Objects.Select(o => new
            {
                name = o.Name,
                type = o.Type,
                x = o.PosX,
                y = o.PosY,
                z = o.PosZ,
                rot_x = o.RotX,
                rot_y = o.RotY,
                rot_z = o.RotZ,
                scale_x = o.ScaleX,
                scale_y = o.ScaleY,
                scale_z = o.ScaleZ,
                color = o.Color,
                ui_text = o.UiText,
                is_ui = o.IsUiElement,
                behavior = o.Behavior,
                components = o.Model.Components.ToList(),
                is_selected = _editor.Selected == o
            }).ToList();
        });

        var json = JsonSerializer.Serialize(sceneData, new JsonSerializerOptions { WriteIndented = true });
        var bytes = Encoding.UTF8.GetBytes(json);
        res.ContentType = "application/json; charset=utf-8";
        res.StatusCode = 200;
        await res.OutputStream.WriteAsync(bytes);
        res.Close();
    }

    private async Task HandleDirectToolCallAsync(string toolName, string body, HttpListenerResponse res)
    {
        Log($"🌐 REST Tool Call: {toolName}");
        JsonObject args;
        try
        {
            args = JsonNode.Parse(body) as JsonObject ?? new JsonObject();
        }
        catch
        {
            args = new JsonObject();
        }

        var result = await ExecuteToolAsync(toolName, args);
        var bytes = Encoding.UTF8.GetBytes(result);
        res.ContentType = "application/json; charset=utf-8";
        res.StatusCode = 200;
        await res.OutputStream.WriteAsync(bytes);
        res.Close();
    }

    private async Task HandleMcpJsonRpcAsync(string body, HttpListenerResponse res)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(body);
        }
        catch (Exception ex)
        {
            await SendJsonRpcErrorAsync(res, null, -32700, $"Parse error: {ex.Message}");
            return;
        }

        if (root is not JsonObject reqObj)
        {
            await SendJsonRpcErrorAsync(res, null, -32600, "Invalid Request");
            return;
        }

        var id = reqObj["id"];
        var method = reqObj["method"]?.GetValue<string>() ?? "";

        Log($"🤖 MCP Request: {method}");

        switch (method)
        {
            case "initialize":
                await SendJsonRpcResultAsync(res, id, new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new
                    {
                        tools = new { listChanged = false }
                    },
                    serverInfo = new
                    {
                        name = "moewshare-editor",
                        version = "0.1.0"
                    }
                });
                break;

            case "notifications/initialized":
                res.StatusCode = 204;
                res.Close();
                break;

            case "ping":
                await SendJsonRpcResultAsync(res, id, new { });
                break;

            case "tools/list":
                await SendJsonRpcResultAsync(res, id, new
                {
                    tools = GetToolDescriptors()
                });
                break;

            case "tools/call":
                var toolParams = reqObj["params"] as JsonObject;
                var toolName = toolParams?["name"]?.GetValue<string>() ?? "";
                var toolArgs = toolParams?["arguments"] as JsonObject ?? new JsonObject();

                Log($"⚡ tools/call: {toolName} ({toolArgs.ToJsonString()})");
                var textResult = await ExecuteToolAsync(toolName, toolArgs);

                await SendJsonRpcResultAsync(res, id, new
                {
                    content = new object[]
                    {
                        new
                        {
                            type = "text",
                            text = textResult
                        }
                    }
                });
                break;

            default:
                await SendJsonRpcErrorAsync(res, id, -32601, $"Method not found: {method}");
                break;
        }
    }

    private async Task SendJsonRpcResultAsync(HttpListenerResponse res, JsonNode? id, object result)
    {
        var responseObj = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["result"] = JsonSerializer.SerializeToNode(result)
        };

        var json = responseObj.ToJsonString();
        var bytes = Encoding.UTF8.GetBytes(json);
        res.ContentType = "application/json; charset=utf-8";
        res.StatusCode = 200;
        await res.OutputStream.WriteAsync(bytes);
        res.Close();
    }

    private async Task SendJsonRpcErrorAsync(HttpListenerResponse res, JsonNode? id, int code, string message)
    {
        var responseObj = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };

        var json = responseObj.ToJsonString();
        var bytes = Encoding.UTF8.GetBytes(json);
        res.ContentType = "application/json; charset=utf-8";
        res.StatusCode = 200;
        await res.OutputStream.WriteAsync(bytes);
        res.Close();
    }

    private static object[] GetToolDescriptors()
    {
        return
        [
            new
            {
                name = "moewshare_get_status",
                description = "Возвращает текущее состояние редактора Moewshare (папка проекта, сцена, объекты, запущен ли тест игры).",
                inputSchema = new { type = "object", properties = new { } }
            },
            new
            {
                name = "moewshare_get_scene",
                description = "Возвращает список всех объектов текущей сцены с их координатами, размерами, цветом, текстом и компонентами.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        query = new { type = "string", description = "Фильтр поиска по имени объекта (опционально)" }
                    }
                }
            },
            new
            {
                name = "moewshare_create_object",
                description = "Создает новый объект на сцене (Box, Sphere, Cylinder, Plane, Light, Sprite, Canvas, UiText, UiButton, UiImage, Coin, Platform, PlayerPlatformer, PlayerTopDown).",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        type = new { type = "string", description = "Тип объекта или шаблона (box, sphere, cylinder, plane, light, sprite, canvas, ui_text, ui_button, ui_image, coin, platform, player_platformer, player_topdown)" },
                        name = new { type = "string", description = "Имя объекта (опционально, если пусто - сгенерируется автоматически)" },
                        x = new { type = "number", description = "Координата X" },
                        y = new { type = "number", description = "Координата Y" },
                        z = new { type = "number", description = "Координата Z" },
                        color = new { type = "string", description = "Цвет HEX (например #FF69B4, #4A2929, #F2B84B)" },
                        text = new { type = "string", description = "Текст (для UI элементов: кнопки, надписи)" },
                        as_child = new { type = "boolean", description = "Сделать дочерним объектом для текущего выбранного" }
                    },
                    required = new[] { "type" }
                }
            },
            new
            {
                name = "moewshare_update_object",
                description = "Изменяет параметры существующего объекта (позиция, масштаб, поворот, имя, цвет, текст, видимость).",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string", description = "Имя объекта для поиска" },
                        new_name = new { type = "string", description = "Новое имя объекта" },
                        x = new { type = "number" },
                        y = new { type = "number" },
                        z = new { type = "number" },
                        scale_x = new { type = "number" },
                        scale_y = new { type = "number" },
                        scale_z = new { type = "number" },
                        rot_x = new { type = "number" },
                        rot_y = new { type = "number" },
                        rot_z = new { type = "number" },
                        color = new { type = "string" },
                        text = new { type = "string" },
                        solid = new { type = "boolean" },
                        speed = new { type = "number" }
                    },
                    required = new[] { "name" }
                }
            },
            new
            {
                name = "moewshare_delete_object",
                description = "Удаляет объект со сцены по имени.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string", description = "Имя удаляемого объекта" }
                    },
                    required = new[] { "name" }
                }
            },
            new
            {
                name = "moewshare_select_object",
                description = "Выделяет объект в иерархии сцены и инспекторе.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string", description = "Имя объекта" }
                    },
                    required = new[] { "name" }
                }
            },
            new
            {
                name = "moewshare_add_component",
                description = "Добавляет компонент на объект (например: BoxCollider, SphereCollider, Rigidbody, Spin, PlatformerController, TopDownController).",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        object_name = new { type = "string", description = "Имя объекта" },
                        component = new { type = "string", description = "Имя компонента" }
                    },
                    required = new[] { "object_name", "component" }
                }
            },
            new
            {
                name = "moewshare_get_event_graph",
                description = "Возвращает визуальный граф событий (ноды, связи, переменные) для сцены или конкретного объекта.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        object_name = new { type = "string", description = "Оставьте пустым для глобального графа сцены, или укажите имя объекта" }
                    }
                }
            },
            new
            {
                name = "moewshare_add_event_node",
                description = "Добавляет ноду в граф событий сцены или объекта (например: event_begin_play, event_ui_button_clicked, math_add, math_subtract, var_get, var_set, flow_branch, action_ui_set_text).",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        type = new { type = "string", description = "Тип ноды (event_begin_play, event_ui_button_clicked, math_add, math_subtract, flow_branch, var_get, var_set, action_ui_set_text, logic_greater_equal и др.)" },
                        x = new { type = "number", description = "Позиция X на холсте (по умолчанию 200)" },
                        y = new { type = "number", description = "Позиция Y на холсте (по умолчанию 200)" },
                        object_name = new { type = "string", description = "Имя объекта для графа (пусто для глобального)" },
                        properties = new { type = "object", description = "Словарь свойств (например ButtonName, TargetName, Prefix, VariableName)" }
                    },
                    required = new[] { "type" }
                }
            },
            new
            {
                name = "moewshare_connect_nodes",
                description = "Соединяет выходящий порт одной ноды с входящим портом другой ноды проводом.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        from_node_id = new { type = "string" },
                        from_port_id = new { type = "string" },
                        to_node_id = new { type = "string" },
                        to_port_id = new { type = "string" },
                        object_name = new { type = "string", description = "Имя объекта (или пусто для глобального графа)" }
                    },
                    required = new[] { "from_node_id", "from_port_id", "to_node_id", "to_port_id" }
                }
            },
            new
            {
                name = "moewshare_add_variable",
                description = "Создает переменную на графе событий (Score, ClickPower, UpgradeCost и т.д.).",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string", description = "Имя переменной" },
                        type = new { type = "string", description = "Тип переменной: Integer, Float, Boolean, String" },
                        default_value = new { type = "string", description = "Значение по умолчанию" },
                        object_name = new { type = "string", description = "Имя объекта (или пусто для глобального графа)" }
                    },
                    required = new[] { "name" }
                }
            },
            new
            {
                name = "moewshare_run_game",
                description = "Запускает симуляцию игры в редакторе (F5).",
                inputSchema = new { type = "object", properties = new { } }
            },
            new
            {
                name = "moewshare_stop_game",
                description = "Останавливает запущенную симуляцию игры.",
                inputSchema = new { type = "object", properties = new { } }
            },
            new
            {
                name = "moewshare_save_project",
                description = "Сохраняет текущую сцену и настройки проекта.",
                inputSchema = new { type = "object", properties = new { } }
            },
            new
            {
                name = "moewshare_get_console_logs",
                description = "Возвращает последние записи лога консоли движка.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        limit = new { type = "integer", description = "Количество записей (по умолчанию 30)" }
                    }
                }
            }
        ];
    }

    private async Task<string> ExecuteToolAsync(string name, JsonObject args)
    {
        try
        {
            return await RunOnUiAsync(() =>
            {
                switch (name)
                {
                    case "moewshare_get_status":
                        return JsonSerializer.Serialize(new
                        {
                            project_folder = _editor.ProjectFolder,
                            scene_name = _editor.CurrentScene.Name,
                            objects_count = _editor.Objects.Count,
                            is_running = _editor.IsRunning,
                            is_dirty = _editor.IsDirty,
                            selected = _editor.Selected?.Name
                        }, new JsonSerializerOptions { WriteIndented = true });

                    case "moewshare_get_scene":
                    {
                        var query = args["query"]?.GetValue<string>()?.ToLowerInvariant();
                        var objs = _editor.Objects.AsEnumerable();
                        if (!string.IsNullOrEmpty(query))
                        {
                            objs = objs.Where(o => o.Name.ToLowerInvariant().Contains(query));
                        }

                        var resultList = objs.Select(o => new
                        {
                            name = o.Name,
                            type = o.Type,
                            pos = new { x = o.PosX, y = o.PosY, z = o.PosZ },
                            scale = new { x = o.ScaleX, y = o.ScaleY, z = o.ScaleZ },
                            color = o.Color,
                            text = o.UiText,
                            behavior = o.Behavior,
                            components = o.Model.Components.ToList(),
                            has_graph = o.Model.EventGraph.Nodes.Count > 0,
                            graph_nodes_count = o.Model.EventGraph.Nodes.Count
                        }).ToList();

                        return JsonSerializer.Serialize(resultList, new JsonSerializerOptions { WriteIndented = true });
                    }

                    case "moewshare_create_object":
                    {
                        var typeStr = args["type"]?.GetValue<string>() ?? "box";
                        var customName = args["name"]?.GetValue<string>();
                        var asChild = args["as_child"]?.GetValue<bool>() ?? false;

                        _editor.AddFromTemplate(typeStr, asChild);
                        var created = _editor.Selected;
                        if (created != null)
                        {
                            if (!string.IsNullOrWhiteSpace(customName))
                            {
                                created.Name = _editor.Unique(customName);
                            }

                            if (args["x"] != null) created.PosX = args["x"]!.GetValue<double>();
                            if (args["y"] != null) created.PosY = args["y"]!.GetValue<double>();
                            if (args["z"] != null) created.PosZ = args["z"]!.GetValue<double>();

                            if (args["color"] != null) created.Color = args["color"]!.GetValue<string>();
                            if (args["text"] != null) created.UiText = args["text"]!.GetValue<string>();

                            _editor.Changed();
                            return JsonSerializer.Serialize(new
                            {
                                success = true,
                                message = $"Объект «{created.Name}» ({created.Type}) успешно создан!",
                                name = created.Name,
                                type = created.Type
                            });
                        }

                        return JsonSerializer.Serialize(new { success = false, message = "Не удалось создать объект" });
                    }

                    case "moewshare_update_object":
                    {
                        var targetName = args["name"]?.GetValue<string>();
                        if (string.IsNullOrEmpty(targetName)) return JsonSerializer.Serialize(new { success = false, error = "Имя объекта обязательно" });

                        var obj = _editor.Objects.FirstOrDefault(o => o.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase));
                        if (obj == null) return JsonSerializer.Serialize(new { success = false, error = $"Объект «{targetName}» не найден" });

                        if (args["new_name"] != null) obj.Name = args["new_name"]!.GetValue<string>();
                        if (args["x"] != null) obj.PosX = args["x"]!.GetValue<double>();
                        if (args["y"] != null) obj.PosY = args["y"]!.GetValue<double>();
                        if (args["z"] != null) obj.PosZ = args["z"]!.GetValue<double>();
                        if (args["scale_x"] != null) obj.ScaleX = args["scale_x"]!.GetValue<double>();
                        if (args["scale_y"] != null) obj.ScaleY = args["scale_y"]!.GetValue<double>();
                        if (args["scale_z"] != null) obj.ScaleZ = args["scale_z"]!.GetValue<double>();
                        if (args["rot_x"] != null) obj.RotX = args["rot_x"]!.GetValue<double>();
                        if (args["rot_y"] != null) obj.RotY = args["rot_y"]!.GetValue<double>();
                        if (args["rot_z"] != null) obj.RotZ = args["rot_z"]!.GetValue<double>();
                        if (args["color"] != null) obj.Color = args["color"]!.GetValue<string>();
                        if (args["text"] != null) obj.UiText = args["text"]!.GetValue<string>();
                        if (args["solid"] != null) obj.Solid = args["solid"]!.GetValue<bool>();
                        if (args["speed"] != null) obj.Speed = args["speed"]!.GetValue<double>();

                        _editor.Changed();
                        return JsonSerializer.Serialize(new { success = true, message = $"Объект «{obj.Name}» обновлен", name = obj.Name });
                    }

                    case "moewshare_delete_object":
                    {
                        var targetName = args["name"]?.GetValue<string>();
                        var obj = _editor.Objects.FirstOrDefault(o => o.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase));
                        if (obj == null) return JsonSerializer.Serialize(new { success = false, error = $"Объект «{targetName}» не найден" });

                        _editor.Selected = obj;
                        _editor.DeleteSelectedCommand.Execute(null);
                        return JsonSerializer.Serialize(new { success = true, message = $"Объект «{targetName}» удален" });
                    }

                    case "moewshare_select_object":
                    {
                        var targetName = args["name"]?.GetValue<string>();
                        var obj = _editor.Objects.FirstOrDefault(o => o.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase));
                        if (obj == null) return JsonSerializer.Serialize(new { success = false, error = $"Объект «{targetName}» не найден" });

                        _editor.Selected = obj;
                        return JsonSerializer.Serialize(new { success = true, message = $"Объект «{obj.Name}» выбран", name = obj.Name });
                    }

                    case "moewshare_add_component":
                    {
                        var targetName = args["object_name"]?.GetValue<string>();
                        var comp = args["component"]?.GetValue<string>();
                        var obj = _editor.Objects.FirstOrDefault(o => o.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase));
                        if (obj == null) return JsonSerializer.Serialize(new { success = false, error = $"Объект «{targetName}» не найден" });

                        if (!string.IsNullOrEmpty(comp))
                        {
                            obj.AddComponent(comp);
                            _editor.Changed();
                            return JsonSerializer.Serialize(new { success = true, message = $"Компонент «{comp}» добавлен на «{obj.Name}»" });
                        }
                        return JsonSerializer.Serialize(new { success = false, message = "Компонент не указан" });
                    }

                    case "moewshare_get_event_graph":
                    {
                        var objName = args["object_name"]?.GetValue<string>();
                        EventGraph graph;
                        if (!string.IsNullOrEmpty(objName))
                        {
                            var targetObj = _editor.Objects.FirstOrDefault(o => o.Name.Equals(objName, StringComparison.OrdinalIgnoreCase));
                            if (targetObj == null) return JsonSerializer.Serialize(new { error = $"Объект «{objName}» не найден" });
                            graph = targetObj.Model.EventGraph;
                        }
                        else
                        {
                            graph = _editor.CurrentScene.EventGraph;
                        }

                        return JsonSerializer.Serialize(new
                        {
                            target = string.IsNullOrEmpty(objName) ? "Scene" : objName,
                            variables = graph.Variables,
                            nodes = graph.Nodes.Select(n => new
                            {
                                id = n.Id,
                                type = n.NodeType,
                                title = n.Title,
                                x = n.PosX,
                                y = n.PosY,
                                properties = n.Properties,
                                inputs = n.Inputs.Select(p => new { id = p.Id, name = p.Name, type = p.Type.ToString() }),
                                outputs = n.Outputs.Select(p => new { id = p.Id, name = p.Name, type = p.Type.ToString() })
                            }),
                            connections = graph.Connections.Select(c => new
                            {
                                from_node = c.FromNodeId,
                                from_port = c.FromPortId,
                                to_node = c.ToNodeId,
                                to_port = c.ToPortId
                            })
                        }, new JsonSerializerOptions { WriteIndented = true });
                    }

                    case "moewshare_add_event_node":
                    {
                        var nodeType = args["type"]?.GetValue<string>();
                        if (string.IsNullOrEmpty(nodeType)) return JsonSerializer.Serialize(new { success = false, error = "Тип ноды обязателен" });

                        var posX = args["x"]?.GetValue<double>() ?? 250;
                        var posY = args["y"]?.GetValue<double>() ?? 200;
                        var objName = args["object_name"]?.GetValue<string>();

                        var graphPanel = _editor.AllPanels.FirstOrDefault(p => p.Id == "eventgraph") as EventGraphPanelVM;
                        var graphVM = graphPanel?.GraphVM;

                        EventGraph targetGraph;
                        if (!string.IsNullOrEmpty(objName))
                        {
                            var targetObj = _editor.Objects.FirstOrDefault(o => o.Name.Equals(objName, StringComparison.OrdinalIgnoreCase));
                            if (targetObj == null) return JsonSerializer.Serialize(new { success = false, error = $"Объект «{objName}» не найден" });
                            targetGraph = targetObj.Model.EventGraph;
                            if (graphVM != null) graphVM.SelectObjectGraph(targetObj);
                        }
                        else
                        {
                            targetGraph = _editor.CurrentScene.EventGraph;
                            if (graphVM != null && graphVM.IsObjectGraph) graphVM.SelectGlobalGraph();
                        }

                        var node = EventNodeLibrary.CreateNode(nodeType);
                        node.PosX = posX;
                        node.PosY = posY;

                        if (args["properties"] is JsonObject propsObj)
                        {
                            foreach (var kvp in propsObj)
                            {
                                if (kvp.Value != null) node.Properties[kvp.Key] = kvp.Value.ToString();
                            }
                        }

                        targetGraph.AddNode(node);
                        if (graphVM != null)
                        {
                            graphVM.SelectedNode = node;
                            graphVM.NotifyGraphChanged();
                        }

                        _editor.MarkDirty();
                        return JsonSerializer.Serialize(new
                        {
                            success = true,
                            message = $"Нода «{node.Title}» создана",
                            node_id = node.Id,
                            node_type = node.NodeType
                        });
                    }

                    case "moewshare_connect_nodes":
                    {
                        var fromNodeId = args["from_node_id"]?.GetValue<string>();
                        var fromPortId = args["from_port_id"]?.GetValue<string>();
                        var toNodeId = args["to_node_id"]?.GetValue<string>();
                        var toPortId = args["to_port_id"]?.GetValue<string>();
                        var objName = args["object_name"]?.GetValue<string>();

                        if (string.IsNullOrEmpty(fromNodeId) || string.IsNullOrEmpty(fromPortId) ||
                            string.IsNullOrEmpty(toNodeId) || string.IsNullOrEmpty(toPortId))
                        {
                            return JsonSerializer.Serialize(new { success = false, error = "Все параметры портов обязательны" });
                        }

                        EventGraph targetGraph;
                        if (!string.IsNullOrEmpty(objName))
                        {
                            var targetObj = _editor.Objects.FirstOrDefault(o => o.Name.Equals(objName, StringComparison.OrdinalIgnoreCase));
                            if (targetObj == null) return JsonSerializer.Serialize(new { success = false, error = $"Объект «{objName}» не найден" });
                            targetGraph = targetObj.Model.EventGraph;
                        }
                        else
                        {
                            targetGraph = _editor.CurrentScene.EventGraph;
                        }

                        var conn = new EventConnection(fromNodeId, fromPortId, toNodeId, toPortId);
                        if (targetGraph != null && !targetGraph.Connections.Contains(conn))
                        {
                            targetGraph.Connections.Add(conn);
                        }

                        var graphPanel = _editor.AllPanels.FirstOrDefault(p => p.Id == "eventgraph") as EventGraphPanelVM;
                        graphPanel?.GraphVM.NotifyGraphChanged();
                        _editor.MarkDirty();

                        return JsonSerializer.Serialize(new { success = true, message = "Соединение успешно создано" });
                    }

                    case "moewshare_add_variable":
                    {
                        var varName = args["name"]?.GetValue<string>();
                        if (string.IsNullOrEmpty(varName)) return JsonSerializer.Serialize(new { success = false, error = "Имя переменной обязательно" });
                        var varTypeStr = args["type"]?.GetValue<string>() ?? "Integer";
                        var defVal = args["default_value"]?.GetValue<string>() ?? "0";
                        var objName = args["object_name"]?.GetValue<string>();

                        if (!Enum.TryParse<PortType>(varTypeStr, true, out var varType)) varType = PortType.Integer;

                        EventGraph targetGraph;
                        if (!string.IsNullOrEmpty(objName))
                        {
                            var targetObj = _editor.Objects.FirstOrDefault(o => o.Name.Equals(objName, StringComparison.OrdinalIgnoreCase));
                            if (targetObj == null) return JsonSerializer.Serialize(new { success = false, error = $"Объект «{objName}» не найден" });
                            targetGraph = targetObj.Model.EventGraph;
                        }
                        else
                        {
                            targetGraph = _editor.CurrentScene.EventGraph;
                        }

                        var variable = new GraphVariable(varName, varType, defVal);
                        targetGraph.Variables.Add(variable);

                        var graphPanel = _editor.AllPanels.FirstOrDefault(p => p.Id == "eventgraph") as EventGraphPanelVM;
                        graphPanel?.GraphVM.RefreshVariables();
                        _editor.MarkDirty();

                        return JsonSerializer.Serialize(new { success = true, message = $"Переменная «{varName}» создана", name = varName });
                    }

                    case "moewshare_run_game":
                    {
                        if (!_editor.IsRunning)
                        {
                            _editor.RunCommand.Execute(null);
                            return JsonSerializer.Serialize(new { success = true, message = "Игра запущена!" });
                        }
                        return JsonSerializer.Serialize(new { success = false, message = "Игра уже запущена" });
                    }

                    case "moewshare_stop_game":
                    {
                        if (_editor.IsRunning)
                        {
                            _editor.StopCommand.Execute(null);
                            return JsonSerializer.Serialize(new { success = true, message = "Игра остановлена." });
                        }
                        return JsonSerializer.Serialize(new { success = false, message = "Игра не была запущена" });
                    }

                    case "moewshare_save_project":
                    {
                        _editor.SaveCommand.Execute(null);
                        return JsonSerializer.Serialize(new { success = true, message = "Проект сохранен!" });
                    }

                    case "moewshare_get_console_logs":
                    {
                        var limit = args["limit"]?.GetValue<int>() ?? 30;
                        var entries = _editor.ConsoleEntries.TakeLast(limit).Select(e => new
                        {
                            time = e.Time,
                            level = e.Level.ToString(),
                            message = e.Message
                        }).ToList();

                        return JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
                    }

                    default:
                        return JsonSerializer.Serialize(new { error = $"Неизвестная команда: {name}" });
                }
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Автоматически прописывает MCP сервер Moewshare в конфигурацию Antigravity (~/.gemini/config/mcp_config.json)
    /// </summary>
    public static async Task<bool> ConfigureAntigravityAsync(int port)
    {
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var configDir = Path.Combine(home, ".gemini", "config");
            var configFile = Path.Combine(configDir, "mcp_config.json");

            Directory.CreateDirectory(configDir);

            JsonObject root;
            if (File.Exists(configFile))
            {
                var content = await File.ReadAllTextAsync(configFile);
                root = JsonNode.Parse(content) as JsonObject ?? new JsonObject();
            }
            else
            {
                root = new JsonObject();
            }

            if (!root.ContainsKey("mcpServers") || root["mcpServers"] is not JsonObject)
            {
                root["mcpServers"] = new JsonObject();
            }

            var servers = (JsonObject)root["mcpServers"]!;
            servers["moewshare"] = new JsonObject
            {
                ["url"] = $"http://127.0.0.1:{port}/mcp"
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(root, options);
            await File.WriteAllTextAsync(configFile, json);

            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _ = StopAsync();
    }
}
