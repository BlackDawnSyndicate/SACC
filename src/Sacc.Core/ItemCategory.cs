namespace Sacc.Core;

/// <summary>
/// Groups spawn codes into broad, SAH-style categories for the Spawner's category filter.
/// SCUM's own item list is a flat list of asset ids with no category data, so the category is
/// inferred from keywords in the code name (clean-room: our own rules, not anyone else's table).
/// First matching rule wins, so more specific rules come first; anything unmatched is "Other".
/// The grouping is a browsing aid, not authoritative — a few codes will land in the wrong bucket,
/// and the rules are meant to be tuned.
/// </summary>
public static class ItemCategory
{
    public const string All = "All";
    public const string Other = "Other";

    // (category, keywords that put a code there). Checked top to bottom; first hit wins.
    private static readonly (string Category, string[] Keywords)[] ItemRules =
    {
        ("Magazines",   new[] { "magazine", "_mag", "mag_", "clip", "speedloader", "ammo_drum", "_drum" }),
        ("Ammunition",  new[] { "gauge", "ammo", "_cal", "cal_", "caliber", "bullet", "_round", "cartridge", "shell",
                                "buckshot", "birdshot", "slug", "pellet", "arrow", "_bolt", "quarrel", "rocket",
                                "grenade", "flashbang", "molotov", "_fmj", "_hp", "nato" }),
        ("Attachments", new[] { "scope", "sight", "suppressor", "silencer", "foregrip", "_grip", "_barrel", "stock",
                                "muzzle", "optic", "_rail", "bipod", "laser", "holosight", "reddot", "red_dot" }),
        ("Traps",       new[] { "trap", "_mine", "mine_", "snare", "beartrap", "landmine", "claymore" }),
        ("Building",    new[] { "bb_", "_wall", "foundation", "_fence", "_gate", "_roof", "_ramp", "_floor",
                                "stairs", "doorframe", "flag_pole", "deployable", "squad_", "shelter" }),
        ("Medical",     new[] { "bandage", "medical", "morphine", "antibiotic", "painkiller", "firstaid", "first_aid",
                                "syringe", "medkit", "suture", "disinfect", "iodine", "epinephrine", "splint",
                                "_pill", "vitamin", "charcoal_pill", "tourniquet", "defibrillator", "adrenaline" }),
        ("Electronics", new[] { "battery", "_radio", "phone", "laptop", "camera", "_gps", "electronic", "_cable",
                                "circuit", "_chip", "flashlight", "_bulb", "generator", "walkie", "transmitter",
                                "motherboard", "transistor", "resistor" }),
        ("Weapons",     new[] { "weapon", "rifle", "pistol", "revolver", "shotgun", "_smg", "sniper", "carbine", "_m16",
                                "_m9", "ak47", "ak_", "ak74", "glock", "1911", "deagle", "desert_eagle", "mp5",
                                "_uzi", "firearm", "handgun", "launcher", "_rpg", "crossbow", "_bow", "_gun" }),
        ("Melee",       new[] { "knife", "machete", "_axe", "axe_", "hatchet", "cleaver", "_bat", "baseball", "_sword",
                                "katana", "spear", "sledge", "crowbar", "sickle", "_club", "nightstick", "baton",
                                "kukri", "dagger", "shiv", "brass_knuckle" }),
        ("Armor",       new[] { "armor", "armour", "_plate", "kevlar", "ballistic", "helmet", "_padding", "riot_" }),
        ("Backpacks",   new[] { "backpack", "_bag", "rucksack", "_pouch", "satchel", "duffel", "holster" }),
        ("Clothing",    new[] { "shirt", "_pants", "trouser", "jacket", "_coat", "boots", "_shoe", "sneaker", "_hat",
                                "_cap", "glove", "_mask", "balaclava", "bandana", "_sock", "underwear", "_skirt",
                                "_dress", "_belt", "necklace", "_ring", "glasses", "goggles", "hoodie", "sweater",
                                "beanie", "scarf", "_vest", "jeans", "shorts", "uniform", "suit", "poncho" }),
        ("Food",        new[] { "_food", "can_", "_meat", "_fish", "fruit", "vegetable", "bread", "_rice", "beans",
                                "_mre", "ration", "_egg", "cheese", "apple", "banana", "_corn", "potato", "snack",
                                "chocolate", "cereal", "_candy", "cracker", "sausage", "steak", "_jerky", "mushroom",
                                "pumpkin", "cabbage", "tomato", "carrot", "pickle", "honey", "sugar", "flour" }),
        ("Drinks",      new[] { "_water", "_soda", "juice", "_cola", "_beer", "_wine", "_milk", "_tea", "coffee",
                                "_drink", "canteen", "whiskey", "vodka", "energy_drink" }),
        ("Tools",       new[] { "hammer", "screwdriver", "wrench", "_saw", "lockpick", "pliers", "shovel", "_hoe",
                                "fishing", "_rod", "lighter", "matches", "compass", "_map", "binocular", "duct_tape",
                                "toolbox", "chainsaw", "_drill", "whetstone", "scissors", "pickaxe", "_file",
                                "sewing", "repair_kit", "cleaning_kit" }),
        ("Resources",   new[] { "_wood", "plank", "_log", "_metal", "scrap", "_nail", "_screw", "_rag", "_cloth",
                                "_rope", "_wire", "plastic", "rubber", "_stone", "_rock", "charcoal", "gunpowder",
                                "_fuel", "gasoline", "_oil", "fertilizer", "_seed", "_plant", "_ore", "ingot",
                                "_tape", "glue", "resin", "fiber", "leather", "_bark", "_coal" }),
        ("Containers",  new[] { "_crate", "_box", "chest", "locker", "barrel", "_container", "_safe", "cabinet",
                                "fridge", "cooler", "_case", "jerrycan", "_jar", "_tin" }),
    };

    private static readonly (string Category, string[] Keywords)[] VehicleRules =
    {
        ("Bicycles",  new[] { "bicycle", "citybike", "mountain_bike", "_bike" }),
        ("Boats",     new[] { "raft", "dinghy", "boat", "cruiser", "kayak", "canoe", "_ship" }),
        ("Aircraft",  new[] { "heli", "plane", "aircraft", "wolfswood", "glider" }),
        ("Bikes",     new[] { "motorcycle", "_quad", "atv", "scooter", "dirtbike" }),
        ("Cars",      new[] { "_car", "sedan", "suv", "truck", "pickup", "wagon", "_van", "jeep", "buggy",
                              "laika", "barba", "rager", "wolf", "tractor", "_bus" }),
    };

    /// <summary>The item categories in display order (not counting <see cref="All"/>/<see cref="Other"/>).</summary>
    public static IReadOnlyList<string> ItemCategories { get; } = ItemRules.Select(r => r.Category).ToList();

    /// <summary>The vehicle categories in display order.</summary>
    public static IReadOnlyList<string> VehicleCategories { get; } = VehicleRules.Select(r => r.Category).ToList();

    public static string OfItem(string code) => Classify(code, ItemRules);
    public static string OfVehicle(string code) => Classify(code, VehicleRules);

    private static string Classify(string code, (string Category, string[] Keywords)[] rules)
    {
        if (string.IsNullOrEmpty(code)) return Other;
        foreach (var (category, keywords) in rules)
            foreach (var kw in keywords)
                if (code.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    return category;
        return Other;
    }

    /// <summary>
    /// Counts how many of <paramref name="codes"/> fall in each category, returning the categories that have
    /// at least one, in canonical display order with <see cref="Other"/> last. Each entry is (name, count).
    /// </summary>
    public static List<(string Name, int Count)> Counts(IEnumerable<string> codes, bool vehicles = false)
    {
        var order = vehicles ? VehicleCategories : ItemCategories;
        var tally = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var code in codes)
        {
            var cat = vehicles ? OfVehicle(code) : OfItem(code);
            tally[cat] = tally.GetValueOrDefault(cat) + 1;
        }
        var result = new List<(string, int)>();
        foreach (var name in order)
            if (tally.TryGetValue(name, out var n)) result.Add((name, n));
        if (tally.TryGetValue(Other, out var other)) result.Add((Other, other));
        return result;
    }

    /// <summary>Keeps only the codes in <paramref name="category"/> (<see cref="All"/> keeps everything).</summary>
    public static IReadOnlyList<string> InCategory(IReadOnlyList<string> codes, string? category, bool vehicles = false)
    {
        if (string.IsNullOrEmpty(category) || category == All) return codes;
        return codes.Where(c => (vehicles ? OfVehicle(c) : OfItem(c)) == category).ToList();
    }
}
