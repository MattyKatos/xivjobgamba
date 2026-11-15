using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace XIVJobGamba.Services;

/// <summary>
/// A territory is a duty when a ContentFinderCondition row points at it and has
/// a real ContentType. DutyStarted is not used: that event fires when the
/// barrier drops, and it skips duties loaded already in progress or after a
/// disconnect. TerritoryChanged is the zone-in, which is the entry the points
/// rule is about.
/// </summary>
internal sealed class DutyEntryService
{
    private readonly IDataManager data;
    private readonly IPluginLog log;
    private Dictionary<uint, uint>? territoryToContent;
    private bool loggedFailure;

    public DutyEntryService(IDataManager data, IPluginLog log)
    {
        this.data = data;
        this.log = log;
    }

    public bool TryGetContentId(uint territoryId, out uint contentId)
    {
        contentId = 0;
        if (territoryId == 0)
            return false;

        try
        {
            var map = EnsureMap();
            return map.TryGetValue(territoryId, out contentId) && contentId != 0;
        }
        catch (Exception ex)
        {
            if (!loggedFailure)
            {
                log.Error(ex, "Couldn't read ContentFinderCondition while checking a territory.");
                loggedFailure = true;
            }

            return false;
        }
    }

    private Dictionary<uint, uint> EnsureMap()
    {
        if (territoryToContent != null)
            return territoryToContent;

        var map = new Dictionary<uint, uint>();
        var sheet = data.GetExcelSheet<ContentFinderCondition>();
        foreach (var row in sheet)
        {
            if (row.RowId == 0 || !row.TerritoryType.IsValid || row.TerritoryType.RowId == 0)
                continue;

            if (!row.ContentType.IsValid || row.ContentType.RowId == 0)
                continue;

            map.TryAdd(row.TerritoryType.RowId, row.RowId);
        }

        territoryToContent = map;
        return map;
    }
}
