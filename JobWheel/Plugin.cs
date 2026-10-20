using System;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace JobWheel;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/jobwheel";

    [PluginService] private static IDalamudPluginInterface PluginInterface { get; set; } = null!;
    [PluginService] private static ICommandManager CommandManager { get; set; } = null!;
    [PluginService] private static IChatGui ChatGui { get; set; } = null!;
    [PluginService] private static IDutyState DutyState { get; set; } = null!;
    [PluginService] private static IObjectTable ObjectTable { get; set; } = null!;
    [PluginService] private static ICondition Condition { get; set; } = null!;

    private readonly Configuration config;
    private readonly WheelService service;
    private readonly WindowSystem windowSystem = new("JobWheel");
    private readonly WheelWindow window;
    private readonly IDutyState.DutyStartedDelegate dutyStartedHandler;
    private readonly IDutyState.DutyCompletedDelegate dutyCompletedHandler;

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        service = new WheelService(config, () => PluginInterface.SavePluginConfig(config), ChatGui, ObjectTable, Condition);

        window = new WheelWindow(service);
        windowSystem.AddWindow(window);

        CommandManager.AddHandler(CommandName, new CommandInfo((_, _) => window.Toggle())
        {
            HelpMessage = "Toggle the Job Wheel window",
        });

        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += window.Toggle;

        dutyStartedHandler = _ => service.OnDutyStarted();
        dutyCompletedHandler = _ => service.OnDutyCompleted();
        DutyState.DutyStarted += dutyStartedHandler;
        DutyState.DutyCompleted += dutyCompletedHandler;
    }

    public void Dispose()
    {
        DutyState.DutyStarted -= dutyStartedHandler;
        DutyState.DutyCompleted -= dutyCompletedHandler;
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= window.Toggle;
        windowSystem.RemoveAllWindows();
        CommandManager.RemoveHandler(CommandName);
    }
}
