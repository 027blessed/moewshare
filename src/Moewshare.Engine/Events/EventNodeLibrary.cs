namespace Moewshare.Engine.Events;

public sealed record NodeDescriptor(
    string Type,
    string Title,
    string Category,
    string Description,
    Func<EventNode> Factory
);

public static class EventNodeLibrary
{
    public static IReadOnlyList<NodeDescriptor> All { get; } =
    [
        // === СОБЫТИЯ ===
        new("event_begin_play", "Begin Play (Старт игры)", "События", "Вызывается один раз при старте сцены или игры", () => new EventNode
        {
            NodeType = "event_begin_play",
            Title = "▶ Begin Play",
            Category = "События",
            Outputs = [new("out_exec", "Старт", PortType.Exec, PortDirection.Output)]
        }),

        new("event_update", "On Update (Каждый кадр)", "События", "Срабатывает каждый кадр игры", () => new EventNode
        {
            NodeType = "event_update",
            Title = "⏱ On Update",
            Category = "События",
            Outputs =
            [
                new("out_exec", "Шаг", PortType.Exec, PortDirection.Output),
                new("out_dt", "Delta Time", PortType.Float, PortDirection.Output)
            ]
        }),

        new("event_trigger_enter", "On Trigger Enter (Вход в триггер)", "События", "Срабатывает, когда объект касается или входит в триггер", () => new EventNode
        {
            NodeType = "event_trigger_enter",
            Title = "⚡ On Trigger Enter",
            Category = "События",
            Outputs =
            [
                new("out_exec", "Касание", PortType.Exec, PortDirection.Output),
                new("out_other", "Кто коснулся", PortType.ObjectRef, PortDirection.Output),
                new("out_other_name", "Имя коснувшегося", PortType.String, PortDirection.Output),
                new("out_self", "Этот объект", PortType.ObjectRef, PortDirection.Output)
            ]
        }),

        new("event_collision_enter", "On Collision Enter (Столкновение)", "События", "Срабатывает при физическом ударе об твердый объект", () => new EventNode
        {
            NodeType = "event_collision_enter",
            Title = "💥 On Collision Enter",
            Category = "События",
            Outputs =
            [
                new("out_exec", "Удар", PortType.Exec, PortDirection.Output),
                new("out_other", "Препятствие", PortType.ObjectRef, PortDirection.Output),
                new("out_other_name", "Имя объекта", PortType.String, PortDirection.Output)
            ]
        }),

        new("event_input_pressed", "On Key Pressed (Нажатие клавиши)", "События", "Срабатывает при нажатии заданной клавиши (Пробел, E, W, F...)", () => new EventNode
        {
            NodeType = "event_input_pressed",
            Title = "⌨️ On Key Pressed",
            Category = "События",
            Properties = new() { ["Key"] = "Space" },
            Outputs =
            [
                new("out_exec", "Нажата", PortType.Exec, PortDirection.Output),
                new("out_key", "Код клавиши", PortType.String, PortDirection.Output)
            ]
        }),

        new("event_ui_button_clicked", "On UI Button Clicked (Клик по кнопке UI)", "События", "Срабатывает при клике или тапе на кнопку в интерфейсе (Кликер)", () => new EventNode
        {
            NodeType = "event_ui_button_clicked",
            Title = "👆 On Button Clicked",
            Category = "События",
            Properties = new() { ["ButtonName"] = "" },
            Outputs =
            [
                new("out_exec", "Клик", PortType.Exec, PortDirection.Output),
                new("out_name", "Имя кнопки", PortType.String, PortDirection.Output)
            ]
        }),

        new("event_object_clicked", "On Object Clicked (Клик по объекту в сцене)", "События", "Срабатывает при клике мышкой или тапе на 3D-объект или спрайт (Кликер)", () => new EventNode
        {
            NodeType = "event_object_clicked",
            Title = "🖱️ On Object Clicked",
            Category = "События",
            Properties = new() { ["ObjectName"] = "" },
            Outputs =
            [
                new("out_exec", "Клик", PortType.Exec, PortDirection.Output),
                new("out_self", "Объект", PortType.ObjectRef, PortDirection.Output),
                new("out_name", "Имя объекта", PortType.String, PortDirection.Output)
            ]
        }),

        new("event_timer", "On Timer (По таймеру / Пассивный доход)", "События", "Срабатывает периодически через заданный интервал секунд (автоклик, таймеры)", () => new EventNode
        {
            NodeType = "event_timer",
            Title = "⏲ On Timer",
            Category = "События",
            Properties = new() { ["Interval"] = "1.0", ["Repeat"] = "true" },
            Outputs = [new("out_exec", "Тик", PortType.Exec, PortDirection.Output)]
        }),

        new("event_custom", "Custom Event (Пользовательское событие)", "События", "Срабатывает при отправке пользовательского события из любого места", () => new EventNode
        {
            NodeType = "event_custom",
            Title = "🔔 Custom Event",
            Category = "События",
            Properties = new() { ["EventName"] = "CoinCollected" },
            Outputs = [new("out_exec", "Вызов", PortType.Exec, PortDirection.Output)]
        }),

        // === ПОТОК ВЫПОЛНЕНИЯ ===
        new("flow_branch", "Branch (Условие / If-Else)", "Поток", "Разветвляет логику в зависимости от того, истинно условие или ложно", () => new EventNode
        {
            NodeType = "flow_branch",
            Title = "🔀 Branch",
            Category = "Поток",
            Inputs =
            [
                new("in_exec", "Вход", PortType.Exec, PortDirection.Input),
                new("in_cond", "Условие", PortType.Boolean, PortDirection.Input, "true")
            ],
            Outputs =
            [
                new("out_true", "True (Да)", PortType.Exec, PortDirection.Output),
                new("out_false", "False (Нет)", PortType.Exec, PortDirection.Output)
            ]
        }),

        new("flow_sequence", "Sequence (Последовательность)", "Поток", "Выполняет несколько действий строго по очереди (Затем 0, 1, 2)", () => new EventNode
        {
            NodeType = "flow_sequence",
            Title = "🔢 Sequence",
            Category = "Поток",
            Inputs = [new("in_exec", "Вход", PortType.Exec, PortDirection.Input)],
            Outputs =
            [
                new("out_0", "Затем 0", PortType.Exec, PortDirection.Output),
                new("out_1", "Затем 1", PortType.Exec, PortDirection.Output),
                new("out_2", "Затем 2", PortType.Exec, PortDirection.Output)
            ]
        }),

        new("flow_for_loop", "For Loop (Цикл от .. до)", "Поток", "Повторяет действия заданное количество раз с счётчиком индекса", () => new EventNode
        {
            NodeType = "flow_for_loop",
            Title = "🔁 For Loop",
            Category = "Поток",
            Inputs =
            [
                new("in_exec", "Вход", PortType.Exec, PortDirection.Input),
                new("in_first", "От", PortType.Integer, PortDirection.Input, "0"),
                new("in_last", "До", PortType.Integer, PortDirection.Input, "9")
            ],
            Outputs =
            [
                new("out_body", "Тело цикла", PortType.Exec, PortDirection.Output),
                new("out_index", "Индекс", PortType.Integer, PortDirection.Output),
                new("out_done", "Завершено", PortType.Exec, PortDirection.Output)
            ]
        }),

        new("flow_do_once", "Do Once (Выполнить 1 раз)", "Поток", "Выполняет выход только один раз, пока не будет вызван сброс (Reset)", () => new EventNode
        {
            NodeType = "flow_do_once",
            Title = "🔒 Do Once",
            Category = "Поток",
            Inputs =
            [
                new("in_exec", "Вход", PortType.Exec, PortDirection.Input),
                new("in_reset", "Сброс (Reset)", PortType.Exec, PortDirection.Input)
            ],
            Outputs = [new("out_exec", "Выход", PortType.Exec, PortDirection.Output)]
        }),

        new("flow_delay", "Delay (Задержка в секундах)", "Поток", "Приостанавливает выполнение ветки на указанное время в секундах", () => new EventNode
        {
            NodeType = "flow_delay",
            Title = "⏳ Delay",
            Category = "Поток",
            Inputs =
            [
                new("in_exec", "Вход", PortType.Exec, PortDirection.Input),
                new("in_duration", "Секунд", PortType.Float, PortDirection.Input, "1.0")
            ],
            Outputs = [new("out_exec", "Завершено", PortType.Exec, PortDirection.Output)]
        }),

        // === ПЕРЕМЕННЫЕ ===
        new("var_get", "Get Variable (Получить значение)", "Переменные", "Возвращает текущее значение выбранной переменной", () => new EventNode
        {
            NodeType = "var_get",
            Title = "📦 Get Variable",
            Category = "Переменные",
            Properties = new() { ["VariableName"] = "Coins" },
            Outputs = [new("out_val", "Значение", PortType.Any, PortDirection.Output)]
        }),

        new("var_set", "Set Variable (Записать значение)", "Переменные", "Присваивает новое значение выбранной переменной", () => new EventNode
        {
            NodeType = "var_set",
            Title = "📝 Set Variable",
            Category = "Переменные",
            Properties = new() { ["VariableName"] = "Coins" },
            Inputs =
            [
                new("in_exec", "Вход", PortType.Exec, PortDirection.Input),
                new("in_val", "Новое значение", PortType.Any, PortDirection.Input, "0")
            ],
            Outputs =
            [
                new("out_exec", "Выход", PortType.Exec, PortDirection.Output),
                new("out_val", "Значение", PortType.Any, PortDirection.Output)
            ]
        }),

        new("var_increment", "Increment ++ (Прибавить 1)", "Переменные", "Увеличивает числовую переменную на 1", () => new EventNode
        {
            NodeType = "var_increment",
            Title = "➕➕ Increment (++)",
            Category = "Переменные",
            Properties = new() { ["VariableName"] = "Coins" },
            Inputs = [new("in_exec", "Вход", PortType.Exec, PortDirection.Input)],
            Outputs =
            [
                new("out_exec", "Выход", PortType.Exec, PortDirection.Output),
                new("out_val", "Новое значение", PortType.Integer, PortDirection.Output)
            ]
        }),

        new("var_decrement", "Decrement -- (Отнять 1)", "Переменные", "Уменьшает числовую переменную на 1", () => new EventNode
        {
            NodeType = "var_decrement",
            Title = "➖➖ Decrement (--)",
            Category = "Переменные",
            Properties = new() { ["VariableName"] = "Health" },
            Inputs = [new("in_exec", "Вход", PortType.Exec, PortDirection.Input)],
            Outputs =
            [
                new("out_exec", "Выход", PortType.Exec, PortDirection.Output),
                new("out_val", "Новое значение", PortType.Integer, PortDirection.Output)
            ]
        }),

        // === МАТЕМАТИКА ===
        new("math_add", "➕ Add / Сложение (A + B)", "Математика", "Складывает два числа (A + B, сумма, плюс, add)", () => new EventNode
        {
            NodeType = "math_add",
            Title = "➕ Add (A + B)",
            Category = "Математика",
            Inputs =
            [
                new("in_a", "A", PortType.Float, PortDirection.Input, "0"),
                new("in_b", "B", PortType.Float, PortDirection.Input, "1")
            ],
            Outputs = [new("out_result", "Результат", PortType.Float, PortDirection.Output)]
        }),

        new("math_subtract", "➖ Subtract / Вычитание (A - B)", "Математика", "Вычитает B из A (A - B, минус, разность, sub)", () => new EventNode
        {
            NodeType = "math_subtract",
            Title = "➖ Subtract (A - B)",
            Category = "Математика",
            Inputs =
            [
                new("in_a", "A", PortType.Float, PortDirection.Input, "0"),
                new("in_b", "B", PortType.Float, PortDirection.Input, "1")
            ],
            Outputs = [new("out_result", "Результат", PortType.Float, PortDirection.Output)]
        }),

        new("math_multiply", "✖ Multiply / Умножение (A * B)", "Математика", "Умножает A на B (A * B, произведение, умножить, mul)", () => new EventNode
        {
            NodeType = "math_multiply",
            Title = "✖ Multiply (A * B)",
            Category = "Математика",
            Inputs =
            [
                new("in_a", "A", PortType.Float, PortDirection.Input, "1"),
                new("in_b", "B", PortType.Float, PortDirection.Input, "1")
            ],
            Outputs = [new("out_result", "Результат", PortType.Float, PortDirection.Output)]
        }),

        new("math_divide", "➗ Divide / Деление (A / B)", "Математика", "Делит A на B (A / B, разделить, деление, div)", () => new EventNode
        {
            NodeType = "math_divide",
            Title = "➗ Divide (A / B)",
            Category = "Математика",
            Inputs =
            [
                new("in_a", "A", PortType.Float, PortDirection.Input, "10"),
                new("in_b", "B", PortType.Float, PortDirection.Input, "2")
            ],
            Outputs = [new("out_result", "Результат", PortType.Float, PortDirection.Output)]
        }),

        new("math_clamp", "Clamp (Ограничить мин..макс)", "Математика", "Ограничивает число в диапазоне между Min и Max", () => new EventNode
        {
            NodeType = "math_clamp",
            Title = "📏 Clamp (Мин..Макс)",
            Category = "Математика",
            Inputs =
            [
                new("in_val", "Число", PortType.Float, PortDirection.Input, "50"),
                new("in_min", "Мин", PortType.Float, PortDirection.Input, "0"),
                new("in_max", "Макс", PortType.Float, PortDirection.Input, "100")
            ],
            Outputs = [new("out_result", "Результат", PortType.Float, PortDirection.Output)]
        }),

        new("math_random_int", "Random Integer (Случайное целое)", "Математика", "Генерирует случайное целое число от Мин до Макс включительно", () => new EventNode
        {
            NodeType = "math_random_int",
            Title = "🎲 Random Int",
            Category = "Математика",
            Inputs =
            [
                new("in_min", "Мин", PortType.Integer, PortDirection.Input, "1"),
                new("in_max", "Макс", PortType.Integer, PortDirection.Input, "10")
            ],
            Outputs = [new("out_result", "Результат", PortType.Integer, PortDirection.Output)]
        }),

        // === СРАВНЕНИЕ И ЛОГИКА ===
        new("logic_equal", "A == B (Равно)", "Логика", "Проверяет равенство двух значений", () => new EventNode
        {
            NodeType = "logic_equal",
            Title = "🟰 Equal (A == B)",
            Category = "Логика",
            Inputs =
            [
                new("in_a", "A", PortType.Any, PortDirection.Input, "0"),
                new("in_b", "B", PortType.Any, PortDirection.Input, "0")
            ],
            Outputs = [new("out_result", "Результат", PortType.Boolean, PortDirection.Output)]
        }),

        new("logic_not_equal", "A != B (Не равно)", "Логика", "Проверяет неравенство двух значений", () => new EventNode
        {
            NodeType = "logic_not_equal",
            Title = "≠ Not Equal (A != B)",
            Category = "Логика",
            Inputs =
            [
                new("in_a", "A", PortType.Any, PortDirection.Input, "0"),
                new("in_b", "B", PortType.Any, PortDirection.Input, "0")
            ],
            Outputs = [new("out_result", "Результат", PortType.Boolean, PortDirection.Output)]
        }),

        new("logic_greater", "A > B (Больше)", "Логика", "Проверяет, больше ли A чем B", () => new EventNode
        {
            NodeType = "logic_greater",
            Title = "▶ Greater (A > B)",
            Category = "Логика",
            Inputs =
            [
                new("in_a", "A", PortType.Float, PortDirection.Input, "0"),
                new("in_b", "B", PortType.Float, PortDirection.Input, "0")
            ],
            Outputs = [new("out_result", "Результат", PortType.Boolean, PortDirection.Output)]
        }),

        new("logic_greater_equal", "A >= B (Больше или равно)", "Логика", "Проверяет, больше или равно ли A чем B (хватает ли монет/очков)", () => new EventNode
        {
            NodeType = "logic_greater_equal",
            Title = "≥ Greater or Equal (A >= B)",
            Category = "Логика",
            Inputs =
            [
                new("in_a", "A", PortType.Float, PortDirection.Input, "0"),
                new("in_b", "B", PortType.Float, PortDirection.Input, "0")
            ],
            Outputs = [new("out_result", "Результат", PortType.Boolean, PortDirection.Output)]
        }),

        new("logic_less", "A < B (Меньше)", "Логика", "Проверяет, меньше ли A чем B", () => new EventNode
        {
            NodeType = "logic_less",
            Title = "◀ Less (A < B)",
            Category = "Логика",
            Inputs =
            [
                new("in_a", "A", PortType.Float, PortDirection.Input, "0"),
                new("in_b", "B", PortType.Float, PortDirection.Input, "0")
            ],
            Outputs = [new("out_result", "Результат", PortType.Boolean, PortDirection.Output)]
        }),

        new("logic_less_equal", "A <= B (Меньше или равно)", "Логика", "Проверяет, меньше или равно ли A чем B", () => new EventNode
        {
            NodeType = "logic_less_equal",
            Title = "≤ Less or Equal (A <= B)",
            Category = "Логика",
            Inputs =
            [
                new("in_a", "A", PortType.Float, PortDirection.Input, "0"),
                new("in_b", "B", PortType.Float, PortDirection.Input, "0")
            ],
            Outputs = [new("out_result", "Результат", PortType.Boolean, PortDirection.Output)]
        }),

        new("logic_and", "A AND B (Логическое И)", "Логика", "Истинно, только если оба условия A и B истинны", () => new EventNode
        {
            NodeType = "logic_and",
            Title = "🧩 AND (И)",
            Category = "Логика",
            Inputs =
            [
                new("in_a", "A", PortType.Boolean, PortDirection.Input, "true"),
                new("in_b", "B", PortType.Boolean, PortDirection.Input, "true")
            ],
            Outputs = [new("out_result", "Результат", PortType.Boolean, PortDirection.Output)]
        }),

        new("logic_or", "A OR B (Логическое ИЛИ)", "Логика", "Истинно, если хотя бы одно из условий A или B истинно", () => new EventNode
        {
            NodeType = "logic_or",
            Title = "🔀 OR (ИЛИ)",
            Category = "Логика",
            Inputs =
            [
                new("in_a", "A", PortType.Boolean, PortDirection.Input, "false"),
                new("in_b", "B", PortType.Boolean, PortDirection.Input, "false")
            ],
            Outputs = [new("out_result", "Результат", PortType.Boolean, PortDirection.Output)]
        }),

        new("logic_not", "NOT (Отрицание)", "Логика", "Инвертирует логическое значение (True превращает в False и наоборот)", () => new EventNode
        {
            NodeType = "logic_not",
            Title = "❗ NOT (Отрицание)",
            Category = "Логика",
            Inputs = [new("in_a", "Вход", PortType.Boolean, PortDirection.Input, "false")],
            Outputs = [new("out_result", "Результат", PortType.Boolean, PortDirection.Output)]
        }),

        // === ОБЪЕКТЫ И СЦЕНА ===
        new("object_get_player", "Get Player (Получить игрока)", "Объекты", "Находит и возвращает объект управляемого игрока сцены", () => new EventNode
        {
            NodeType = "object_get_player",
            Title = "🏃 Get Player",
            Category = "Объекты",
            Outputs =
            [
                new("out_obj", "Игрок", PortType.ObjectRef, PortDirection.Output),
                new("out_name", "Имя", PortType.String, PortDirection.Output)
            ]
        }),

        new("object_get_self", "Get Self (Этот объект)", "Объекты", "Возвращает объект, на котором закреплен этот граф", () => new EventNode
        {
            NodeType = "object_get_self",
            Title = "🎯 Get Self",
            Category = "Объекты",
            Outputs =
            [
                new("out_obj", "Объект", PortType.ObjectRef, PortDirection.Output),
                new("out_name", "Имя", PortType.String, PortDirection.Output)
            ]
        }),

        new("object_get_by_name", "Find Object By Name (Поиск по имени)", "Объекты", "Находит объект сцены по точному имени (например, Door, Key, Coin...)", () => new EventNode
        {
            NodeType = "object_get_by_name",
            Title = "🔍 Find By Name",
            Category = "Объекты",
            Properties = new() { ["ObjectName"] = "Door" },
            Outputs =
            [
                new("out_obj", "Объект", PortType.ObjectRef, PortDirection.Output),
                new("out_found", "Найден?", PortType.Boolean, PortDirection.Output)
            ]
        }),

        new("object_destroy", "Destroy Object (Уничтожить / Скрыть)", "Объекты", "Удаляет или скрывает объект со сцены (например, собранную монетку)", () => new EventNode
        {
            NodeType = "object_destroy",
            Title = "💥 Destroy Object",
            Category = "Объекты",
            Inputs =
            [
                new("in_exec", "Вход", PortType.Exec, PortDirection.Input),
                new("in_target", "Цель", PortType.ObjectRef, PortDirection.Input)
            ],
            Outputs = [new("out_exec", "Выход", PortType.Exec, PortDirection.Output)]
        }),

        new("object_set_active", "Set Active (Включить / Выключить)", "Объекты", "Включает или выключает видимость и активность объекта", () => new EventNode
        {
            NodeType = "object_set_active",
            Title = "👁 Set Active",
            Category = "Объекты",
            Inputs =
            [
                new("in_exec", "Вход", PortType.Exec, PortDirection.Input),
                new("in_target", "Цель", PortType.ObjectRef, PortDirection.Input),
                new("in_active", "Активен", PortType.Boolean, PortDirection.Input, "false")
            ],
            Outputs = [new("out_exec", "Выход", PortType.Exec, PortDirection.Output)]
        }),

        new("object_set_position", "Set Position (Переместить)", "Объекты", "Устанавливает мировые координаты объекта (X, Y, Z)", () => new EventNode
        {
            NodeType = "object_set_position",
            Title = "📍 Set Position",
            Category = "Объекты",
            Inputs =
            [
                new("in_exec", "Вход", PortType.Exec, PortDirection.Input),
                new("in_target", "Цель", PortType.ObjectRef, PortDirection.Input),
                new("in_x", "X", PortType.Float, PortDirection.Input, "0"),
                new("in_y", "Y", PortType.Float, PortDirection.Input, "0"),
                new("in_z", "Z", PortType.Float, PortDirection.Input, "0")
            ],
            Outputs = [new("out_exec", "Выход", PortType.Exec, PortDirection.Output)]
        }),

        // === ИГРОК, ЗДОРОВЬЕ И ГЕЙМПЛЕЙ ===
        new("health_damage", "Apply Damage (Нанести урон)", "Здоровье", "Отнимает очки здоровья у цели и проверяет гибель", () => new EventNode
        {
            NodeType = "health_damage",
            Title = "💔 Apply Damage",
            Category = "Здоровье",
            Inputs =
            [
                new("in_exec", "Вход", PortType.Exec, PortDirection.Input),
                new("in_target", "Цель", PortType.ObjectRef, PortDirection.Input),
                new("in_dmg", "Урон", PortType.Integer, PortDirection.Input, "20")
            ],
            Outputs =
            [
                new("out_exec", "Выход", PortType.Exec, PortDirection.Output),
                new("out_hp", "Остаток HP", PortType.Integer, PortDirection.Output),
                new("out_dead", "Погиб?", PortType.Boolean, PortDirection.Output)
            ]
        }),

        new("health_heal", "Heal (Восстановить здоровье)", "Здоровье", "Прибавляет очки здоровья цели до максимального значения", () => new EventNode
        {
            NodeType = "health_heal",
            Title = "💖 Heal (Лечение)",
            Category = "Здоровье",
            Inputs =
            [
                new("in_exec", "Вход", PortType.Exec, PortDirection.Input),
                new("in_target", "Цель", PortType.ObjectRef, PortDirection.Input),
                new("in_amount", "Количество", PortType.Integer, PortDirection.Input, "25")
            ],
            Outputs =
            [
                new("out_exec", "Выход", PortType.Exec, PortDirection.Output),
                new("out_hp", "Текущее HP", PortType.Integer, PortDirection.Output)
            ]
        }),

        new("game_win", "Win Game (Победа)", "Геймплей", "Вызывает экран победы в игре", () => new EventNode
        {
            NodeType = "game_win",
            Title = "🏆 Win Game",
            Category = "Геймплей",
            Inputs = [new("in_exec", "Вход", PortType.Exec, PortDirection.Input)],
            Outputs = [new("out_exec", "Выход", PortType.Exec, PortDirection.Output)]
        }),

        new("game_lose", "Lose Game (Поражение)", "Геймплей", "Вызывает экран поражения (Game Over)", () => new EventNode
        {
            NodeType = "game_lose",
            Title = "💀 Game Over",
            Category = "Геймплей",
            Inputs = [new("in_exec", "Вход", PortType.Exec, PortDirection.Input)],
            Outputs = [new("out_exec", "Выход", PortType.Exec, PortDirection.Output)]
        }),

        new("game_restart", "Restart Game (Перезапуск)", "Геймплей", "Перезагружает текущую сцену и возвращает игрока в начало", () => new EventNode
        {
            NodeType = "game_restart",
            Title = "🔄 Restart Game",
            Category = "Геймплей",
            Inputs = [new("in_exec", "Вход", PortType.Exec, PortDirection.Input)],
            Outputs = [new("out_exec", "Выход", PortType.Exec, PortDirection.Output)]
        }),

        // === ИНТЕРФЕЙС UI ===
        new("ui_set_text", "Set UI Text (Изменить надпись)", "Интерфейс", "Обновляет текст экранного элемента (например счётчика монет)", () => new EventNode
        {
            NodeType = "ui_set_text",
            Title = "🔤 Set UI Text",
            Category = "Интерфейс",
            Properties = new() { ["TargetName"] = "Text", ["Prefix"] = "", ["Suffix"] = "" },
            Inputs =
            [
                new("in_exec", "Вход", PortType.Exec, PortDirection.Input),
                new("in_text", "Значение", PortType.Any, PortDirection.Input, "0")
            ],
            Outputs = [new("out_exec", "Выход", PortType.Exec, PortDirection.Output)]
        }),

        new("ui_format_text", "Format Text (Текст + Значение)", "Интерфейс", "Соединяет префикс и значение переменной в одну строку (например: «Монеты: » + 5)", () => new EventNode
        {
            NodeType = "ui_format_text",
            Title = "🏷️ Format Text",
            Category = "Интерфейс",
            Inputs =
            [
                new("in_prefix", "Префикс", PortType.String, PortDirection.Input, "Монеты: "),
                new("in_val", "Значение", PortType.Any, PortDirection.Input, "0"),
                new("in_suffix", "Суффикс", PortType.String, PortDirection.Input, "")
            ],
            Outputs = [new("out_result", "Итоговая строка", PortType.String, PortDirection.Output)]
        }),

        new("ui_show_message", "Show Alert (Всплывающее окно)", "Интерфейс", "Показывает всплывающее системное сообщение игроку", () => new EventNode
        {
            NodeType = "ui_show_message",
            Title = "💬 Show Alert",
            Category = "Интерфейс",
            Inputs =
            [
                new("in_exec", "Вход", PortType.Exec, PortDirection.Input),
                new("in_msg", "Сообщение", PortType.String, PortDirection.Input, "Привет из Moewshare!")
            ],
            Outputs = [new("out_exec", "Выход", PortType.Exec, PortDirection.Output)]
        }),

        // === ИНВЕНТАРЬ И ПРЕДМЕТЫ ===
        new("inventory_has_item", "Has Item (Есть ли предмет?)", "Инвентарь", "Проверяет наличие предмета с заданным ID (например, Key)", () => new EventNode
        {
            NodeType = "inventory_has_item",
            Title = "🔑 Has Item?",
            Category = "Инвентарь",
            Properties = new() { ["ItemId"] = "Key" },
            Outputs = [new("out_has", "Есть предмет?", PortType.Boolean, PortDirection.Output)]
        }),

        new("inventory_add_item", "Add Item (Добавить предмет)", "Инвентарь", "Добавляет предмет в инвентарь игрока (например, подобранный ключ)", () => new EventNode
        {
            NodeType = "inventory_add_item",
            Title = "🎒 Add Item",
            Category = "Инвентарь",
            Properties = new() { ["ItemId"] = "Key" },
            Inputs = [new("in_exec", "Вход", PortType.Exec, PortDirection.Input)],
            Outputs = [new("out_exec", "Выход", PortType.Exec, PortDirection.Output)]
        }),

        new("inventory_remove_item", "Remove Item (Потратить предмет)", "Инвентарь", "Удаляет предмет из инвентаря игрока при использовании", () => new EventNode
        {
            NodeType = "inventory_remove_item",
            Title = "🗝️ Remove Item",
            Category = "Инвентарь",
            Properties = new() { ["ItemId"] = "Key" },
            Inputs = [new("in_exec", "Вход", PortType.Exec, PortDirection.Input)],
            Outputs = [new("out_exec", "Выход", PortType.Exec, PortDirection.Output)]
        }),

        // === АУДИО ===
        new("audio_play_sound", "Play Sound (Воспроизвести звук)", "Аудио", "Воспроизводит звуковой эффект (звон монетки, клик, прыжок)", () => new EventNode
        {
            NodeType = "audio_play_sound",
            Title = "🔊 Play Sound",
            Category = "Аудио",
            Properties = new() { ["SoundType"] = "coin" },
            Inputs = [new("in_exec", "Вход", PortType.Exec, PortDirection.Input)],
            Outputs = [new("out_exec", "Выход", PortType.Exec, PortDirection.Output)]
        })
    ];

    public static NodeDescriptor? Find(string type) => All.FirstOrDefault(d => d.Type == type);

    public static EventNode CreateNode(string nodeType)
    {
        var desc = All.FirstOrDefault(d => d.Type == nodeType);
        if (desc is not null) return desc.Factory();

        return new EventNode
        {
            NodeType = nodeType,
            Title = nodeType,
            Category = "Пользовательские",
            Inputs = [new("in_exec", "Вход", PortType.Exec, PortDirection.Input)],
            Outputs = [new("out_exec", "Выход", PortType.Exec, PortDirection.Output)]
        };
    }
}
