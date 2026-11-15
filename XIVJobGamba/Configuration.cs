using Dalamud.Configuration;
using Newtonsoft.Json;

namespace XIVJobGamba;

[Serializable]
internal sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>
    /// Jobs picked within the last this many successful spins stay off the wheel.
    /// Zero disables the cooldown.
    /// </summary>
    public int CooldownSpins { get; set; } = 3;

    /// <summary>
    /// When false, every saved gearset is a slice. When true, one slice per job,
    /// keeping the highest item level set.
    /// </summary>
    public bool OneSlicePerJob { get; set; }

    public bool IncludeCraftersAndGatherers { get; set; } = true;

    /// <summary>
    /// Keys are "{gearset slot}:{exact name}". Renaming a set, or saving a
    /// different set into that slot, drops the exclusion.
    /// </summary>
    public List<string> BlacklistKeys { get; set; } = [];

    /// <summary>
    /// Successful picks, oldest first. Only the last <see cref="CooldownSpins"/>
    /// entries block the wheel. Failed equips are not recorded.
    /// </summary>
    public List<uint> RecentJobIds { get; set; } = [];

    [JsonIgnore]
    private Dalamud.Plugin.IDalamudPluginInterface? pluginInterface;

    public void Initialize(Dalamud.Plugin.IDalamudPluginInterface pluginInterface)
    {
        this.pluginInterface = pluginInterface;
        Sanitize();
    }

    public void Sanitize()
    {
        CooldownSpins = Math.Clamp(CooldownSpins, 0, SpinRules.MaxCooldown);
        BlacklistKeys ??= [];
        RecentJobIds ??= [];

        if (RecentJobIds.Count > SpinRules.MaxHistory)
            RecentJobIds.RemoveRange(0, RecentJobIds.Count - SpinRules.MaxHistory);
    }

    public void Save()
    {
        Sanitize();
        pluginInterface?.SavePluginConfig(this);
    }

    public bool IsBlacklisted(string key) => BlacklistKeys.Contains(key);

    public void SetBlacklisted(string key, bool excluded)
    {
        if (excluded)
        {
            if (!BlacklistKeys.Contains(key))
                BlacklistKeys.Add(key);
        }
        else
        {
            BlacklistKeys.Remove(key);
        }

        Save();
    }

    public void RemoveBlacklistKeys(IEnumerable<string> keys)
    {
        var drop = keys as IReadOnlySet<string> ?? new HashSet<string>(keys);
        if (drop.Count == 0)
            return;

        BlacklistKeys.RemoveAll(drop.Contains);
        Save();
    }

    public void RememberJob(uint classJobId)
    {
        SpinRules.Remember(RecentJobIds, classJobId);
        Save();
    }

    public void ClearHistory()
    {
        RecentJobIds.Clear();
        Save();
    }

    public static string BlacklistKey(int gearsetId, string gearsetName) => $"{gearsetId}:{gearsetName}";
}
