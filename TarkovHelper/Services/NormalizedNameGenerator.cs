using System.Net;
using System.Text.RegularExpressions;
using TarkovHelper.Models;

namespace TarkovHelper.Services
{
    /// <summary>
    /// Utility for generating and matching normalized names
    /// Used to match wiki item names to tarkov.dev API items
    /// </summary>
    public static class NormalizedNameGenerator
    {
        /// <summary>
        /// Manual overrides for quest normalizedNames that conflict with skills or other entities
        /// Maps API/generated normalizedName to wiki-style normalizedName
        /// </summary>
        private static readonly Dictionary<string, string> QuestNameOverrides = new(StringComparer.OrdinalIgnoreCase)
        {
            { "immunity", "immunity-quest" },  // Conflicts with Immunity skill
            { "reserve", "reserve-quest" },    // Conflicts with Reserve map
        };

        /// <summary>
        /// Manual overrides for wiki URLs where quest name differs from wiki page name
        /// Maps quest name to wiki page name (for renamed quests, disambiguation pages, etc.)
        /// </summary>
        private static readonly Dictionary<string, string> WikiUrlOverrides = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Immunity", "Immunity_(quest)" },
            { "Immunity (quest)", "Immunity_(quest)" },
            { "Reserve", "Reserve_(quest)" },
            { "Reserve (quest)", "Reserve_(quest)" },
            { "A Shooter Born in Heaven", "Shooter_Born_in_Heaven" },
            { "Ambulance", "First_Aid" },
            { "Ambulances Again", "Paramedic" },
            { "Beyond the Red Meat - Part 1", "Beyond_the_Red_Meat" },
            { "Beyond the Red Meat - Part 2", "The_Secret_Recipe" },
            { "BP Depot", "Oil_Run" },
            { "Cargo X - Part 1", "Cargo_X" },
            { "Cargo X - Part 2", "Tracker" },
            { "Cargo X - Part 4", "Gifts_from_Tarkov" },
            { "Colleagues - Part 1", "Colleagues" },
            { "Colleagues - Part 2", "Tarkov-Style_Diplomacy" },
            { "Colleagues - Part 3", "A_Difficult_Choice" },
            { "Database - Part 1", "Inventory_Files" },
            { "Database - Part 2", "A_Big_Loss" },
            { "Developer's Secrets - Part 1", "Corporate_Perks" },
            { "Developer's Secrets - Part 2", "The_Secret_to_Productivity" },
            { "Easy Job - Part 1", "Easy_Job" },
            { "Farming - Part 1", "Playing_the_Market" },
            { "Farming - Part 3", "Farming" },
            { "Farming - Part 4", "Semiconductor_Crisis" },
            { "Friend From the West - Part 1", "Friend_From_the_West" },
            { "Gendarmerie - District Patrol", "District_Patrol" },
            { "Glory to CPSU - Part 2", "Glory_to_CPSU" },
            { "Gunsmith - Old Friend's Request", "Gunsmith_Master_-_Part_12" },
            { "Gunsmith - Part 1", "Gunsmith_-_MP-133" },
            { "Gunsmith - Part 2", "Gunsmith_-_AKS-74U" },
            { "Gunsmith - Part 3", "Gunsmith_-_HK_MP5" },
            { "Gunsmith - Part 4", "Gunsmith_-_OP-SKS" },
            { "Gunsmith - Part 5", "Gunsmith_-_Model_870" },
            { "Gunsmith - Part 6", "Gunsmith_-_AKM" },
            { "Gunsmith - Part 7", "Gunsmith_-_M4A1" },
            { "Gunsmith - Part 8", "Gunsmith_-_AKS-74N" },
            { "Gunsmith - Part 9", "Gunsmith_-_P226R" },
            { "Gunsmith - Part 10", "Gunsmith_-_AK-105" },
            { "Gunsmith - Part 11", "Gunsmith_-_Vector_9x19" },
            { "Gunsmith - Part 12", "Gunsmith_-_MPX" },
            { "Gunsmith - Part 13", "Gunsmith_-_AS_VAL" },
            { "Gunsmith - Part 14", "Gunsmith_Master_-_Part_1" },
            { "Gunsmith - Part 15", "Gunsmith_Master_-_Part_2" },
            { "Gunsmith - Part 16", "Gunsmith_Master_-_Part_3" },
            { "Gunsmith - Part 17", "Gunsmith_Master_-_Part_4" },
            { "Gunsmith - Part 18", "Gunsmith_Master_-_Part_5" },
            { "Gunsmith - Part 19", "Gunsmith_Master_-_Part_6" },
            { "Gunsmith - Part 20", "Gunsmith_Master_-_Part_7" },
            { "Gunsmith - Part 21", "Gunsmith_Master_-_Part_8" },
            { "Gunsmith - Part 22", "Gunsmith_Master_-_Part_9" },
            { "Gunsmith - Part 23", "Gunsmith_Master_-_Part_10" },
            { "Gunsmith - Part 24", "Gunsmith_Master_-_Part_11" },
            { "Gunsmith - Part 25", "Gunsmith_Master_-_Part_13" },
            { "Getting Acquainted", "To_the_Light_-_Getting_Acquainted" },
            { "Half Empty", "Half-Empty" },
            { "House Arrest - Part 1", "House_Arrest" },
            { "Hunter", "All_This_Filth..." },
            { "Inventory Check", "Inventory_Files" },
            { "Lend-Lease - Part 1", "Metal_Birds" },
            { "Lend-Lease - Part 2", "From_Hand_to_Hand" },
            { "Living High is Not a Crime - Part 1", "Living_High_is_Not_a_Crime" },
            { "Living High is Not a Crime - Part 2", "Living_High_is_Not_a_Crime" },
            { "New Day, New Paths", "New_Paths" },
            { "No Offence", "Ironclad_Proof" },
            { "No Questions Asked", "Special_Order" },
            { "Operation Aquarius - Part 1", "Operation_Aquarius" },
            { "Operation Aquarius - Part 2", "Operation_Aquarius" },
            { "Painkiller", "Charity" },
            { "Pets Won't Need It - Part 1", "Pets_Won%27t_Need_It" },
            { "Sales Night", "Pathfinder" },
            { "Sanitary Standards - Part 1", "Sanitary_Standards" },
            { "Signal - Part 1", "Ill-Wisher" },
            { "Signal - Part 2", "Rat_Hunting" },
            { "Spa Tour - Part 1", "One-Way_Ticket" },
            { "Spa Tour - Part 3", "Fuel_Shortage" },
            { "Spa Tour - Part 4", "I_Need_More_Power" },
            { "Spa Tour - Part 5", "Master_Key" },
            { "Spa Tour - Part 7", "Chemical_Experiments" },
            { "Swift One", "Swift" },
            { "Test Drive - Part 1", "The_Tarkov_Import" },
            { "Test Drive - Part 2", "Power_of_Persuasion" },
            { "Test Drive - Part 3", "Job_for_a_Patriot" },
            { "Test Drive - Part 4", "Getting_Some_Air" },
            { "Test Drive - Part 5", "Easy-Breezy" },
            { "Test Drive - Part 6", "Unique_Experience" },
            { "The Blood of War - Part 1", "Fuel_Crisis" },
            { "The Blood of War - Part 3", "Small_Things,_Big_Help" },
            { "The Bunker - Part 1", "The_Bunker" },
            { "The Cult - Part 1", "The_Cult" },
            { "The Cult - Part 2", "The_Cult" },
            { "The Huntsman Path - Eraser - Part 1", "The_Huntsman_Path_-_Eraser" },
            { "The Huntsman Path - Eraser - Part 2", "The_Huntsman_Path_-_Eraser" },
            { "The Huntsman Path - Evil Watchman", "The_Huntsman_Path_-_Angry_Watchman" },
            { "The Tarkov Shooter - Part 8", "The_Tarkov_Shooter_-_Part_7" },
            { "Trust Regain", "Aid_Stations" },
            { "Vitamins - Part 1", "Vitamins" },
            { "Vitamins - Part 2", "Vitamins" },
            { "New Beginning (Prestige 1)", "New_Beginning_(Prestige_1)" },
            { "New Beginning (Prestige 2)", "New_Beginning_(Prestige_2)" },
            { "New Beginning (Prestige 3)", "New_Beginning_(Prestige_3)" },
            { "New Beginning (Prestige 4)", "New_Beginning_(Prestige_4)" },
            { "New Beginning (Prestige 5)", "New_Beginning_(Prestige_5)" },
            { "New Beginning (Prestige 6)", "New_Beginning_(Prestige_6)" }
        };

        /// <summary>
        /// Apply quest-specific normalizedName overrides
        /// </summary>
        public static string ApplyQuestOverride(string normalizedName)
        {
            if (string.IsNullOrEmpty(normalizedName))
                return normalizedName;

            return QuestNameOverrides.TryGetValue(normalizedName, out var overrideName)
                ? overrideName
                : normalizedName;
        }

        /// <summary>
        /// Get wiki page name for a quest (handles disambiguation pages and renamed quests)
        /// </summary>
        public static string GetWikiPageName(string questName)
        {
            if (string.IsNullOrEmpty(questName))
                return questName;

            return WikiUrlOverrides.TryGetValue(questName, out var wikiPageName)
                ? wikiPageName
                : questName;
        }

        /// <summary>
        /// Get the full Wiki URL for a quest, using its stored WikiPageLink if available,
        /// or falling back to the normalized wiki page name without breaking parentheses or special characters.
        /// </summary>
        public static string GetQuestWikiUrl(TarkovTask? task, string? fallbackQuestName = null)
        {
            if (!string.IsNullOrEmpty(task?.WikiPageLink))
            {
                return task.WikiPageLink;
            }

            var name = !string.IsNullOrEmpty(task?.Name) ? task.Name : (fallbackQuestName ?? string.Empty);
            if (string.IsNullOrEmpty(name))
                return "https://escapefromtarkov.fandom.com/wiki/Quests";

            var wikiPageName = GetWikiPageName(name);
            var encoded = Uri.EscapeDataString(wikiPageName.Replace(" ", "_"))
                .Replace("%28", "(")
                .Replace("%29", ")")
                .Replace("%27", "'");
            return $"https://escapefromtarkov.fandom.com/wiki/{encoded}";
        }

        /// <summary>
        /// Generate a normalized name from a wiki or display name
        /// Matches tarkov.dev API normalizedName format
        /// </summary>
        /// <param name="name">Original name (e.g., "MP-133 12ga pump-action shotgun")</param>
        /// <returns>Normalized name (e.g., "mp-133-12ga-pump-action-shotgun")</returns>
        public static string Generate(string name)
        {
            if (string.IsNullOrEmpty(name))
                return string.Empty;

            // Decode HTML entities first (e.g., &#39; -> ')
            var decoded = WebUtility.HtmlDecode(name);

            var result = decoded
                .ToLowerInvariant()
                .Replace(" ", "-")
                .Replace("'", "")      // Remove apostrophes: "Huntsman's Path" -> "huntsmans-path"
                .Replace("'", "")      // Remove right single quotation mark (U+2019)
                .Replace("'", "")      // Remove left single quotation mark (U+2018)
                .Replace(":", "")      // Remove colons
                .Replace("?", "")      // Remove question marks
                .Replace(".", "")      // Remove periods
                .Replace(",", "")      // Remove commas
                .Replace("!", "")      // Remove exclamation marks
                .Replace("\"", "")     // Remove quotes
                .Replace("(", "")      // Remove parentheses
                .Replace(")", "")
                .Replace("[", "")      // Remove brackets
                .Replace("]", "")
                .Replace("&", "and");  // Replace ampersand with "and"

            // Fix multiple consecutive hyphens (e.g., "---" → "-")
            while (result.Contains("--"))
                result = result.Replace("--", "-");

            return result.Trim('-');   // Remove leading/trailing hyphens
        }

        /// <summary>
        /// Generate alternative normalized names for fuzzy matching
        /// Useful when wiki item names don't exactly match API names
        /// </summary>
        /// <param name="name">Original name</param>
        /// <returns>List of possible normalized names to try</returns>
        public static List<string> GenerateAlternatives(string name)
        {
            var alternatives = new List<string>();
            var primary = Generate(name);

            if (!string.IsNullOrEmpty(primary))
                alternatives.Add(primary);

            // Alternative 1: Remove parenthetical text
            var withoutParentheses = Regex.Replace(name, @"\s*\([^)]*\)", "");
            var normalized1 = Generate(withoutParentheses);
            if (!string.IsNullOrEmpty(normalized1) && !alternatives.Contains(normalized1))
                alternatives.Add(normalized1);

            // Alternative 2: Expand common abbreviations
            var expanded = name
                .Replace("12ga", "12-gauge")
                .Replace("7.62", "762")
                .Replace("5.56", "556")
                .Replace("9x19", "9x19mm");
            var normalized2 = Generate(expanded);
            if (!string.IsNullOrEmpty(normalized2) && !alternatives.Contains(normalized2))
                alternatives.Add(normalized2);

            // Alternative 3: For ammo - try without type suffix (NATO, M80, etc.)
            // e.g., "7.62x51mm NATO" -> "762x51mm"
            var ammoWithoutType = Regex.Replace(name, @"\s+(NATO|M\d+|FMJ|HP|AP|SP|BT|T|PS|PP|BP|BS|BT|US|PRS|SNB|LPS|MAI|PMM|RIP|Quakemaker|Warmage|Action\s*SX|Luger\s*CCI|Green\s*Tracer|Red\s*Tracer|Tracer)$", "", RegexOptions.IgnoreCase);
            var normalized3 = Generate(ammoWithoutType);
            if (!string.IsNullOrEmpty(normalized3) && !alternatives.Contains(normalized3))
                alternatives.Add(normalized3);

            // Alternative 4: Try with "mm" removed for caliber matching
            // e.g., "762x51mm" -> "762x51"
            var withoutMm = primary.Replace("mm", "");
            if (!string.IsNullOrEmpty(withoutMm) && !alternatives.Contains(withoutMm))
                alternatives.Add(withoutMm);

            // Alternative 5: Handle dogtag name variations
            // "BEAR Dogtag" -> "dogtag-bear", "bear-dogtag"
            if (name.Contains("Dogtag", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Dog tag", StringComparison.OrdinalIgnoreCase))
            {
                var dogtag1 = "dogtag-bear";
                var dogtag2 = "dogtag-usec";
                var dogtag3 = "bear-dogtag";
                var dogtag4 = "usec-dogtag";

                if (name.Contains("BEAR", StringComparison.OrdinalIgnoreCase))
                {
                    if (!alternatives.Contains(dogtag1)) alternatives.Add(dogtag1);
                    if (!alternatives.Contains(dogtag3)) alternatives.Add(dogtag3);
                }
                if (name.Contains("USEC", StringComparison.OrdinalIgnoreCase))
                {
                    if (!alternatives.Contains(dogtag2)) alternatives.Add(dogtag2);
                    if (!alternatives.Contains(dogtag4)) alternatives.Add(dogtag4);
                }
            }

            return alternatives;
        }

        /// <summary>
        /// Normalize a quest name for file matching
        /// Handles special characters that may be encoded differently
        /// </summary>
        /// <param name="name">Quest name</param>
        /// <returns>Filename-safe normalized name</returns>
        public static string NormalizeForFilename(string name)
        {
            return name
                // Normalize various apostrophe types to standard single quote
                .Replace("'", "'")  // RIGHT SINGLE QUOTATION MARK (U+2019) -> '
                .Replace("'", "'")  // LEFT SINGLE QUOTATION MARK (U+2018) -> '
                .Replace("ʼ", "'")  // MODIFIER LETTER APOSTROPHE -> '
                // Replace invalid filename characters with underscore
                .Replace("?", "_")
                .Replace(":", "_")
                .Replace("*", "_")
                .Replace("\"", "_")
                .Replace("<", "_")
                .Replace(">", "_")
                .Replace("|", "_");
        }
    }
}
