using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace JobWheel;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>Remove a job from the wheel once a duty has been completed on it.</summary>
    public bool RemoveFromWheel { get; set; } = true;

    /// <summary>Switch to the job's gearset automatically when the wheel lands on it.</summary>
    public bool AutoEquipGearset { get; set; } = true;

    /// <summary>Print a chat warning when a duty starts and you aren't on the assigned job.</summary>
    public bool WarnWrongJob { get; set; } = true;

    /// <summary>Job IDs the user switched off. Everything else is on the wheel by default.</summary>
    public HashSet<byte> DisabledJobs { get; set; } = new();

    /// <summary>Job IDs already completed (only filled while RemoveFromWheel is on).</summary>
    public HashSet<byte> UsedJobs { get; set; } = new();

    /// <summary>The job the wheel assigned and that is still waiting on a duty. 0 = none.</summary>
    public byte ActiveJob { get; set; }
}
