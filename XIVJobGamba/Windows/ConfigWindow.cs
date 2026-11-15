using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace XIVJobGamba.Windows;

internal sealed class ConfigWindow : Window
{
    private readonly Plugin plugin;

    public ConfigWindow(Plugin plugin)
        : base("XIV Job Gamba Settings###XIVJobGambaConfig")
    {
        this.plugin = plugin;
        AllowPinning = true;

        Size = new Vector2(560, 640);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 420),
            MaximumSize = new Vector2(900, 1400),
        };
    }

    public override void PreDraw()
    {
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.09f, 0.08f, 0.07f, 0.97f));
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.32f, 0.22f, 0.08f, 1f));
        ImGui.PushStyleColor(ImGuiCol.CheckMark, new Vector4(0.93f, 0.76f, 0.35f, 1f));
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, new Vector4(0.86f, 0.68f, 0.28f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.46f, 0.32f, 0.10f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.64f, 0.46f, 0.14f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.80f, 0.58f, 0.18f, 1f));
    }

    public override void PostDraw() => ImGui.PopStyleColor(7);

    public override void Draw()
    {
        var config = plugin.Configuration;
        var catalog = plugin.Gearsets.Current;

        ImGui.TextWrapped(
            "Each saved gearset is a slice of its own, so two sets for one job can both show up. " +
            "Turn on one slice per job to keep only the highest item level set for each job " +
            "(the lowest slot number wins a tie). The wheel draws only the slices the next spin can land on.");
        ImGui.Spacing();

        var cooldown = config.CooldownSpins;
        if (ImGui.SliderInt("Cooldown spins", ref cooldown, 0, SpinRules.MaxCooldown))
        {
            config.CooldownSpins = cooldown;
            config.Save();
        }

        ImGui.TextDisabled("A job equipped in the last this many spins is left off the wheel. 0 disables it. Saved in the plugin config.");

        var onePerJob = config.OneSlicePerJob;
        if (ImGui.Checkbox("One slice per job", ref onePerJob))
        {
            config.OneSlicePerJob = onePerJob;
            config.Save();
        }

        ImGui.TextDisabled("Off: every gearset is an entry. On: highest item level gearset for that job.");

        var includeCrafters = config.IncludeCraftersAndGatherers;
        if (ImGui.Checkbox("Include crafters and gatherers", ref includeCrafters))
        {
            config.IncludeCraftersAndGatherers = includeCrafters;
            config.Save();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Spin history");
        ImGui.TextDisabled($"{config.RecentJobIds.Count} successful pick{(config.RecentJobIds.Count == 1 ? "" : "s")} stored. Failed equips are not recorded.");

        if (config.RecentJobIds.Count == 0)
        {
            ImGui.TextDisabled("No picks yet.");
        }
        else
        {
            var recent = DescribeHistory(config.RecentJobIds, catalog);
            ImGui.TextWrapped("Oldest to newest: " + recent);
        }

        using (ImRaii.Disabled(config.RecentJobIds.Count == 0))
        {
            if (ImGui.Button("Clear history"))
                config.ClearHistory();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Blacklist");
        ImGui.TextWrapped(
            "Checked gearsets never appear on the wheel. An exclusion is the gearset slot plus its exact name, " +
            "so renaming the set (or saving a different set into that slot) lets it back in.");

        if (catalog.State == Services.GearsetReadState.LoggedOut)
        {
            ImGui.TextDisabled("Log in to see your gearsets. Saved exclusions are kept.");
        }
        else if (catalog.State == Services.GearsetReadState.Failed)
        {
            ImGui.TextWrapped("Gearsets couldn't be read. Check /xllog.");
        }
        else if (catalog.Gearsets.Count == 0)
        {
            ImGui.TextDisabled("No saved gearsets.");
        }
        else
        {
            var scale = ImGuiHelpers.GlobalScale;
            using var child = ImRaii.Child("gamba-blacklist", new Vector2(0f, 240f * scale), true);
            if (child.Success)
            {
                foreach (var gearset in catalog.Gearsets)
                {
                    ImGui.PushID(gearset.GearsetId);
                    var excluded = config.IsBlacklisted(gearset.BlacklistKey);
                    var label = $"#{gearset.GearsetId}   {gearset.JobAbbreviation}   {gearset.GearsetName}   (iLv {gearset.ItemLevel})";
                    if (ImGui.Checkbox(label, ref excluded))
                        config.SetBlacklisted(gearset.BlacklistKey, excluded);

                    ImGui.PopID();
                }
            }
        }

        if (catalog.State == Services.GearsetReadState.Ready)
            DrawStaleKeys(config, catalog);

        ImGui.Spacing();
        if (ImGui.Button("Open wheel"))
            plugin.OpenWheel();
    }

    private static void DrawStaleKeys(Configuration config, Services.GearsetCatalog catalog)
    {
        var live = new HashSet<string>(catalog.Gearsets.Count);
        foreach (var gearset in catalog.Gearsets)
            live.Add(gearset.BlacklistKey);

        var stale = new List<string>();
        foreach (var key in config.BlacklistKeys)
        {
            if (!live.Contains(key))
                stale.Add(key);
        }

        if (stale.Count == 0)
            return;

        ImGui.Spacing();
        ImGui.TextDisabled($"{stale.Count} saved exclusion{(stale.Count == 1 ? "" : "s")} no longer match a gearset (renamed or replaced).");
        if (ImGui.Button("Remove unmatched exclusions"))
            config.RemoveBlacklistKeys(stale);
    }

    private static string DescribeHistory(IReadOnlyList<uint> jobIds, Services.GearsetCatalog catalog)
    {
        var parts = new string[jobIds.Count];
        for (var i = 0; i < jobIds.Count; i++)
        {
            string? abbreviation = null;
            foreach (var gearset in catalog.Gearsets)
            {
                if (gearset.ClassJobId == jobIds[i])
                {
                    abbreviation = gearset.JobAbbreviation;
                    break;
                }
            }

            parts[i] = abbreviation ?? jobIds[i].ToString();
        }

        return string.Join(", ", parts);
    }
}
