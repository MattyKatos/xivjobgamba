using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;

namespace XIVJobGamba.Services;

internal enum GearsetReadState
{
    LoggedOut,
    Ready,
    Failed,
}

internal sealed class GearsetCatalog
{
    public static GearsetCatalog LoggedOut { get; } = new(GearsetReadState.LoggedOut, [], null, null);

    public GearsetCatalog(
        GearsetReadState state,
        IReadOnlyList<GearsetSnapshot> gearsets,
        int? currentGearsetId,
        string? error)
    {
        State = state;
        Gearsets = gearsets;
        CurrentGearsetId = currentGearsetId;
        Error = error;
    }

    public GearsetReadState State { get; }

    public IReadOnlyList<GearsetSnapshot> Gearsets { get; }

    public int? CurrentGearsetId { get; }

    public string? Error { get; }
}

internal readonly record struct EquipOutcome(bool Success, string? Failure)
{
    public static EquipOutcome Ok() => new(true, null);

    public static EquipOutcome Fail(string reason) => new(false, reason);
}

/// <summary>
/// Reads and equips gearsets through RaptureGearsetModule.
/// Calls into the module only from the framework thread.
/// EquipGearset already refuses illegal swaps and returns -1; the condition
/// checks run first so the chat line can say why.
/// </summary>
internal sealed class GearsetService
{
    private readonly IClientState clientState;
    private readonly IPlayerState playerState;
    private readonly IObjectTable objects;
    private readonly ICondition condition;
    private readonly IDataManager data;
    private readonly IPluginLog log;
    private readonly object gate = new();

    private GearsetCatalog catalog = GearsetCatalog.LoggedOut;
    private Lumina.Excel.ExcelSheet<ClassJob>? jobs;
    private long lastRefreshTicks = long.MinValue;
    private bool loggedFailure;

    public GearsetService(
        IClientState clientState,
        IPlayerState playerState,
        IObjectTable objects,
        ICondition condition,
        IDataManager data,
        IPluginLog log)
    {
        this.clientState = clientState;
        this.playerState = playerState;
        this.objects = objects;
        this.condition = condition;
        this.data = data;
        this.log = log;
    }

    public GearsetCatalog Current
    {
        get
        {
            lock (gate)
                return catalog;
        }
    }

    public void Refresh(bool force = false)
    {
        var now = Environment.TickCount64;
        if (!force && now - lastRefreshTicks < 500)
            return;

        lastRefreshTicks = now;

        if (!clientState.IsLoggedIn || !playerState.IsLoaded)
        {
            Publish(GearsetCatalog.LoggedOut);
            return;
        }

        try
        {
            EnsureJobSheet();
            Publish(ReadCatalog());
            loggedFailure = false;
        }
        catch (Exception ex)
        {
            if (!loggedFailure)
            {
                log.Error(ex, "Failed to read saved gearsets");
                loggedFailure = true;
            }

            Publish(new GearsetCatalog(GearsetReadState.Failed, [], null, ex.Message));
        }
    }

    public EquipOutcome TryEquip(int gearsetId)
    {
        if (!clientState.IsLoggedIn || !playerState.IsLoaded)
            return EquipOutcome.Fail("Log in before equipping a gearset.");

        if (objects.LocalPlayer == null)
            return EquipOutcome.Fail("Can't equip a gearset until your character is in the world.");

        if (objects.LocalPlayer.IsDead)
            return EquipOutcome.Fail("Can't equip a gearset while you're dead.");

        foreach (var block in Blocks)
        {
            if (condition[block.Flag])
                return EquipOutcome.Fail(block.Reason);
        }

        var native = TryEquipNative(gearsetId);
        return native;
    }

    private void Publish(GearsetCatalog next)
    {
        lock (gate)
            catalog = next;
    }

    private void EnsureJobSheet()
    {
        if (jobs != null)
            return;

        jobs = data.GetExcelSheet<ClassJob>();
    }

    private unsafe GearsetCatalog ReadCatalog()
    {
        var module = RaptureGearsetModule.Instance();
        if (module == null)
            return new GearsetCatalog(GearsetReadState.Failed, [], null, "Gearset module is not available.");

        var entries = module->Entries;
        var gearsets = new List<GearsetSnapshot>(entries.Length);
        for (var i = 0; i < entries.Length; i++)
        {
            if (!module->IsValidGearset(i))
                continue;

            var flags = entries[i].Flags;
            if ((flags & RaptureGearsetModule.GearsetFlag.Exists) == 0)
                continue;

            var name = CleanName(entries[i].NameString);
            var classJobId = (uint)entries[i].ClassJob;
            ResolveJob(classJobId, out var abbreviation, out var jobName, out var role, out var crafter);

            var icon = module->GetClassJobIconForGearset(i);
            var iconId = icon > 0 ? (uint)icon : 62100u + classJobId;

            gearsets.Add(new GearsetSnapshot(
                i,
                classJobId,
                abbreviation,
                jobName,
                name,
                entries[i].ItemLevel,
                iconId,
                role,
                crafter,
                Configuration.BlacklistKey(i, name)));
        }

        int? current = null;
        var currentIndex = module->CurrentGearsetIndex;
        if ((uint)currentIndex < (uint)entries.Length
            && module->IsValidGearset(currentIndex)
            && (entries[currentIndex].Flags & RaptureGearsetModule.GearsetFlag.Exists) != 0)
        {
            current = currentIndex;
        }

        return new GearsetCatalog(GearsetReadState.Ready, gearsets, current, null);
    }

    private unsafe EquipOutcome TryEquipNative(int gearsetId)
    {
        var module = RaptureGearsetModule.Instance();
        if (module == null)
            return EquipOutcome.Fail("Gearsets aren't available yet.");

        if (!module->IsValidGearset(gearsetId))
            return EquipOutcome.Fail("That gearset no longer exists.");

        var entry = module->GetGearset(gearsetId);
        if (entry == null || (entry->Flags & RaptureGearsetModule.GearsetFlag.Exists) == 0)
            return EquipOutcome.Fail("That gearset no longer exists.");

        // 0 uses the glamour plate linked to the gearset, matching a manual equip.
        var result = module->EquipGearset(gearsetId, 0);
        if (result != 0)
            return EquipOutcome.Fail("The game refused to equip that gearset. Finish whatever you're doing, then spin again.");

        return EquipOutcome.Ok();
    }

    private void ResolveJob(uint classJobId, out string abbreviation, out string jobName, out WheelRole role, out bool crafter)
    {
        abbreviation = classJobId == 0 ? "ADV" : classJobId.ToString();
        jobName = classJobId == 0 ? "Adventurer" : "Job " + classJobId;
        role = WheelRole.Unknown;
        crafter = false;

        if (jobs == null || !jobs.TryGetRow(classJobId, out var row))
            return;

        var abbr = row.Abbreviation.ExtractText();
        var name = row.Name.ExtractText();
        if (!string.IsNullOrWhiteSpace(abbr))
            abbreviation = abbr;

        if (!string.IsNullOrWhiteSpace(name))
            jobName = name;

        crafter = row.DohDolJobIndex >= 0;
        role = JobRoleClassifier.Classify(SignalsFor(row));
    }

    private JobRoleSignals SignalsFor(ClassJob row)
    {
        var hand = false;
        var land = false;
        var war = false;
        var magic = false;

        try
        {
            if (row.ClassJobCategory.IsValid)
            {
                var category = row.ClassJobCategory.Value;
                // Membership columns on ClassJobCategory, not job row ids.
                // Hand and land are exclusive on the discipline rows the job points at.
                hand = category.CRP || category.BSM || category.ARM || category.GSM
                    || category.LTW || category.WVR || category.ALC || category.CUL;
                land = category.MIN || category.BTN || category.FSH;
                war = category.GLA || category.PGL || category.MRD || category.LNC || category.ARC
                    || category.PLD || category.MNK || category.WAR || category.DRG || category.BRD
                    || category.ROG || category.NIN || category.MCH || category.DRK || category.SAM
                    || category.GNB || category.DNC || category.RPR || category.VPR;
                magic = category.CNJ || category.THM || category.WHM || category.BLM || category.ACN
                    || category.SMN || category.SCH || category.AST || category.SGE || category.RDM
                    || category.PCT || category.BLU;
            }
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Couldn't read the ClassJobCategory row while coloring a slice.");
        }

        return new JobRoleSignals(
            row.Role,
            row.JobType,
            row.DohDolJobIndex,
            row.PrimaryStat,
            row.UIPriority,
            hand,
            land,
            war,
            magic);
    }

    private static string CleanName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "(unnamed)";

        return raw.Trim().TrimEnd('\0');
    }

    private readonly record struct Block(ConditionFlag Flag, string Reason);

    // EquipGearset rejects these itself. Naming them here is so the player
    // sees a reason instead of a bare failure. Duty-bound is intentionally
    // absent: swapping jobs between pulls is allowed.
    private static readonly Block[] Blocks =
    [
        new(ConditionFlag.InCombat, "Can't equip a gearset during combat."),
        new(ConditionFlag.Unconscious, "Can't equip a gearset while unconscious."),
        new(ConditionFlag.BetweenAreas, "Can't equip a gearset while zoning."),
        new(ConditionFlag.BetweenAreas51, "Can't equip a gearset while zoning."),
        new(ConditionFlag.WatchingCutscene, "Can't equip a gearset during a cutscene."),
        new(ConditionFlag.WatchingCutscene78, "Can't equip a gearset during a cutscene."),
        new(ConditionFlag.OccupiedInCutSceneEvent, "Can't equip a gearset during a cutscene."),
        new(ConditionFlag.Casting, "Can't equip a gearset while casting."),
        new(ConditionFlag.Casting87, "Can't equip a gearset while casting."),
        new(ConditionFlag.Crafting, "Can't equip a gearset while crafting."),
        new(ConditionFlag.PreparingToCraft, "Can't equip a gearset while crafting."),
        new(ConditionFlag.ExecutingCraftingAction, "Can't equip a gearset while crafting."),
        new(ConditionFlag.Gathering, "Can't equip a gearset while gathering."),
        new(ConditionFlag.ExecutingGatheringAction, "Can't equip a gearset while gathering."),
        new(ConditionFlag.Mounted, "Can't equip a gearset while mounted."),
        new(ConditionFlag.Mounting, "Can't equip a gearset while mounting."),
        new(ConditionFlag.Mounting71, "Can't equip a gearset while mounting."),
        new(ConditionFlag.RidingPillion, "Can't equip a gearset while mounted."),
        new(ConditionFlag.MountOrOrnamentTransition, "Can't equip a gearset while mounting."),
        new(ConditionFlag.TradeOpen, "Can't equip a gearset while trading."),
        new(ConditionFlag.Occupied, "Can't equip a gearset while occupied."),
        new(ConditionFlag.Occupied30, "Can't equip a gearset while occupied."),
        new(ConditionFlag.Occupied33, "Can't equip a gearset while occupied."),
        new(ConditionFlag.Occupied38, "Can't equip a gearset while occupied."),
        new(ConditionFlag.Occupied39, "Can't equip a gearset while occupied."),
        new(ConditionFlag.OccupiedInEvent, "Can't equip a gearset while occupied."),
        new(ConditionFlag.OccupiedInQuestEvent, "Can't equip a gearset during a quest event."),
        new(ConditionFlag.OccupiedSummoningBell, "Can't equip a gearset while using the summoning bell."),
        new(ConditionFlag.Jumping, "Can't equip a gearset while jumping."),
        new(ConditionFlag.Jumping61, "Can't equip a gearset while jumping."),
        new(ConditionFlag.Diving, "Can't equip a gearset while diving."),
        new(ConditionFlag.PlayingMiniGame, "Can't equip a gearset during a minigame."),
        new(ConditionFlag.Performing, "Can't equip a gearset while performing."),
    ];
}
