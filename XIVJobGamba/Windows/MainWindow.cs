using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace XIVJobGamba.Windows;

internal sealed class MainWindow : Window
{
    private readonly Plugin plugin;
    private readonly SpinAnimation animation = new();
    private readonly object requestGate = new();

    private List<GearsetSnapshot> heldWheel = [];
    private bool holdingResult;
    private int? landedIndex;
    private bool landedOnCooldown;
    private GearsetSnapshot? pendingEquip;
    private string? banner;
    private bool bannerSuccess;

    public MainWindow(Plugin plugin)
        : base("XIV Job Gamba###XIVJobGambaMain")
    {
        this.plugin = plugin;
        AllowPinning = true;

        Size = new Vector2(560, 860);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(440, 680),
            MaximumSize = new Vector2(960, 1400),
        };
    }

    public override void OnClose()
    {
        if (!animation.IsSpinning)
            return;

        animation.Cancel();
        lock (requestGate)
            pendingEquip = null;
    }

    public bool TryDequeueEquip(out GearsetSnapshot entry)
    {
        lock (requestGate)
        {
            if (pendingEquip == null)
            {
                entry = null!;
                return false;
            }

            entry = pendingEquip;
            pendingEquip = null;
            return true;
        }
    }

    public void ShowOutcome(bool success, string message)
    {
        landedOnCooldown = success && holdingResult;
        banner = message;
        bannerSuccess = success;
    }

    public override void PreDraw()
    {
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.09f, 0.08f, 0.07f, 0.97f));
        ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.16f, 0.12f, 0.06f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.32f, 0.22f, 0.08f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.72f, 0.58f, 0.28f, 0.70f));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.46f, 0.32f, 0.10f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.64f, 0.46f, 0.14f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.80f, 0.58f, 0.18f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(7);
    }

    public override void Draw()
    {
        var now = (float)ImGui.GetTime();
        if (animation.IsSpinning && animation.Advance(now))
            OnSpinFinished();

        var plan = BuildPlan();
        DrawBanner();
        DrawBlockReason(plan);
        DrawWheel(plan);
        DrawReadout(plan);
        DrawButtons(plan);
        DrawCooldown(plan);
        DrawFootnotes(plan);
    }

    private WheelPlan BuildPlan()
    {
        var catalog = plugin.Gearsets.Current;
        var config = plugin.Configuration;
        var selected = SpinRules.SelectEntries(
            catalog.Gearsets,
            config.OneSlicePerJob,
            config.IncludeCraftersAndGatherers,
            config.BlacklistKeys);
        var eligible = SpinRules.Eligible(selected, config.RecentJobIds, config.CooldownSpins);
        return new WheelPlan(catalog, selected, eligible);
    }

    private void DrawBanner()
    {
        if (string.IsNullOrEmpty(banner))
            return;

        var color = bannerSuccess
            ? new Vector4(0.62f, 0.86f, 0.55f, 1f)
            : new Vector4(0.95f, 0.58f, 0.42f, 1f);

        using (ImRaii.PushColor(ImGuiCol.Text, color))
            ImGui.TextWrapped(banner);

        ImGui.Spacing();
    }

    private void DrawBlockReason(WheelPlan plan)
    {
        if (animation.IsSpinning || AwaitingEquip)
            return;

        var reason = SpinRules.WhySpinIsBlocked(
            plan.Catalog.State != Services.GearsetReadState.LoggedOut,
            plan.Catalog.State == Services.GearsetReadState.Failed,
            plan.Catalog.Gearsets.Count,
            plan.Selected.Count,
            plan.Eligible.Count,
            plugin.Configuration.CooldownSpins);

        if (reason == null)
            return;

        using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.93f, 0.78f, 0.45f, 1f)))
            ImGui.TextWrapped(reason);

        if (SpinRules.AllOnCooldown(plan.Selected, plugin.Configuration.RecentJobIds, plugin.Configuration.CooldownSpins))
        {
            if (ImGui.Button("Clear history"))
                plugin.Configuration.ClearHistory();
        }

        ImGui.Spacing();
    }

    private void DrawWheel(WheelPlan plan)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var width = MathF.Max(280f * scale, ImGui.GetContentRegionAvail().X);
        var side = MathF.Min(width, 520f * scale);
        var topLeft = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(MathF.Max(width, side), side));

        var center = topLeft + new Vector2(width / 2f, (side / 2f) + (6f * scale));
        var radius = (side * 0.42f) - (8f * scale);
        var display = DisplayEntries(plan);
        var rotation = DisplayRotation(display.Count);
        var hub = HubText(display);

        WheelRenderer.Draw(center, radius, rotation, display, MutedJob(display), hub, plugin.TextureProvider, scale);
        DrawHover(center, radius, rotation, display);
    }

    private void DrawHover(Vector2 center, float radius, float rotation, IReadOnlyList<GearsetSnapshot> display)
    {
        if (display.Count == 0 || !ImGui.IsWindowHovered())
            return;

        var mouse = ImGui.GetMousePos();
        var delta = mouse - center;
        var distance = MathF.Sqrt((delta.X * delta.X) + (delta.Y * delta.Y));
        if (distance > radius || distance < radius * 0.40f)
            return;

        var index = WheelMath.SliceIndexAtAngle(MathF.Atan2(delta.Y, delta.X), rotation, display.Count);
        var entry = display[index];
        var suffix = MutedJob(display) == entry.ClassJobId ? "\nOn cooldown after this spin" : string.Empty;
        ImGui.SetTooltip($"{entry.JobName} ({entry.JobAbbreviation})\n{entry.GearsetName}\niLv {entry.ItemLevel}{suffix}");
    }

    private uint? MutedJob(IReadOnlyList<GearsetSnapshot> display)
    {
        if (!holdingResult || !landedOnCooldown || landedIndex is not int index || (uint)index >= (uint)display.Count)
            return null;

        return display[index].ClassJobId;
    }

    private void DrawReadout(WheelPlan plan)
    {
        var display = DisplayEntries(plan);
        ImGui.Spacing();
        if (display.Count == 0)
        {
            ImGui.TextDisabled("Nothing is on the wheel.");
            return;
        }

        var index = WheelMath.SliceIndexAtPointer(DisplayRotation(display.Count), display.Count);
        var entry = display[index];
        var verb = animation.IsSpinning ? "Passing" : holdingResult ? "Landed on" : "At the pointer";
        ImGui.TextWrapped($"{verb}: {entry.JobAbbreviation}  {entry.JobName}  —  {entry.GearsetName}  (iLv {entry.ItemLevel})");

        if (holdingResult && landedOnCooldown)
            ImGui.TextDisabled("Grey slices are that job. They stay off the next spin until the cooldown ends.");
        else if (holdingResult && !bannerSuccess && !string.IsNullOrEmpty(banner))
            ImGui.TextDisabled("That roll was not equipped, so it was not added to the cooldown.");
    }

    private void DrawButtons(WheelPlan plan)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var canSpin = !animation.IsSpinning && !AwaitingEquip && plan.Eligible.Count > 0;
        var label = animation.IsSpinning ? "Spinning..." : plan.Eligible.Count > 0 ? $"Spin ({plan.Eligible.Count})" : "Spin";

        using (ImRaii.Disabled(!canSpin))
        {
            if (ImGui.Button(label, new Vector2(190f * scale, 40f * scale)) && canSpin)
                StartSpin(plan.Eligible);
        }

        ImGui.SameLine();
        if (ImGui.Button("Settings", new Vector2(130f * scale, 40f * scale)))
            plugin.OpenSettings();

        if (plugin.Configuration.RecentJobIds.Count > 0)
        {
            ImGui.SameLine();
            if (ImGui.Button("Clear history", new Vector2(140f * scale, 40f * scale)))
            {
                plugin.Configuration.ClearHistory();
                holdingResult = false;
                landedOnCooldown = false;
            }
        }
    }

    private void DrawCooldown(WheelPlan plan)
    {
        var config = plugin.Configuration;
        ImGui.Spacing();
        ImGui.Separator();
        var cooldownLabel = config.CooldownSpins == 0
            ? "Job cooldown is off."
            : $"Job cooldown: last {config.CooldownSpins} successful spin{(config.CooldownSpins == 1 ? "" : "s")}.";
        ImGui.TextDisabled(cooldownLabel);

        if (config.CooldownSpins <= 0 || plan.Selected.Count == 0)
            return;

        var shown = false;
        var seen = new HashSet<uint>();
        foreach (var entry in plan.Selected)
        {
            if (!seen.Add(entry.ClassJobId))
                continue;

            var remaining = SpinRules.SpinsRemaining(entry.ClassJobId, config.RecentJobIds, config.CooldownSpins);
            if (remaining <= 0)
                continue;

            shown = true;
            ImGui.TextDisabled($"  {entry.JobAbbreviation}  {entry.JobName}  —  {remaining} spin{(remaining == 1 ? "" : "s")} left");
        }

        if (!shown)
            ImGui.TextDisabled("  No jobs are on cooldown.");
    }

    private void DrawFootnotes(WheelPlan plan)
    {
        var config = plugin.Configuration;
        var catalog = plan.Catalog;
        if (catalog.State != Services.GearsetReadState.Ready)
            return;

        var excluded = 0;
        foreach (var gearset in catalog.Gearsets)
        {
            if (config.IsBlacklisted(gearset.BlacklistKey))
                excluded++;
        }

        ImGui.Spacing();
        ImGui.TextDisabled($"{plan.Eligible.Count} on the next spin, {catalog.Gearsets.Count} saved, {excluded} excluded.");

        if (catalog.CurrentGearsetId is int current)
        {
            GearsetSnapshot? equipped = null;
            foreach (var gearset in catalog.Gearsets)
            {
                if (gearset.GearsetId == current)
                {
                    equipped = gearset;
                    break;
                }
            }

            if (equipped != null)
                ImGui.TextDisabled($"Equipped now: {equipped.JobAbbreviation} — {equipped.GearsetName}");
        }

        if (ImGui.CollapsingHeader("Why isn't a job on the wheel?"))
        {
            ImGui.TextWrapped(
                "The wheel only draws jobs the next spin can land on. Blacklisted gearsets never appear. " +
                "With the cooldown above zero, a job that was successfully equipped in the last X spins is skipped until it ages out. " +
                "If the game refuses the equip (combat, casting, mounted, and so on), that spin is not added to history and the job stays eligible. " +
                "By default every saved gearset is its own slice, including two sets for the same job. " +
                "Settings can collapse that to one slice per job, keeping the highest item level, and can hide crafters and gatherers.");
        }
    }

    private void StartSpin(IReadOnlyList<GearsetSnapshot> eligible)
    {
        if (eligible.Count == 0)
            return;

        heldWheel = eligible.ToList();
        holdingResult = false;
        landedIndex = null;
        landedOnCooldown = false;
        banner = null;

        var index = Random.Shared.Next(heldWheel.Count);
        var extraTurns = 4f + ((float)Random.Shared.NextDouble() * 2f);
        var slice = MathF.Tau / heldWheel.Count;
        var jitter = (((float)Random.Shared.NextDouble() * 2f) - 1f) * slice * 0.22f;
        animation.SetRotation(WheelMath.CenterRotation(0, heldWheel.Count, 0f));
        animation.Start(heldWheel.Count, index, (float)ImGui.GetTime(), 4.6f, extraTurns, jitter);
    }

    private void OnSpinFinished()
    {
        if (heldWheel.Count == 0)
            return;

        landedIndex = animation.WinnerIndex;
        holdingResult = true;
        var winner = heldWheel[animation.WinnerIndex];
        lock (requestGate)
            pendingEquip = winner;
    }

    private IReadOnlyList<GearsetSnapshot> DisplayEntries(WheelPlan plan)
        => animation.IsSpinning || holdingResult ? heldWheel : plan.Eligible;

    private float DisplayRotation(int count)
    {
        if (animation.IsSpinning || holdingResult)
            return animation.Rotation;

        return count > 0 ? WheelMath.CenterRotation(0, count, 0f) : 0f;
    }

    private string HubText(IReadOnlyList<GearsetSnapshot> display)
    {
        if (display.Count == 0)
            return "—";

        var index = WheelMath.SliceIndexAtPointer(DisplayRotation(display.Count), display.Count);
        return display[index].JobAbbreviation;
    }

    private bool AwaitingEquip
    {
        get
        {
            lock (requestGate)
                return pendingEquip != null;
        }
    }

    private sealed record WheelPlan(
        Services.GearsetCatalog Catalog,
        IReadOnlyList<GearsetSnapshot> Selected,
        IReadOnlyList<GearsetSnapshot> Eligible);
}
