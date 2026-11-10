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
        ("Magazines",    new[] { "magazine", "_mag", "mag_", "clip", "speedloader", "ammo_drum", "_drum" }),
        ("Ammunition",   new[] { "gauge", "ammo", "_cal", "cal_", "caliber", "bullet", "_round", "cartridge", "shell",
                                 "buckshot", "birdshot", "slug", "pellet", "arrow", "_bolt", "quarrel", "rocket",
                                 "grenade", "flashbang", "molotov", "_fmj", "_hp", "nato", "primer", "casing", "_flare", "buckball" }),
        ("Attachments",  new[] { "scope", "sight", "suppressor", "silencer", "foregrip", "_grip", "_barrel", "stock",
                                 "muzzle", "optic", "_rail", "bipod", "laser", "holosight", "reddot", "red_dot",
                                 "bayonet", "sling", "compensator", "flashhider", "picatinny", "mount_" }),
        ("Traps",        new[] { "trap", "_mine", "mine_", "snare", "beartrap", "landmine", "claymore" }),
        ("Building",     new[] { "bb_", "_wall", "foundation", "_fence", "_gate", "_roof", "_ramp", "_floor",
                                 "stairs", "doorframe", "flag_pole", "deployable", "squad_", "shelter", "_door",
                                 "window_", "_pillar", "_beam", "barricade", "watchtower", "_frame", "ladder",
                                 "sign_", "billboard", "awning", "canopy" }),
        ("Vehicle Parts", new[] { "wheel", "_tire", "_tyre", "spark_plug", "sparkplug", "headlight", "taillight",
                                 "bumper", "carburet", "radiator", "windshield", "windscreen", "muffler", "exhaust",
                                 "gearbox", "_brake", "clutch", "piston", "alternator", "car_door", "car_seat",
                                 "car_battery", "_axle", "fender", "hubcap", "chassis", "_engine", "fuel_pump",
                                 "oil_filter", "air_filter", "ignition", "spoiler", "_rim_", "camshaft" }),
        ("Medical",      new[] { "bandage", "medical", "morphine", "antibiotic", "painkiller", "firstaid", "first_aid",
                                 "syringe", "medkit", "suture", "disinfect", "iodine", "epinephrine", "splint",
                                 "_pill", "vitamin", "charcoal_pill", "tourniquet", "defibrillator", "adrenaline",
                                 "antiseptic", "gauze", "ointment", "_cream", "inhaler", "aspirin", "ibuprofen",
                                 "penicillin", "insulin", "sedative", "_serum", "vaccine", "blood_bag", "bandaid",
                                 "plaster", "cast_", "brace_", "laxative", "antacid", "_balm", "stimpack" }),
        ("Electronics",  new[] { "battery", "_radio", "phone", "laptop", "camera", "_gps", "electronic", "_cable",
                                 "circuit", "_chip", "flashlight", "_bulb", "generator", "walkie", "transmitter",
                                 "motherboard", "transistor", "resistor", "_fuse", "speaker", "antenna", "lightbulb",
                                 "_led", "sensor", "adapter", "charger", "_usb", "harddrive", "hard_drive", "monitor",
                                 "keyboard", "_clock", "drone", "capacitor", "diode", "receiver", "amplifier", "_lamp" }),
        ("Weapons",      new[] { "weapon", "rifle", "pistol", "revolver", "shotgun", "_smg", "sniper", "carbine", "_m16",
                                 "_m9", "ak47", "ak_", "ak74", "glock", "1911", "deagle", "desert_eagle", "mp5",
                                 "_uzi", "firearm", "handgun", "launcher", "_rpg", "crossbow", "_bow", "_gun" }),
        ("Melee",        new[] { "knife", "machete", "_axe", "axe_", "hatchet", "cleaver", "_bat", "baseball", "_sword",
                                 "katana", "spear", "sledge", "crowbar", "sickle", "_club", "nightstick", "baton",
                                 "kukri", "dagger", "shiv", "brass_knuckle", "mace", "tomahawk", "machair" }),
        ("Armor",        new[] { "armor", "armour", "_plate", "kevlar", "ballistic", "helmet", "_padding", "riot_",
                                 "pauldron", "greave", "gauntlet", "_shield" }),
        ("Backpacks",    new[] { "backpack", "_bag", "rucksack", "_pouch", "satchel", "duffel", "holster" }),
        ("Clothing",     new[] { "shirt", "_pants", "trouser", "jacket", "_coat", "boots", "_shoe", "sneaker", "_hat",
                                 "_cap", "glove", "_mask", "balaclava", "bandana", "_sock", "underwear", "_skirt",
                                 "_dress", "_belt", "necklace", "_ring", "glasses", "goggles", "hoodie", "sweater",
                                 "beanie", "scarf", "_vest", "jeans", "shorts", "uniform", "suit", "poncho", "hood",
                                 "_cape", "armband", "_watch", "apron", "overall", "coverall", "robe", "kilt",
                                 "legging", "stocking", "sandal", "slipper", "visor", "earring", "bracelet", "_tie_",
                                 "headband", "wristband", "cardigan", "blazer", "parka", "raincoat" }),
        ("Food",         new[] { "_food", "canned", "_meat", "_fish", "fruit", "vegetable", "bread", "_rice", "beans",
                                 "_mre", "ration", "_egg", "cheese", "apple", "banana", "_corn", "potato", "snack",
                                 "chocolate", "cereal", "_candy", "cracker", "sausage", "steak", "_jerky", "mushroom",
                                 "pumpkin", "cabbage", "tomato", "carrot", "pickle", "honey", "sugar", "flour",
                                 "soup", "stew", "pasta", "noodle", "pizza", "burger", "sandwich", "_pie", "_cake",
                                 "cookie", "donut", "pancake", "bacon", "salami", "peanut", "berry", "grape", "orange",
                                 "lemon", "_lime", "peach", "_pear", "plum", "melon", "lettuce", "onion", "garlic",
                                 "spinach", "broccoli", "yogurt", "butter", "_jam", "oatmeal", "granola", "popcorn",
                                 "biscuit", "marshmallow", "lollipop", "fries", "_bean", "dough", "meal_", "chips" }),
        ("Alcohol",      new[] { "_beer", "_wine", "whiskey", "whisky", "vodka", "_rum", "brandy", "tequila", "_gin",
                                 "moonshine", "liquor", "alcohol", "champagne", "cider", "_ale", "_mead", "bourbon",
                                 "scotch", "absinthe", "schnapps", "liqueur" }),
        ("Drinks",       new[] { "_water", "_soda", "juice", "_cola", "_milk", "_tea", "coffee", "_drink", "canteen",
                                 "energy_drink", "lemonade", "smoothie", "_shake", "kvass", "soda_" }),
        ("Tools",        new[] { "hammer", "screwdriver", "wrench", "_saw", "lockpick", "pliers", "shovel", "_hoe",
                                 "fishing", "_rod", "lighter", "matches", "compass", "_map", "binocular", "duct_tape",
                                 "toolbox", "chainsaw", "_drill", "whetstone", "scissors", "pickaxe", "_file",
                                 "sewing", "repair_kit", "cleaning_kit", "_pot", "_pan", "kettle", "_grill", "stove",
                                 "_hook", "needle", "_net", "_key", "keycard", "padlock", "mirror", "razor", "_brush",
                                 "funnel", "bucket", "canister", "tongs", "clamp", "handle", "trowel", "rake",
                                 "sharpener", "magnifying", "spanner", "ratchet" }),
        ("Resources",    new[] { "_wood", "plank", "_log", "_metal", "scrap", "_nail", "_screw", "_rag", "_cloth",
                                 "_rope", "_wire", "plastic", "rubber", "_stone", "_rock", "charcoal", "gunpowder",
                                 "_fuel", "gasoline", "_oil", "fertilizer", "_seed", "_plant", "_ore", "ingot",
                                 "_tape", "glue", "resin", "fiber", "leather", "_bark", "_coal",
                                 "colorcan", "color_can", "paint", "_dye", "_bar", "_sheet", "_pipe", "_tube",
                                 "_chain", "_spring", "cement", "concrete", "_sand", "_clay", "gravel", "brick",
                                 "twine", "_cord", "fabric", "canvas", "foam", "_tar", "_wax", "_lye", "_acid",
                                 "bleach", "solvent", "grease", "putty", "sealant", "adhesive", "solder", "_ash",
                                 "compost", "_sap", "_root", "_herb", "spice", "yarn", "string", "thread",
                                 "lubricant", "petrol", "diesel", "kerosene", "charcoal" }),
        ("Containers",   new[] { "_crate", "_box", "chest", "locker", "barrel", "_container", "_safe", "cabinet",
                                 "fridge", "cooler", "_case", "jerrycan", "_jar", "_tin", "_jug", "flask", "_vial",
                                 "_tank", "basket", "_sack", "_bin", "trunk", "wardrobe", "shelf", "drawer", "pallet" }),
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
