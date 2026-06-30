using clib.ImGuiHelpers;
using clib.Ui;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Enums;
using System.Globalization;
using System.Text;

namespace ComplexTweaks.Tweaks;

public class FateToolKitWindow(FateToolKit tweak) : MinimisableWindow($"Fate Tracker##{nameof(FateToolKitWindow)}") {
    protected override Vector2 MinimisedSize => new(700, 90);

    public override bool DrawConditions() => IObjectTable.Get().LocalPlayer.Available;

    protected override void DrawContent(bool minimised) {
        tweak.SyncRunningState();

        var itemProgress = tweak.GetCurrentMode().GetItemProgress(tweak)?.ToList();

        using (var rounding = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 6f))
        using (var runButtonColor = ImRaii.PushColor(ImGuiCol.Button, tweak.Running ? (uint)Colors.Negative : (uint)Colors.Positive)
            .Push(ImGuiCol.ButtonHovered, tweak.Running ? (uint)Colors.NegativeHover : (uint)Colors.PositiveHover)
            .Push(ImGuiCol.ButtonActive, tweak.Running ? (uint)Colors.NegativeActive : (uint)Colors.PositiveActive)) {

            if (ImGui.Button(tweak.Running ? (tweak.PendingStopWhenSafe ? "Stopping" : "Stop") : "Start")) {
                if (tweak.Running) {
                    if (ImGui.GetIO().KeyCtrl) {
                        tweak.PendingStopWhenSafe = true;
                    }
                    else {
                        tweak.ToggleRunning();
                        NavmeshIPC.Get().Stop();
                    }
                }
                else {
                    tweak.ToggleRunning();
                }
            }
            ImGui.TooltipOnHover(tweak.Running, $"Stop. Ctrl+{SeIconChar.MouseLeftClick.ToIconString()} soft stop");

            ImGui.SameLine();
            DrawHeaderChip(
                $"Automation: {(tweak.Running ? Svc.Automation.Status : "Stopped")}",
                tweak.Running ? Colors.ChipGold : Colors.ChipMuted,
                Colors.Grey2
            );

            ImGui.SameLine();
            DrawHeaderChip(
                $"State: {tweak.CurrentState}",
                tweak.Running && !tweak.CurrentState.Equals("Idle", StringComparison.OrdinalIgnoreCase) ? Colors.ChipGold : Colors.ChipMuted,
                Colors.Grey2
            );

            ImGui.SameLine();
            DrawHeaderChip($"Completed: {tweak.CompletedCount}", Colors.ChipInfo, Colors.Grey2);

            if (tweak.RemainingUntilCompleted is { } remaining && remaining > 0) {
                ImGui.SameLine();
                DrawHeaderChip($"Remaining: {remaining}", Colors.ChipInfo, Colors.Grey2);
            }

            var modeRemaining = tweak.GetCurrentMode().GetRemainingDisplay(tweak);
            if (!string.IsNullOrEmpty(modeRemaining)) {
                ImGui.SameLine();
                var (bg, fg) = modeRemaining.Equals("Done", StringComparison.OrdinalIgnoreCase) ? (Colors.ChipMuted, Colors.Grey2) : (Colors.ChipInfo, Colors.Grey2);
                DrawHeaderChip(modeRemaining, bg, fg);
                if (itemProgress is { Count: > 0 })
                    ImGui.TooltipOnHover(BuildItemProgressTooltip(itemProgress));
            }

            ImGui.SameLine();
            var style = ImGui.GetStyle();
            var rightButtonWidth = (ImGui.GetFrameHeight() + style.FramePadding.X * 2f) * 2f + style.ItemSpacing.X;
            var leftRightGap = style.ItemSpacing.X;
            var leftContentRight = ImGui.GetItemRectMax().X;
            if (Math.Max(0f, ImGui.GetContentRegionAvail().X - rightButtonWidth) is > 0 and var spacer) {
                ImGui.Dummy(new Vector2(spacer, 0f));
                ImGui.SameLine();
            }
            DrawModeButton();
            ImGui.SameLine();
            using (var _ = ImRaii.Disabled(tweak.ModeSuppliesSwapZones))
            using (var zoneButtonColor = ImRaii.PushColor(ImGuiCol.Text, tweak.HasSelectedSwapZones ? (uint)Color.Gold : ImGui.GetColorU32(ImGuiCol.Text))) {
                if (ImGuiComponents.IconButton("###ZoneSelector", FontAwesomeIcon.Globe)) {
                    TerritorySelectWindow.Show(tweak.SelectedSwapZones, new() {
                        Filter = row => row.IsInUse && row.TerritoryIntendedUse.Value.StructsEnum is TerritoryIntendedUse.Overworld && !row.IsPvpZone,
                        Columns = TerritorySelectColumn.All & ~TerritorySelectColumn.Duty & ~TerritorySelectColumn.IntendedUse,
                    });
                }
            }
            if (tweak.ModeSuppliesSwapZones)
                ImGui.TooltipOnHover(ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled), "Zone list is defined by the current grind mode. Switch to None to select zones manually.");
            else if (tweak.HasSelectedSwapZones)
                ImGui.TooltipOnHover($"Swap Zones: {tweak.SelectedSwapZones.Count}");
            else
                ImGui.TooltipOnHover("Swap Zones (uses default swap behaviour if none selected)");

            if (minimised) {
                MinimisedContentWidth = Math.Max(400, leftContentRight - ImGui.GetWindowPos().X + leftRightGap + rightButtonWidth + style.WindowPadding.X * 2);
            }
        }

        if (minimised)
            return;

        ImGui.SpacedSeparator();

        if (itemProgress is { Count: > 0 }) {
            DrawItemProgressTable(itemProgress);
            ImGui.SpacedSeparator();
        }

        if (tweak.GetOrderedFates().ToList() is not { Count: > 0 } fates) {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "No fates match the current filters.");
            return;
        }

        foreach (var (fate, isAvailable) in fates) {
            using var id = ImRaii.PushId($"fate_{fate.Id}");

            var availableWidth = ImGui.GetContentRegionAvail().X;
            var displayName = FormatDisplayName(fate);
            var nameWidth = Math.Min(200f.Scaled(), availableWidth * 0.4f);
            var progressWidth = Math.Max(1f, availableWidth - nameWidth - ImGui.GetStyle().ItemSpacing.X);

            using (var buttonStyle = ImRaii.PushStyle(ImGuiStyleVar.ButtonTextAlign, new Vector2(0, 0.5f)))
            using (var color = ImRaii.PushColor(ImGuiCol.Button, 0).Push(ImGuiCol.ButtonHovered, ImGui.GetColorU32(ImGuiCol.ButtonHovered)).Push(ImGuiCol.ButtonActive, ImGui.GetColorU32(ImGuiCol.ButtonActive))) {
                if (fate.HasBonus) {
                    ImGui.Image(ITextureProvider.Get().GetFromGameIcon(new Dalamud.Interface.Textures.GameIconLookup(65001)).GetWrapOrEmpty().Handle, new Vector2(ImGui.IconUnitHeight()));
                    ImGui.SameLine(0f, 0f);
                }

                using (var nameCol = ImRaii.PushColor(ImGuiCol.Text, isAvailable ? (uint)Color.White : Colors.Grey3)) {
                    if (ImGui.Button(displayName, new Vector2(
                        fate.HasBonus
                            ? Math.Max(1f, nameWidth - ImGui.IconUnitWidth())
                            : nameWidth,
                        0
                    ))) {
                        if (NavmeshIPC.Get().IsRunning())
                            NavmeshIPC.Get().Stop();
                        else
                            NavmeshIPC.Get().PathfindAndMoveTo(fate.Position.RandomPoint(fate.Radius * 0.5f).OnMesh(), ICondition.Get()[ConditionFlag.InFlight]);
                    }
                }

                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    tweak.ToggleBlacklist(fate);

                ImGui.TooltipOnHover(BuildFateTooltip(fate, displayName));
            }

            ImGui.SameLine();

            var percentage = fate.Progress / 100f;
            var progressLabel = $"{fate.Progress}%";

            var cursorPos = ImGui.GetCursorPos();
            var labelSize = ImGui.CalcTextSize(progressLabel);
            var textX = Math.Max(0f, progressWidth - labelSize.X - 4f);

            using (var color = ImRaii.PushColor(ImGuiCol.PlotHistogram, tweak.Config.BarColour))
                ImGui.ProgressBar(percentage, new Vector2(progressWidth, ImGui.GetFrameHeight()), "");

            ImGui.SetCursorPos(new Vector2(cursorPos.X + textX, cursorPos.Y + (ImGui.GetFrameHeight() - labelSize.Y) * 0.5f));
            ImGui.TextColored(
                ImGui.GetProgressBarTextColor(tweak.Config.BarColour, ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg], percentage, textX, labelSize.X, progressWidth),
                progressLabel
            );

            ImGui.SpacedSeparator();
        }
    }

    private void DrawModeButton() {
        if (ImGuiComponents.IconButton("###GrindMode", FontAwesomeIcon.List))
            ImGui.OpenPopup("###GrindModePopup");
        ImGui.TooltipOnHover($"Grind mode: {tweak.GetCurrentMode().DisplayName}\nEXPERIMENTAL (didn't get to test non-gemstones)");

        using var popup = ImRaii.Popup("###GrindModePopup");
        if (popup) {
            foreach (var mode in FateGrindModes.All) {
                if (ImGui.Selectable(mode.DisplayName, mode.DisplayName == tweak.SelectedModeId)) {
                    tweak.SelectedModeId = mode.DisplayName;
                    ImGui.CloseCurrentPopup();
                }
            }
        }
    }

    private static void DrawHeaderChip(string text, Color background, Color textColor) {
        using var chipColor = ImRaii.PushColor(ImGuiCol.Button, (uint)background)
            .Push(ImGuiCol.ButtonHovered, (uint)background)
            .Push(ImGuiCol.ButtonActive, (uint)background)
            .Push(ImGuiCol.Text, (uint)textColor);
        ImGui.Button(text);
    }

    private static readonly Vector4 CompleteColor = new(0.45f, 0.85f, 0.45f, 1f);

    private void DrawItemProgressTable(List<ItemTargetProgress> progress) {
        var completeCount = progress.Count(p => p.IsComplete);
        using var node = ImRaii.TreeNode($"Per-type progress  ({completeCount}/{progress.Count} complete)###ItemProgress", ImGuiTreeNodeFlags.DefaultOpen);
        if (!node)
            return;

        ImGui.TextWrapped("Edit \"Target\" to set how many of each you want (default is 10, enough atma for every relic; reset with the undo button). When a row is complete its zone is skipped during farming.");

        using var table = ImRaii.Table("###ItemProgressTable", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp);
        if (!table)
            return;

        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Zone", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("In bags", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("Target", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("Left", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableHeadersRow();

        foreach (var p in progress) {
            using var id = ImRaii.PushId((int)p.ItemId);
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            if (p.IsComplete)
                ImGui.TextColored(CompleteColor, ItemName(p.ItemId));
            else
                ImGui.TextV(ItemName(p.ItemId));

            ImGui.TableNextColumn();
            ImGui.TextV(ZoneNames(p.TerritoryIds));

            ImGui.TableNextColumn();
            ImGui.TextV(p.Owned.ToString());

            ImGui.TableNextColumn();
            var target = p.Required;
            ImGui.SetNextItemWidth(70f.Scaled());
            if (ImGui.InputInt("###target", ref target, 0))
                SetTarget(p.ItemId, target);
            if (tweak.Config.ItemTarget.ContainsKey(p.ItemId)) {
                ImGui.SameLine();
                if (ImGuiComponents.IconButton("###resetTarget", FontAwesomeIcon.Undo))
                    ResetTarget(p.ItemId);
                ImGui.TooltipOnHover("Reset to default target");
            }

            ImGui.TableNextColumn();
            if (p.IsComplete)
                ImGui.TextColored(CompleteColor, "Done");
            else
                ImGui.TextV(p.Remaining.ToString());
        }
    }

    private void SetTarget(uint itemId, int value) {
        tweak.Config.ItemTarget[itemId] = Math.Max(0, value);
        tweak.RefreshZoneItemTargets();
    }

    private void ResetTarget(uint itemId) {
        tweak.Config.ItemTarget.Remove(itemId);
        tweak.RefreshZoneItemTargets();
    }

    private static string BuildItemProgressTooltip(List<ItemTargetProgress> progress) {
        var sb = new StringBuilder();
        foreach (var p in progress) {
            var status = p.IsComplete ? "done" : $"{p.Remaining} left";
            sb.AppendLine($"{ItemName(p.ItemId)} ({ZoneNames(p.TerritoryIds)}): {p.Owned}/{p.Required} — {status}");
        }
        return sb.ToString().TrimEnd();
    }

    private static string ItemName(uint itemId) {
        var name = Sheets.Item.GetRow(itemId).Name.ToString();
        return string.IsNullOrEmpty(name) ? $"Item {itemId}" : name;
    }

    private static string ZoneNames(IReadOnlyList<uint> territoryIds)
        => territoryIds.Count == 0 ? "?" : string.Join(", ", territoryIds.Select(ZoneName));

    private static string ZoneName(uint territoryId) {
        var name = Sheets.TerritoryType.GetRow(territoryId).PlaceName.Value.Name.ToString();
        return string.IsNullOrEmpty(name) ? $"#{territoryId}" : name;
    }

    private string BuildFateTooltip(PublicEvent fate, string displayName) {
        var sb = new StringBuilder();
        var player = IObjectTable.Get().LocalPlayer;
        var time = fate.TimeRemaining >= 0 ? TimeSpan.FromSeconds(fate.TimeRemaining).ToString(@"mm\:ss") : "∞";
        var distance = player?.DistanceTo(fate.Position).ToString("F1", CultureInfo.InvariantCulture) ?? "?";

        sb.AppendLine(displayName);
        sb.AppendLine($"Rule: {fate.Rule}");
        sb.AppendLine($"State: {fate.State}");
        sb.AppendLine($"Progress: {fate.Progress}%");
        sb.AppendLine($"Time: {time}");
        sb.AppendLine($"Distance: {distance}");

        var (isEligible, failedConditions) = tweak.GetFateConditionDetails(fate);
        sb.AppendLine($"Will be automated? {isEligible}");
        if (failedConditions.Count > 0) {
            sb.AppendLine("Blocked by:");
            foreach (var reason in failedConditions)
                sb.AppendLine($" - {reason}");
        }

        return sb.ToString().TrimEnd();
    }

    public string FormatDisplayName(PublicEvent fate) => tweak.Config.DisplayNameFormat
        .Replace("{Level}", fate.Level.ToString())
        .Replace("{Name}", fate.Name)
        .Replace("{Id}", fate.Id.ToString())
        .Replace("{Progress}", fate.Progress.ToString())
        .Replace("{TimeRemaining}", fate.TimeRemaining >= 0 ? TimeSpan.FromSeconds(fate.TimeRemaining).ToString(@"mm\:ss") : "∞")
        .Replace("{Distance}", IObjectTable.Get().LocalPlayer?.DistanceTo(fate.Position).ToString("F1") ?? "?")
        .Replace("{State}", fate.State.ToString());
}
