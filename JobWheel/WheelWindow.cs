using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui; // API 12 and older: using ImGuiNET;

namespace JobWheel;

public sealed class WheelWindow : Window
{
    private const float SpinSeconds = 4.5f;
    private const float ExtraTurns = 5f;

    private static readonly Vector4 Green = new(0.45f, 1f, 0.45f, 1f);
    private static readonly Vector4 Grey = new(0.7f, 0.7f, 0.7f, 1f);

    private readonly WheelService service;

    private float rotation;            // current wheel angle (radians)
    private bool spinning;
    private float startRotation, targetRotation;
    private DateTime spinStart;
    private JobInfo winner;
    private List<JobInfo>? snapshot;   // wheel shown while/after spinning, so it doesn't change mid-spin

    public WheelWindow(WheelService service) : base("Job Wheel")
    {
        this.service = service;
        Size = new Vector2(360, 520);
        SizeCondition = ImGuiCond.FirstUseEver;

        service.Changed += () =>
        {
            if (spinning) return;
            snapshot = null;
            rotation = 0;
        };
    }

    private static Vector4 RoleColor(Role role) => role switch
    {
        Role.Tank => new Vector4(0.30f, 0.45f, 0.85f, 1f),
        Role.Healer => new Vector4(0.30f, 0.70f, 0.40f, 1f),
        Role.Melee => new Vector4(0.80f, 0.30f, 0.30f, 1f),
        Role.Ranged => new Vector4(0.85f, 0.55f, 0.25f, 1f),
        _ => new Vector4(0.60f, 0.40f, 0.80f, 1f),
    };

    public override void Draw()
    {
        var cfg = service.Config;

        AdvanceSpin();
        DrawStatus(cfg);
        ImGui.Spacing();

        var shown = snapshot ?? service.Pool();
        DrawWheel(shown);

        var pool = service.Pool();
        var canSpin = !spinning && cfg.ActiveJob == 0 && pool.Count > 0;

        ImGui.BeginDisabled(!canSpin);
        if (ImGui.Button("Spin the wheel!", new Vector2(-1, 32)))
            StartSpin(pool);
        ImGui.EndDisabled();

        if (pool.Count == 0)
            ImGui.TextColored(Grey, "The wheel is empty. Reset it in Settings.");
        else if (cfg.ActiveJob != 0 && !spinning)
            ImGui.TextColored(Grey, "Finish your current job before spinning again.");

        ImGui.TextColored(Grey, $"{pool.Count} of {Jobs.All.Length} jobs on the wheel");

        ImGui.Spacing();
        DrawSettings(cfg);
    }

    private void AdvanceSpin()
    {
        if (!spinning) return;

        var t = (float)(DateTime.UtcNow - spinStart).TotalSeconds / SpinSeconds;
        if (t >= 1f)
        {
            rotation = targetRotation;
            spinning = false;
            service.Land(winner);
        }
        else
        {
            var eased = 1f - MathF.Pow(1f - t, 3f); // ease-out
            rotation = startRotation + (targetRotation - startRotation) * eased;
        }
    }

    private void StartSpin(List<JobInfo> pool)
    {
        snapshot = pool;
        var index = Random.Shared.Next(pool.Count);
        winner = pool[index];

        // Choose the final angle so the winner's segment centre sits under the pointer (top, -90 degrees).
        var seg = MathF.Tau / pool.Count;
        var final = -MathF.PI / 2f - (index + 0.5f) * seg;

        startRotation = rotation;
        targetRotation = final;
        while (targetRotation < startRotation + ExtraTurns * MathF.Tau)
            targetRotation += MathF.Tau;

        spinStart = DateTime.UtcNow;
        spinning = true;
    }

    private void DrawStatus(Configuration cfg)
    {
        if (cfg.ActiveJob != 0 && Jobs.Get(cfg.ActiveJob) is { } job)
        {
            ImGui.TextColored(Green, $"Your job: {job.Name} ({job.Abbr})");
            ImGui.TextWrapped("Complete one duty on this job to clear it.");

            if (ImGui.Button("Equip gearset")) service.TryEquipActive();
            ImGui.SameLine();
            if (ImGui.Button("Mark done")) service.MarkDone();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Use if a duty finished but wasn't detected.");
            ImGui.SameLine();
            if (ImGui.Button("Cancel")) service.CancelActive();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Drop this job without removing it from the wheel.");
        }
        else
        {
            ImGui.TextUnformatted("No job assigned. Spin the wheel!");
        }
    }

    private void DrawWheel(IReadOnlyList<JobInfo> jobs)
    {
        var avail = ImGui.GetContentRegionAvail().X;
        var diameter = MathF.Min(avail - 8f, 300f);
        var radius = diameter / 2f;

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (avail - diameter) / 2f);
        var origin = ImGui.GetCursorScreenPos();
        var center = origin + new Vector2(radius, radius + 18f);
        ImGui.Dummy(new Vector2(diameter, diameter + 24f));

        var dl = ImGui.GetWindowDrawList();
        var border = ImGui.GetColorU32(new Vector4(0.08f, 0.08f, 0.1f, 1f));
        var white = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f));

        var n = jobs.Count;
        if (n == 0)
        {
            dl.AddCircleFilled(center, radius, ImGui.GetColorU32(new Vector4(0.2f, 0.2f, 0.22f, 1f)), 64);
            var label = "Empty";
            dl.AddText(center - ImGui.CalcTextSize(label) / 2f, white, label);
        }
        else
        {
            var seg = MathF.Tau / n;
            var arcSteps = Math.Max(4, (int)(seg * radius / 5f));

            for (var i = 0; i < n; i++)
            {
                var a0 = rotation + i * seg;
                var a1 = a0 + seg;
                var shade = i % 2 == 0 ? 1f : 0.82f;
                var c = RoleColor(jobs[i].Role);
                var fill = ImGui.GetColorU32(new Vector4(c.X * shade, c.Y * shade, c.Z * shade, 1f));

                if (n == 1)
                {
                    dl.AddCircleFilled(center, radius, fill, 64);
                }
                else
                {
                    dl.PathLineTo(center);
                    dl.PathArcTo(center, radius, a0, a1, arcSteps);
                    dl.PathFillConvex(fill);
                    dl.AddLine(center, center + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * radius, border, 1.5f);
                }

                var mid = a0 + seg / 2f;
                var labelPos = center + new Vector2(MathF.Cos(mid), MathF.Sin(mid)) * radius * 0.68f;
                var text = jobs[i].Abbr;
                dl.AddText(labelPos - ImGui.CalcTextSize(text) / 2f, white, text);
            }
        }

        dl.AddCircle(center, radius, border, 64, 3f);
        dl.AddCircleFilled(center, radius * 0.07f, border, 24);

        // Pointer at the top
        var tip = new Vector2(center.X, center.Y - radius + 8f);
        var left = new Vector2(center.X - 10f, center.Y - radius - 14f);
        var right = new Vector2(center.X + 10f, center.Y - radius - 14f);
        dl.AddTriangleFilled(left, right, tip, ImGui.GetColorU32(new Vector4(1f, 0.85f, 0.2f, 1f)));
        dl.AddTriangle(left, right, tip, border, 2f);
    }

    private void DrawSettings(Configuration cfg)
    {
        if (!ImGui.CollapsingHeader("Settings")) return;

        var remove = cfg.RemoveFromWheel;
        if (ImGui.Checkbox("Remove job from wheel after its duty", ref remove))
        {
            cfg.RemoveFromWheel = remove;
            service.Save();
            service.NotifyChanged();
        }

        var equip = cfg.AutoEquipGearset;
        if (ImGui.Checkbox("Switch to the job's gearset automatically", ref equip))
        {
            cfg.AutoEquipGearset = equip;
            service.Save();
        }

        var warn = cfg.WarnWrongJob;
        if (ImGui.Checkbox("Warn if a duty starts on the wrong job", ref warn))
        {
            cfg.WarnWrongJob = warn;
            service.Save();
        }

        ImGui.Spacing();
        if (ImGui.Button("Reset wheel (bring every job back)"))
            service.ResetWheel();

        ImGui.Spacing();
        ImGui.TextUnformatted("Jobs on the wheel:");

        var perRow = 3;
        for (var i = 0; i < Jobs.All.Length; i++)
        {
            var job = Jobs.All[i];
            var enabled = !cfg.DisabledJobs.Contains(job.Id);
            var label = cfg.UsedJobs.Contains(job.Id) ? $"{job.Abbr} (done)##{job.Id}" : $"{job.Abbr}##{job.Id}";

            if (ImGui.Checkbox(label, ref enabled))
            {
                if (enabled) cfg.DisabledJobs.Remove(job.Id);
                else cfg.DisabledJobs.Add(job.Id);
                service.Save();
                service.NotifyChanged();
            }

            if (i % perRow != perRow - 1 && i != Jobs.All.Length - 1)
                ImGui.SameLine(((i % perRow) + 1) * 110f);
        }
    }
}
