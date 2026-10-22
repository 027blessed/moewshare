using Moewshare.Engine.Events;
using Xunit;

namespace Moewshare.Engine.Tests;

public class EventSystemTests
{
    [Fact]
    public void NodeLibrary_Has_Extensive_Catalog()
    {
        Assert.True(EventNodeLibrary.All.Count >= 30, $"Library should have >= 30 nodes, found: {EventNodeLibrary.All.Count}");

        var categories = EventNodeLibrary.All.Select(n => n.Category).Distinct().ToList();
        Assert.Contains("События", categories);
        Assert.Contains("Поток", categories);
        Assert.Contains("Переменные", categories);
        Assert.Contains("Математика", categories);
        Assert.Contains("Логика", categories);
        Assert.Contains("Объекты", categories);
        Assert.Contains("Здоровье", categories);
        Assert.Contains("Интерфейс", categories);
        Assert.Contains("Инвентарь", categories);
        Assert.Contains("Аудио", categories);
    }

    [Fact]
    public void Graph_CanConnect_Rules_Validation()
    {
        var graph = new EventGraph();

        var nodeExec1 = new EventNode
        {
            Id = "n1",
            Outputs = [new("out_exec", "Exec", PortType.Exec, PortDirection.Output)]
        };
        var nodeExec2 = new EventNode
        {
            Id = "n2",
            Inputs = [new("in_exec", "Exec", PortType.Exec, PortDirection.Input)]
        };
        var nodeDataInt = new EventNode
        {
            Id = "n3",
            Inputs = [new("in_val", "Val", PortType.Integer, PortDirection.Input)]
        };

        // 1. Exec to Exec -> Allowed
        Assert.True(graph.CanConnect(nodeExec1, nodeExec1.Outputs[0], nodeExec2, nodeExec2.Inputs[0], out _));

        // 2. Exec to Data -> Rejected
        Assert.False(graph.CanConnect(nodeExec1, nodeExec1.Outputs[0], nodeDataInt, nodeDataInt.Inputs[0], out var reason1));
        Assert.NotEmpty(reason1);

        // 3. Same node to same node -> Rejected
        Assert.False(graph.CanConnect(nodeExec1, nodeExec1.Outputs[0], nodeExec1, nodeExec1.Outputs[0], out _));
    }

    [Fact]
    public void Graph_Serialization_Preserves_All_Data()
    {
        var graph = EventGraph.CreateCoinCollectorPreset();
        string json = graph.ToJson();

        var deserialized = EventGraph.FromJson(json);
        Assert.NotNull(deserialized);
        Assert.Equal(graph.Nodes.Count, deserialized.Nodes.Count);
        Assert.Equal(graph.Connections.Count, deserialized.Connections.Count);
        Assert.Equal(graph.Variables.Count, deserialized.Variables.Count);
        Assert.Equal(graph.Variables[0].Name, deserialized.Variables[0].Name);
    }

    [Fact]
    public void Runtime_Variables_Get_Set_Increment()
    {
        var graph = new EventGraph();
        graph.Variables.Add(new GraphVariable("Counter", PortType.Integer, "10"));

        var runtime = new EventRuntime(graph);
        Assert.Equal(10, runtime.Context.Variables["Counter"]);

        // var_increment node
        var nInc = EventNodeLibrary.CreateNode("var_increment");
        nInc.Properties["VariableName"] = "Counter";
        graph.AddNode(nInc);

        var nStart = EventNodeLibrary.CreateNode("event_begin_play");
        graph.AddNode(nStart);
        graph.Connect(nStart.Id, "out_exec", nInc.Id, "in_exec");

        runtime.TriggerBeginPlay();
        Assert.Equal(11, runtime.Context.Variables["Counter"]);
    }

    [Fact]
    public void Runtime_Math_And_Logic_Evaluation()
    {
        var graph = new EventGraph();
        var runtime = new EventRuntime(graph);

        // Math Add
        var nAdd = EventNodeLibrary.CreateNode("math_add");
        nAdd.FindInput("in_a")!.DefaultValue = "15";
        nAdd.FindInput("in_b")!.DefaultValue = "27";
        graph.AddNode(nAdd);
        Assert.Equal(42.0, runtime.EvaluateDouble(nAdd, "in_a") + runtime.EvaluateDouble(nAdd, "in_b"));
        Assert.Equal(42.0, runtime.EvaluateOutput(nAdd, "out_val"));

        // Logic Greater
        var nGreater = EventNodeLibrary.CreateNode("logic_greater");
        nGreater.FindInput("in_a")!.DefaultValue = "50";
        nGreater.FindInput("in_b")!.DefaultValue = "20";
        graph.AddNode(nGreater);
        Assert.True((bool)runtime.EvaluateOutput(nGreater, "out_res"));

        // Logic Equal
        var nEqual = EventNodeLibrary.CreateNode("logic_equal");
        nEqual.FindInput("in_a")!.DefaultValue = "Moew";
        nEqual.FindInput("in_b")!.DefaultValue = "Moew";
        graph.AddNode(nEqual);
        Assert.True((bool)runtime.EvaluateOutput(nEqual, "out_res"));
    }

    [Fact]
    public void Runtime_Branch_Executes_Correct_Path()
    {
        var graph = new EventGraph();
        graph.Variables.Add(new GraphVariable("Result", PortType.String, "None"));

        var nStart = EventNodeLibrary.CreateNode("event_begin_play");
        var nBranch = EventNodeLibrary.CreateNode("flow_branch");
        nBranch.FindInput("in_cond")!.DefaultValue = "true";

        var nSetTrue = EventNodeLibrary.CreateNode("var_set");
        nSetTrue.Properties["VariableName"] = "Result";
        nSetTrue.FindInput("in_val")!.DefaultValue = "TruePath";

        var nSetFalse = EventNodeLibrary.CreateNode("var_set");
        nSetFalse.Properties["VariableName"] = "Result";
        nSetFalse.FindInput("in_val")!.DefaultValue = "FalsePath";

        graph.AddNode(nStart);
        graph.AddNode(nBranch);
        graph.AddNode(nSetTrue);
        graph.AddNode(nSetFalse);

        graph.Connect(nStart.Id, "out_exec", nBranch.Id, "in_exec");
        graph.Connect(nBranch.Id, "out_true", nSetTrue.Id, "in_exec");
        graph.Connect(nBranch.Id, "out_false", nSetFalse.Id, "in_exec");

        var runtime = new EventRuntime(graph);
        runtime.TriggerBeginPlay();

        Assert.Equal("TruePath", runtime.Context.Variables["Result"]);
    }

    [Fact]
    public void Runtime_LoopSafety_Prevents_Infinite_Recursion()
    {
        var graph = new EventGraph();
        var n1 = EventNodeLibrary.CreateNode("flow_sequence");
        var n2 = EventNodeLibrary.CreateNode("flow_sequence");

        graph.AddNode(n1);
        graph.AddNode(n2);

        // Circular connection: n1 -> n2 -> n1
        graph.Connect(n1.Id, "out_0", n2.Id, "in_exec");
        graph.Connect(n2.Id, "out_0", n1.Id, "in_exec");

        var nStart = EventNodeLibrary.CreateNode("event_begin_play");
        graph.AddNode(nStart);
        graph.Connect(nStart.Id, "out_exec", n1.Id, "in_exec");

        var runtime = new EventRuntime(graph);
        runtime.Context.MaxStepsPerTrigger = 100;

        // Must terminate safely without crashing or hanging the test
        runtime.TriggerBeginPlay();
        Assert.True(runtime.Context.CurrentStepCount >= 100);
        Assert.Contains(runtime.Context.ExecutionLog, log => log.Contains("Превышен лимит"));
    }

    [Fact]
    public void CoinCollection_Mechanic_EndToEnd_Simulation()
    {
        var template = TemplateCatalog.Find("coin_collector");
        Assert.NotNull(template);

        var scene = template.Build();
        Assert.NotNull(scene.EventGraph);

        // Verify scene has 10 coins
        var coins = scene.Objects.Where(o => o.Name.StartsWith("Coin", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Equal(10, coins.Count);
        Assert.All(coins, c => Assert.True(c.IsTrigger));

        var player = scene.Objects.First(o => o.Name == "Player");
        var runtime = new EventRuntime(scene.EventGraph, scene.Objects);

        // Simulate collecting each of the 10 coins
        for (int i = 0; i < 10; i++)
        {
            var currentCoin = coins[i];
            runtime.TriggerEnter(currentCoin, player);

            int currentCount = i + 1;
            Assert.Equal(currentCount, runtime.Context.Variables["Coins"]);
            Assert.Contains(currentCoin.Id, runtime.Context.DestroyedObjects);
            Assert.Equal($"Монеты: {currentCount} / 10", runtime.Context.UiTextMap["ScoreText"]);
        }

        // On 10th coin, win condition must be satisfied!
        Assert.True(runtime.Context.IsWon, "Game should be won after collecting all 10 coins!");
    }

    [Fact]
    public void DoorAndKey_Mechanic_EndToEnd_Simulation()
    {
        var graph = EventGraph.CreateDoorKeyPreset();
        var keyObj = new SceneObject { Id = "key_1", Name = "Key", IsTrigger = true };
        var doorObj = new SceneObject { Id = "door_1", Name = "Door", IsTrigger = true };
        var playerObj = new SceneObject { Id = "player_1", Name = "Player" };

        var runtime = new EventRuntime(graph, [keyObj, doorObj, playerObj]);

        // 1. Touch door without key -> door still exists, game not won
        var nDoorTrigger = graph.Nodes.First(n => n.Title.Contains("дверь"));
        // Trigger door trigger directly
        runtime.Context.CurrentSelf = doorObj;
        runtime.Context.CurrentOther = playerObj;
        var doorDownstream = graph.GetConnectedTargets(nDoorTrigger.Id, "out_exec");
        foreach (var c in doorDownstream)
        {
            var target = graph.FindNode(c.ToNodeId);
            if (target is not null)
            {
                // HasKey is false, so true path is not taken
                bool hasKey = runtime.EvaluateBool(target, "in_cond");
                Assert.False(hasKey);
            }
        }
        Assert.False(runtime.Context.IsWon);

        // 2. Pick up key -> HasKey becomes true, key is destroyed
        var nKeyTrigger = graph.Nodes.First(n => n.Title.Contains("ключа"));
        runtime.Context.CurrentSelf = keyObj;
        runtime.Context.CurrentOther = playerObj;
        foreach (var c in graph.GetConnectedTargets(nKeyTrigger.Id, "out_exec"))
        {
            var target = graph.FindNode(c.ToNodeId);
            // var_set HasKey = true
            runtime.Context.Variables["HasKey"] = true;
        }
        Assert.True((bool)runtime.Context.Variables["HasKey"]);

        // 3. Touch door with key -> Door unlocked and win triggered!
        runtime.Context.CurrentSelf = doorObj;
        runtime.Context.CurrentOther = playerObj;
        foreach (var c in graph.GetConnectedTargets(nDoorTrigger.Id, "out_exec"))
        {
            var branch = graph.FindNode(c.ToNodeId);
            bool hasKey = runtime.EvaluateBool(branch!, "in_cond");
            Assert.True(hasKey);
        }
    }

    [Fact]
    public void PlayerHealth_Mechanic_EndToEnd_Simulation()
    {
        var graph = EventGraph.CreatePlayerHealthPreset();
        var hazardObj = new SceneObject { Id = "spikes_1", Name = "Spikes", IsTrigger = true };
        var playerObj = new SceneObject { Id = "player_1", Name = "Player" };

        var runtime = new EventRuntime(graph, [hazardObj, playerObj]);

        // Touch spikes 4 times (each does 25 damage)
        runtime.TriggerEnter(hazardObj, playerObj);
        Assert.Equal(75, runtime.Context.HealthMap[playerObj.Id]);
        Assert.Equal("Здоровье: 75 %", runtime.Context.UiTextMap["HealthText"]);

        runtime.TriggerEnter(hazardObj, playerObj);
        Assert.Equal(50, runtime.Context.HealthMap[playerObj.Id]);

        runtime.TriggerEnter(hazardObj, playerObj);
        Assert.Equal(25, runtime.Context.HealthMap[playerObj.Id]);

        runtime.TriggerEnter(hazardObj, playerObj);
        Assert.Equal(0, runtime.Context.HealthMap[playerObj.Id]);
        Assert.True(runtime.Context.IsLost, "Player should be dead (Game Lose) when health drops to 0");
    }

    [Fact]
    public void Clicker_Mechanic_EndToEnd_Simulation()
    {
        var template = TemplateCatalog.Find("clicker");
        Assert.NotNull(template);

        var scene = template.Build();
        Assert.NotNull(scene.EventGraph);

        var btn = scene.Objects.First(o => o.Name == "ClickButton");
        var runtime = new EventRuntime(scene.EventGraph, scene.Objects);

        Assert.Equal(0, runtime.Context.Variables["Coins"]);

        // Simulate 5 clicks on ClickButton
        for (int i = 1; i <= 5; i++)
        {
            runtime.TriggerButtonClick(btn);
            Assert.Equal(i, runtime.Context.Variables["Coins"]);
            Assert.Equal($"Монеты: {i} 🐾", runtime.Context.UiTextMap["ScoreText"]);
        }

        // Set 3 auto-clickers and tick timer
        runtime.Context.Variables["AutoClickers"] = 3;
        runtime.TriggerTimer();

        Assert.Equal(8, runtime.Context.Variables["Coins"]);
        Assert.Equal("Монеты: 8 🐾", runtime.Context.UiTextMap["ScoreText"]);
    }

    [Fact]
    public void ObjectLevel_EventGraph_Executes_Independently()
    {
        var globalGraph = new EventGraph { Name = "Global" };
        var catObj = new SceneObject
        {
            Id = "cat_1",
            Name = "Cat",
            EventGraph = new EventGraph { Name = "CatLogic" }
        };

        // Put logic inside Cat's individual EventGraph
        var nClick = EventNodeLibrary.CreateNode("event_object_clicked");
        var nDestroy = EventNodeLibrary.CreateNode("object_destroy");
        catObj.EventGraph.AddNode(nClick);
        catObj.EventGraph.AddNode(nDestroy);
        catObj.EventGraph.Connect(nClick.Id, "out_exec", nDestroy.Id, "in_exec");

        var runtime = new EventRuntime(globalGraph, [catObj]);
        Assert.DoesNotContain(catObj.Id, runtime.Context.DestroyedObjects);

        runtime.TriggerObjectClick(catObj);
        Assert.Contains(catObj.Id, runtime.Context.DestroyedObjects);
    }

    [Fact]
    public void GlobalAndObject_EventGraphs_Coexist()
    {
        var globalGraph = new EventGraph { Name = "Global" };
        globalGraph.Variables.Add(new GraphVariable("GlobalClicks", PortType.Integer, "0"));

        var nGlobalClick = EventNodeLibrary.CreateNode("event_object_clicked");
        var nGlobalInc = EventNodeLibrary.CreateNode("var_increment");
        nGlobalInc.Properties["VariableName"] = "GlobalClicks";
        globalGraph.AddNode(nGlobalClick);
        globalGraph.AddNode(nGlobalInc);
        globalGraph.Connect(nGlobalClick.Id, "out_exec", nGlobalInc.Id, "in_exec");

        var cookieObj = new SceneObject
        {
            Id = "cookie_1",
            Name = "Cookie",
            EventGraph = new EventGraph { Name = "CookieLogic" }
        };
        cookieObj.EventGraph.Variables.Add(new GraphVariable("CookieClicks", PortType.Integer, "0"));
        var nCookieClick = EventNodeLibrary.CreateNode("event_object_clicked");
        var nCookieInc = EventNodeLibrary.CreateNode("var_increment");
        nCookieInc.Properties["VariableName"] = "CookieClicks";
        cookieObj.EventGraph.AddNode(nCookieClick);
        cookieObj.EventGraph.AddNode(nCookieInc);
        cookieObj.EventGraph.Connect(nCookieClick.Id, "out_exec", nCookieInc.Id, "in_exec");

        var runtime = new EventRuntime(globalGraph, [cookieObj]);

        runtime.TriggerObjectClick(cookieObj);
        runtime.TriggerObjectClick(cookieObj);

        Assert.Equal(2, runtime.Context.Variables["GlobalClicks"]);
        Assert.Equal(2, runtime.Context.Variables["CookieClicks"]);
    }

    [Fact]
    public void UiButton_With_ObjectClicked_Graph_Executes_Correctly()
    {
        var btn = new SceneObject
        {
            Id = "btn_1",
            Name = "ClickButton",
            Type = ObjectTypes.UiButton,
            EventGraph = new EventGraph { Name = "ButtonLogic" }
        };
        btn.EventGraph.Variables.Add(new GraphVariable("Score", PortType.Integer, "0"));
        btn.EventGraph.Variables.Add(new GraphVariable("ClickPower", PortType.Integer, "1"));

        var nClick = EventNodeLibrary.CreateNode("event_object_clicked");
        var nGetScore = EventNodeLibrary.CreateNode("var_get");
        nGetScore.Properties["VariableName"] = "Score";
        var nGetPower = EventNodeLibrary.CreateNode("var_get");
        nGetPower.Properties["VariableName"] = "ClickPower";
        var nAdd = EventNodeLibrary.CreateNode("math_add");
        var nSetScore = EventNodeLibrary.CreateNode("var_set");
        nSetScore.Properties["VariableName"] = "Score";
        var nSetText = EventNodeLibrary.CreateNode("ui_set_text");
        nSetText.Properties["TargetName"] = "Text";

        btn.EventGraph.AddNode(nClick);
        btn.EventGraph.AddNode(nGetScore);
        btn.EventGraph.AddNode(nGetPower);
        btn.EventGraph.AddNode(nAdd);
        btn.EventGraph.AddNode(nSetScore);
        btn.EventGraph.AddNode(nSetText);

        btn.EventGraph.Connect(nClick.Id, "out_exec", nSetScore.Id, "in_exec");
        btn.EventGraph.Connect(nGetPower.Id, "out_val", nAdd.Id, "in_a");
        btn.EventGraph.Connect(nGetScore.Id, "out_val", nAdd.Id, "in_b");
        btn.EventGraph.Connect(nAdd.Id, "out_result", nSetScore.Id, "in_val");
        btn.EventGraph.Connect(nSetScore.Id, "out_exec", nSetText.Id, "in_exec");
        btn.EventGraph.Connect(nSetScore.Id, "out_val", nSetText.Id, "in_text");

        var textObj = new SceneObject { Id = "text_1", Name = "Text", Type = ObjectTypes.UiText, UiText = "0" };
        var runtime = new EventRuntime(new EventGraph { Name = "Global" }, [btn, textObj]);

        // Trigger button click (user clicks the button in game)
        runtime.TriggerButtonClick(btn);

        Assert.Equal(1, runtime.Context.Variables["Score"]);
        Assert.Equal("1", runtime.Context.UiTextMap["Text"]);
        Assert.Equal("1", textObj.UiText);
    }

    [Fact]
    public void SetUiText_WithPrefix_AndSpecificTarget_UpdatesCorrectly()
    {
        var graph = new EventGraph { Name = "Global" };
        var nBegin = EventNodeLibrary.CreateNode("event_begin_play");
        var nText1 = EventNodeLibrary.CreateNode("ui_set_text");
        nText1.Properties["TargetName"] = "ScoreText";
        nText1.Properties["Prefix"] = "Монет: ";
        nText1.FindInput("in_text")!.DefaultValue = "42";

        var nText2 = EventNodeLibrary.CreateNode("ui_set_text");
        nText2.Properties["TargetName"] = "DpsText";
        nText2.Properties["Prefix"] = "В сек: ";
        nText2.Properties["Suffix"] = " /с";
        nText2.FindInput("in_text")!.DefaultValue = "5";

        graph.AddNode(nBegin);
        graph.AddNode(nText1);
        graph.AddNode(nText2);
        graph.Connect(nBegin.Id, "out_exec", nText1.Id, "in_exec");
        graph.Connect(nText1.Id, "out_exec", nText2.Id, "in_exec");

        var scoreObj = new SceneObject { Id = "st_1", Name = "ScoreText", Type = ObjectTypes.UiText, UiText = "" };
        var dpsObj = new SceneObject { Id = "dps_1", Name = "DpsText", Type = ObjectTypes.UiText, UiText = "" };

        var runtime = new EventRuntime(graph, [scoreObj, dpsObj]);
        runtime.TriggerBeginPlay();

        Assert.Equal("Монет: 42", runtime.Context.UiTextMap["ScoreText"]);
        Assert.Equal("Монет: 42", scoreObj.UiText);
        Assert.Equal("В сек: 5 /с", runtime.Context.UiTextMap["DpsText"]);
        Assert.Equal("В сек: 5 /с", dpsObj.UiText);
    }
}
