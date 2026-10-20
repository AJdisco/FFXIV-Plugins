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
public class SubmarineRecord
{
    public string Name { get; set; } = string.Empty;
    public byte Rank { get; set; }
    public long ReturnUnix { get; set; }   // 0 = no voyage info
    public long SeenUnix { get; set; }     // when we last read it from the game
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public List<uint> TrackedItemIds { get; set; } = new();
    public List<SubmarineRecord> Submarines { get; set; } = new();
}

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/subprofits";

    [PluginService] private static IDalamudPluginInterface PluginInterface { get; set; } = null!;
    [PluginService] private static ICommandManager CommandManager { get; set; } = null!;
    [PluginService] private static IDataManager DataManager { get; set; } = null!;
    [PluginService] private static IContextMenu ContextMenu { get; set; } = null!;
    [PluginService] private static IFramework Framework { get; set; } = null!;

    private readonly Configuration config;
    private readonly WindowSystem windowSystem = new("SubProfits");
    private readonly MainWindow mainWindow;
    private readonly SubmarineTracker tracker;

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Action save = () => PluginInterface.SavePluginConfig(config);

        tracker = new SubmarineTracker(config, save);
        mainWindow = new MainWindow(DataManager, config, save);
        windowSystem.AddWindow(mainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo((_, _) => mainWindow.Toggle())
        {
            HelpMessage = "Toggle the Sub Profits window",
        });

        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += mainWindow.Toggle;
        ContextMenu.OnMenuOpened += OnContextMenuOpened;
        Framework.Update += OnFrameworkUpdate;
    }

    // Runs even when the window is closed, so timers are captured whenever you visit the workshop.
    private void OnFrameworkUpdate(IFramework framework) => tracker.Poll();

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
        Framework.Update -= OnFrameworkUpdate;
        ContextMenu.OnMenuOpened -= OnContextMenuOpened;
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= mainWindow.Toggle;
        windowSystem.RemoveAllWindows();
        CommandManager.RemoveHandler(CommandName);
    }
}

/// <summary>
/// Reads submarine data from the game while you are inside your FC workshop and caches it
/// in the plugin config, so timers keep counting down after you leave.
/// </summary>
public sealed unsafe class SubmarineTracker
{
    private readonly Configuration config;
    private readonly Action save;
    private DateTime nextPoll = DateTime.MinValue;

    public SubmarineTracker(Configuration config, Action save)
    {
        this.config = config;
        this.save = save;
    }

    public void Poll()
    {
        var now = DateTime.UtcNow;
        if (now < nextPoll) return;
        nextPoll = now.AddSeconds(1);

        var housing = HousingManager.Instance();
        if (housing == null || !housing->IsInWorkshop()) return;

        var workshop = housing->WorkshopTerritory;
        if (workshop == null) return;

        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var changed = false;
        var data = workshop->Submersible.Data;

        for (var i = 0; i < data.Length; i++)
        {
            ref var sub = ref data[i];
            var name = sub.NameString;
            if (sub.RankId == 0 || string.IsNullOrWhiteSpace(name))
                continue; // empty slot

            var record = config.Submarines.Find(s => s.Name == name);
            if (record == null)
            {
                record = new SubmarineRecord { Name = name };
                config.Submarines.Add(record);
                changed = true;
            }

            if (record.ReturnUnix != sub.ReturnTime || record.Rank != sub.RankId)
            {
                record.ReturnUnix = sub.ReturnTime;
                record.Rank = sub.RankId;
                changed = true;
            }

            record.SeenUnix = nowUnix; // in memory only; saved next time something changes
        }

        if (changed) save();
    }
}

public sealed unsafe class MainWindow : Window
{
    private static readonly Vector4 Green = new(0.45f, 1f, 0.45f, 1f);
    private static readonly Vector4 Grey = new(0.7f, 0.7f, 0.7f, 1f);

    private readonly IDataManager data;
    private readonly Configuration config;
    private readonly Action save;

    public MainWindow(IDataManager data, Configuration config, Action save) : base("Sub Profits")
    {
        this.data = data;
        this.config = config;
        this.save = save;
        Size = new Vector2(400, 340);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    private static void TextRight(string text, Vector4? color = null)
    {
        var spare = ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(text).X;
        if (spare > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + spare);
        if (color is { } c) ImGui.TextColored(c, text);
        else ImGui.TextUnformatted(text);
    }

    private static string FormatRemaining(long seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        if (t.TotalDays >= 1) return $"{(int)t.TotalDays}d {t.Hours}h {t.Minutes:00}m";
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes:00}m";
        return $"{t.Minutes}m {t.Seconds:00}s";
    }

    public override void Draw()
    {
        DrawProfits();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        DrawSubmarines();
    }

    private void DrawProfits()
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

    private void DrawSubmarines()
    {
        ImGui.TextUnformatted("Submarines");

        if (config.Submarines.Count == 0)
        {
            ImGui.TextWrapped("No submarine data yet. Enter your FC workshop once to load it.");
            return;
        }

        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string? forget = null;

        if (ImGui.BeginTable("subs", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            var pad = ImGui.GetStyle().CellPadding.X * 2;
            ImGui.TableSetupColumn("Submarine", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Returns in", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("99d 23h 59m").X + pad);
            ImGui.TableSetupColumn("At", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("Wed 23:59").X + pad);
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("X").X + pad + ImGui.GetStyle().FramePadding.X * 2);
            ImGui.TableHeadersRow();

            foreach (var s in config.Submarines)
            {
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(s.Name);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip($"Rank {s.Rank}");

                var remaining = s.ReturnUnix - nowUnix;

                ImGui.TableNextColumn();
                if (s.ReturnUnix == 0)
                    TextRight("No voyage", Grey);
                else if (remaining <= 0)
                    TextRight("Returned", Green);
                else
                    TextRight(FormatRemaining(remaining));

                ImGui.TableNextColumn();
                if (s.ReturnUnix != 0 && remaining > 0)
                {
                    var local = DateTimeOffset.FromUnixTimeSeconds(s.ReturnUnix).ToLocalTime();
                    TextRight(local.ToString("ddd HH:mm"));
                }

                ImGui.TableNextColumn();
                if (ImGui.SmallButton($"X##sub{s.Name}")) forget = s.Name;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Forget this submarine");
            }

            ImGui.EndTable();
        }

        if (forget != null)
        {
            config.Submarines.RemoveAll(s => s.Name == forget);
            save();
        }

        ImGui.TextColored(Grey, "Times update when you visit your workshop.");
    }
}
