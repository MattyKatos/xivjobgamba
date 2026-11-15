using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using XIVJobGamba.Services;
using XIVJobGamba.Windows;

namespace XIVJobGamba;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/gamba";

    private readonly ICommandManager commandManager;
    private readonly IClientState clientState;
    private readonly IPlayerState playerState;
    private readonly IFramework framework;
    private readonly IChatGui chat;
    private readonly IPluginLog log;
    private readonly GearsetService gearsets;
    private readonly DutyEntryService dutyEntries;
    private readonly DutyScoreStore scores;
    private readonly MainWindow mainWindow;
    private readonly ConfigWindow configWindow;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        ITextureProvider textureProvider,
        IClientState clientState,
        IPlayerState playerState,
        IObjectTable objects,
        ICondition condition,
        IDataManager data,
        IFramework framework,
        IChatGui chat,
        IPluginLog log)
    {
        this.commandManager = commandManager;
        this.clientState = clientState;
        this.playerState = playerState;
        this.framework = framework;
        this.chat = chat;
        this.log = log;
        TextureProvider = textureProvider;

        Configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(pluginInterface);

        gearsets = new GearsetService(clientState, playerState, objects, condition, data, log);
        dutyEntries = new DutyEntryService(data, log);
        scores = DutyScoreStore.Load(Path.Combine(pluginInterface.GetPluginConfigDirectory(), "duty-score.bin"));
        if (scores.ResetBecauseUnreadable)
            log.Warning("Duty score file was missing a readable header. Points and the pending spin started over at 0.");
        mainWindow = new MainWindow(this);
        configWindow = new ConfigWindow(this);

        WindowSystem = new WindowSystem("XIVJobGamba");
        WindowSystem.AddWindow(mainWindow);
        WindowSystem.AddWindow(configWindow);

        commandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the gearset wheel. \"/gamba config\" opens settings.",
            ShowInHelp = true,
        });

        pluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        pluginInterface.UiBuilder.OpenConfigUi += OpenSettings;
        pluginInterface.UiBuilder.OpenMainUi += OpenWheel;
        framework.Update += OnFramework;
        clientState.TerritoryChanged += OnTerritoryChanged;
        framework.RunOnFrameworkThread(() =>
        {
            if (!disposed)
                gearsets.Refresh(force: true);
        });

        PluginInterface = pluginInterface;
        log.Information("XIV Job Gamba ready. /gamba opens the wheel.");
    }

    internal IDalamudPluginInterface PluginInterface { get; }

    internal ITextureProvider TextureProvider { get; }

    internal Configuration Configuration { get; }

    internal GearsetService Gearsets => gearsets;

    internal DutyScoreStore Scores => scores;

    private WindowSystem WindowSystem { get; }

    private bool disposed;

    public void Dispose()
    {
        disposed = true;
        framework.Update -= OnFramework;
        clientState.TerritoryChanged -= OnTerritoryChanged;
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenSettings;
        PluginInterface.UiBuilder.OpenMainUi -= OpenWheel;

        WindowSystem.RemoveAllWindows();
        commandManager.RemoveHandler(CommandName);
    }

    internal void OpenSettings()
    {
        configWindow.IsOpen = true;
        configWindow.BringToFront();
        if (!disposed)
            gearsets.Refresh(force: true);
    }

    internal void OpenWheel()
    {
        mainWindow.IsOpen = true;
        mainWindow.BringToFront();
        gearsets.Refresh(force: true);
    }

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "config":
            case "settings":
                OpenSettings();
                break;
            case "help":
                chat.Print("/gamba opens the wheel. /gamba config opens settings.", "Job Gamba");
                break;
            default:
                if (mainWindow.IsOpen)
                    mainWindow.IsOpen = false;
                else
                    OpenWheel();
                break;
        }
    }

    private void OnFramework(IFramework _)
    {
        if (disposed)
            return;

        gearsets.Refresh();

        if (!mainWindow.TryDequeueEquip(out var entry))
            return;

        scores.RecordSpin(entry.ClassJobId);

        try
        {
            var outcome = gearsets.TryEquip(entry.GearsetId);
            if (outcome.Success)
            {
                Configuration.RememberJob(entry.ClassJobId);
                var message = $"Equipped {entry.JobName} ({entry.JobAbbreviation}) — {entry.GearsetName}.";
                mainWindow.ShowOutcome(true, message);
                chat.Print(message, "Job Gamba");
                log.Information($"Equipped gearset {entry.GearsetId} ({entry.GearsetName})");
            }
            else
            {
                var message = outcome.Failure ?? "The game refused to equip that gearset.";
                mainWindow.ShowOutcome(false, message);
                chat.PrintError(message, "Job Gamba");
            }
        }
        catch (Exception ex)
        {
            log.Error(ex, $"Failed while equipping gearset {entry.GearsetId}");
            mainWindow.ShowOutcome(false, "Couldn't equip that gearset. See /xllog.");
            chat.PrintError("Couldn't equip that gearset. See /xllog.", "Job Gamba");
        }
    }

    private void OnTerritoryChanged(uint territoryId)
    {
        framework.RunOnFrameworkThread(() => HandleTerritory(territoryId));
    }

    private void HandleTerritory(uint territoryId)
    {
        if (disposed)
            return;

        if (!dutyEntries.TryGetContentId(territoryId, out var contentId))
        {
            scores.LeaveDuty();
            return;
        }

        if (scores.CurrentDutyContentId == contentId)
            return;

        uint? currentJob = playerState.ClassJob.IsValid ? playerState.ClassJob.RowId : null;
        var decision = scores.EnterDuty(currentJob, Configuration.PointsPerDuty, contentId);
        AnnounceDutyPoints(decision);
    }

    private void AnnounceDutyPoints(DutyPointDecision decision)
    {
        if (decision.Awarded && decision.PointsAwarded > 0)
        {
            chat.Print(DutyPoints.Describe(decision, scores.Points, JobLabel(decision.LandedJobId), JobLabel(decision.CurrentJobId)), "Job Gamba");
            return;
        }

        if (!Configuration.ExplainMissedDutyPoints && !(decision.Awarded && decision.PointsAwarded == 0))
            return;

        chat.Print(DutyPoints.Describe(decision, scores.Points, JobLabel(decision.LandedJobId), JobLabel(decision.CurrentJobId)), "Job Gamba");
    }

    internal string JobLabel(uint? classJobId)
    {
        if (classJobId is null)
            return "an unknown job";

        foreach (var gearset in gearsets.Current.Gearsets)
        {
            if (gearset.ClassJobId == classJobId.Value)
                return $"{gearset.JobName} ({gearset.JobAbbreviation})";
        }

        return $"job {classJobId.Value}";
    }
}
