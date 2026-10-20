using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace JobWheel;

/// <summary>Game-facing logic: the wheel pool, gearset switching and duty tracking.</summary>
public sealed unsafe class WheelService
{
    private readonly Action save;
    private readonly IChatGui chat;
    private readonly IObjectTable objects;
    private readonly ICondition condition;

    public Configuration Config { get; }

    /// <summary>Raised when the pool changes (reset, duty done, toggles) so the UI can redraw the wheel.</summary>
    public event Action? Changed;

    public WheelService(Configuration config, Action save, IChatGui chat, IObjectTable objects, ICondition condition)
    {
        Config = config;
        this.save = save;
        this.chat = chat;
        this.objects = objects;
        this.condition = condition;
    }

    public void Save() => save();
    public void NotifyChanged() => Changed?.Invoke();

    public List<JobInfo> Pool() =>
        Jobs.All.Where(j => !Config.DisabledJobs.Contains(j.Id)
                         && !(Config.RemoveFromWheel && Config.UsedJobs.Contains(j.Id)))
                .ToList();

    private void Say(string message) => chat.Print($"[Job Wheel] {message}");

    private byte CurrentJobId() => (byte)(objects.LocalPlayer?.ClassJob.RowId ?? 0);

    /// <summary>Called when the spin animation finishes on a job.</summary>
    public void Land(JobInfo job)
    {
        Config.ActiveJob = job.Id;
        save();
        Say($"The wheel chose {job.Name}! Finish one duty on it.");

        if (Config.AutoEquipGearset)
        {
            TryEquip(job, out var message);
            Say(message);
        }
    }

    public bool TryEquip(JobInfo job, out string message)
    {
        if (condition[ConditionFlag.BoundByDuty] || condition[ConditionFlag.InCombat])
        {
            message = "Can't change gearset while in a duty or in combat. Switch manually when you can.";
            return false;
        }

        var module = RaptureGearsetModule.Instance();
        if (module == null)
        {
            message = "Gearsets aren't available right now.";
            return false;
        }

        for (var i = 0; i < 100; i++)
        {
            if (!module->IsValidGearset(i)) continue;
            var entry = module->GetGearset(i);
            if (entry == null || entry->ClassJob != job.Id) continue;

            module->EquipGearset(i);
            message = $"Switched to gearset {i + 1} ({job.Abbr}).";
            return true;
        }

        message = $"No gearset found for {job.Name}. Create and save one, then press \"Equip gearset\".";
        return false;
    }

    public void TryEquipActive()
    {
        if (Jobs.Get(Config.ActiveJob) is { } job)
        {
            TryEquip(job, out var message);
            Say(message);
        }
    }

    public void OnDutyStarted()
    {
        if (Config.ActiveJob == 0 || !Config.WarnWrongJob) return;
        if (CurrentJobId() != Config.ActiveJob && Jobs.Get(Config.ActiveJob) is { } job)
            Say($"Heads up: the wheel assigned {job.Name}, but you're on a different job.");
    }

    public void OnDutyCompleted()
    {
        if (Config.ActiveJob == 0) return;

        if (CurrentJobId() != Config.ActiveJob)
        {
            Say("Duty completed, but not on the assigned job, so it doesn't count.");
            return;
        }

        MarkDone();
    }

    /// <summary>Finish the current assignment. The job leaves the wheel if "remove" is on.</summary>
    public void MarkDone()
    {
        if (Config.ActiveJob == 0) return;

        var job = Jobs.Get(Config.ActiveJob);
        if (Config.RemoveFromWheel)
            Config.UsedJobs.Add(Config.ActiveJob);
        Config.ActiveJob = 0;
        save();

        var left = Pool().Count;
        Say($"{job?.Name ?? "Job"} done! {left} job{(left == 1 ? "" : "s")} left on the wheel.");
        Changed?.Invoke();
    }

    /// <summary>Drop the current assignment without using the job up.</summary>
    public void CancelActive()
    {
        Config.ActiveJob = 0;
        save();
        Changed?.Invoke();
    }

    public void ResetWheel()
    {
        Config.UsedJobs.Clear();
        save();
        Changed?.Invoke();
    }
}
