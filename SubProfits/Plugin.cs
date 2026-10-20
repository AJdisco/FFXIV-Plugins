using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Configuration;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Item = Lumina.Excel.Sheets.Item;

// NOTE: ImGui namespace depends on your Dalamud API level.
//   API 12 and older: using ImGuiNET;
//   API 13 and newer: using Dalamud.Bindings.ImGui;
using Dalamud.Bindings.ImGui;

namespace SubProfits;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public List<uint> TrackedItemIds { get; set; } = new();
}

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/subprofits";

    [PluginService] private static IDalamudPluginInterface PluginInterface { get; set; } = null!;
    [PluginService] private static ICommandManager CommandManager { get; set; } = null!;
    [PluginService] private static IDataManager DataManager { get; set; } = null!;
    [PluginService] private static IContextMenu ContextMenu { get; set; } = null!;

    private readonly Configuration config;
    private readonly WindowSystem windowSystem = new("SubProfits");
    private readonly MainWindow mainWindow;

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        mainWindow = new MainWindow(DataManager, config, () => PluginInterface.SavePluginConfig(config));
        windowSystem.AddWindow(mainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo((_, _) => mainWindow.Toggle())
        {
            HelpMessage = "Toggle the Sub Profits window",
        });

        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += mainWindow.Toggle;
        ContextMenu.OnMenuOpened += OnContextMenuOpened;
    }

    private void OnContextMenuOpened(IMenuOpenedArgs args)
    {
        // Only react to right-clicks on inventory items.
        if (args.MenuType != ContextMenuType.Inventory) return;
        if (args.Target is not MenuTargetInventory { TargetItem: { } item }) return;

        var id = item.BaseItemId; // strips the HQ/collectable offset
        if (id == 0) return;

        var tracked = config.TrackedItemIds.Contains(id);

        args.AddMenuItem(new MenuItem
        {
            Name = tracked ? "Remove from Sub Profits" : "Add to Sub Profits",
            PrefixChar = 'G',
            PrefixColor = 45,
            OnClicked = _ =>
            {
                if (tracked) config.TrackedItemIds.Remove(id);
                else config.TrackedItemIds.Add(id);
                PluginInterface.SavePluginConfig(config);
            },
        });
    }

    public void Dispose()
    {
        ContextMenu.OnMenuOpened -= OnContextMenuOpened;
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= mainWindow.Toggle;
        windowSystem.RemoveAllWindows();
        CommandManager.RemoveHandler(CommandName);
    }
}

public sealed unsafe class MainWindow : Window
{
    private readonly IDataManager data;
    private readonly Configuration config;
    private readonly Action save;

    public MainWindow(IDataManager data, Configuration config, Action save) : base("Sub Profits")
    {
        this.data = data;
        this.config = config;
        this.save = save;
        Size = new Vector2(380, 220);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    private static void TextRight(string text)
    {
        var spare = ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(text).X;
        if (spare > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + spare);
        ImGui.TextUnformatted(text);
    }

    public override void Draw()
    {
        var inv = InventoryManager.Instance();
        if (inv == null)
        {
            ImGui.TextUnformatted("Inventory not available (are you logged in?)");
            return;
        }

        var itemSheet = data.GetExcelSheet<Item>();
        var currentGil = (long)inv->GetGil();
        long itemsValue = 0;
        uint? toRemove = null;

        if (config.TrackedItemIds.Count == 0)
            ImGui.TextUnformatted("No items tracked. Right-click an inventory item and choose \"Add to Sub Profits\".");

        if (ImGui.BeginTable("items", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            var pad = ImGui.GetStyle().CellPadding.X * 2;
            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("9,999").X + pad);
            ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("99,999").X + pad);
            ImGui.TableSetupColumn("Total", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("9,999,999").X + pad);
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("X").X + pad + ImGui.GetStyle().FramePadding.X * 2);
            ImGui.TableHeadersRow();

            foreach (var id in config.TrackedItemIds)
            {
                if (!itemSheet.TryGetRow(id, out var item))
                    continue;

                // Count NQ + HQ, ignoring equipped gear and armoury chest.
                var qty = inv->GetInventoryItemCount(id, false, false, false)
                        + inv->GetInventoryItemCount(id, true, false, false);

                // PriceLow = what a vendor pays you. PriceMid = what you pay.
                long sell = item.PriceLow;
                long total = qty * sell;
                itemsValue += total;

                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.TextUnformatted(item.Name.ToString());
                ImGui.TableNextColumn(); TextRight(qty.ToString("N0"));
                ImGui.TableNextColumn(); TextRight(sell.ToString("N0"));
                ImGui.TableNextColumn(); TextRight(total.ToString("N0"));
                ImGui.TableNextColumn();
                if (ImGui.SmallButton($"X##{id}")) toRemove = id;
            }

            ImGui.EndTable();
        }

        // Modify the list after iterating, never during.
        if (toRemove is { } rid)
        {
            config.TrackedItemIds.Remove(rid);
            save();
        }

        ImGui.Separator();
        ImGui.TextUnformatted($"Current gil:     {currentGil:N0}");
        ImGui.TextUnformatted($"Items (vendor):  {itemsValue:N0}");
        ImGui.TextUnformatted($"Combined:        {currentGil + itemsValue:N0}");
    }
}
