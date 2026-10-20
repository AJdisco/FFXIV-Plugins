namespace JobWheel;

public enum Role { Tank, Healer, Melee, Ranged, Caster }

public readonly record struct JobInfo(byte Id, string Abbr, string Name, Role Role);

public static class Jobs
{
    // ClassJob row IDs. Blue Mage is left out on purpose (it can't enter most duties).
    public static readonly JobInfo[] All =
    {
        new(19, "PLD", "Paladin", Role.Tank),
        new(21, "WAR", "Warrior", Role.Tank),
        new(32, "DRK", "Dark Knight", Role.Tank),
        new(37, "GNB", "Gunbreaker", Role.Tank),

        new(24, "WHM", "White Mage", Role.Healer),
        new(28, "SCH", "Scholar", Role.Healer),
        new(33, "AST", "Astrologian", Role.Healer),
        new(40, "SGE", "Sage", Role.Healer),

        new(20, "MNK", "Monk", Role.Melee),
        new(22, "DRG", "Dragoon", Role.Melee),
        new(30, "NIN", "Ninja", Role.Melee),
        new(34, "SAM", "Samurai", Role.Melee),
        new(39, "RPR", "Reaper", Role.Melee),
        new(41, "VPR", "Viper", Role.Melee),

        new(23, "BRD", "Bard", Role.Ranged),
        new(31, "MCH", "Machinist", Role.Ranged),
        new(38, "DNC", "Dancer", Role.Ranged),

        new(25, "BLM", "Black Mage", Role.Caster),
        new(27, "SMN", "Summoner", Role.Caster),
        new(35, "RDM", "Red Mage", Role.Caster),
        new(42, "PCT", "Pictomancer", Role.Caster),
    };

    public static JobInfo? Get(byte id)
    {
        foreach (var j in All)
            if (j.Id == id) return j;
        return null;
    }
}
