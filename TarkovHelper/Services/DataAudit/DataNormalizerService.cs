using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using TarkovHelper.Models.DataAudit;

namespace TarkovHelper.Services.DataAudit
{
    /// <summary>
    /// Service that normalizes, cleans, and joins extracted MediaWiki and community data
    /// into structured, database-compatible NormalizedAuditData matching the SQLite schema of tarkov_data.db.
    /// </summary>
    public class DataNormalizerService
    {
        #region Canonical Station Maps

        private static readonly Dictionary<string, string> StationNameToBsgId = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Heating", "5d388e97081959000a123acf" },
            { "Generator", "5d3b396e33c48f02b81cd9f3" },
            { "Vents", "5d473c1e081959000e530190" },
            { "Security", "5d484fb3654e7600681d9314" },
            { "Lavatory", "5d484fba654e7600691aadf7" },
            { "Stash", "5d484fc0654e76006657e0ab" },
            { "Water Collector", "5d484fc8654e760065037abf" },
            { "Medstation", "5d484fcd654e7668ec2ec322" },
            { "Nutrition Unit", "5d484fd1654e76006732bf2e" },
            { "Rest Space", "5d484fd6654e76051d3cc791" },
            { "Workbench", "5d484fda654e7600681d9315" },
            { "Intelligence Center", "5d484fdf654e7600691aadf8" },
            { "Shooting Range", "5d484fe3654e76006657e0ac" },
            { "Library", "5d494a0e5b56502f18c98a02" },
            { "Scav Case", "5d494a175b56502f18c98a04" },
            { "Illumination", "5d494a205b56502f18c98a06" },
            { "Hall of Fame", "5d494a295b56502f18c98a08" },
            { "Air Filtering Unit", "5d494a315b56502f18c98a0a" },
            { "Solar Power", "5d494a385b56502f18c98a0c" },
            { "Booze Generator", "5d494a3f5b56502f18c98a0e" },
            { "Bitcoin Farm", "5d494a445b56502f18c98a10" },
            { "Gym", "6377a9b9a93bde8fa30eb79a" },
            { "Defective Wall", "637b39f02e873739ec490215" },
            { "Weapon Rack", "63db64cbf9963741dc0d741f" },
            { "Gear Rack", "65e5bb1713227bb7690cea0a" },
            { "Cultist Circle", "667298e75ea6b4493c08f266" },

            // Hyphenated/kebab-case normalized names
            { "water-collector", "5d484fc8654e760065037abf" },
            { "nutrition-unit", "5d484fd1654e76006732bf2e" },
            { "rest-space", "5d484fd6654e76051d3cc791" },
            { "intelligence-center", "5d484fdf654e7600691aadf8" },
            { "shooting-range", "5d484fe3654e76006657e0ac" },
            { "scav-case", "5d494a175b56502f18c98a04" },
            { "hall-of-fame", "5d494a295b56502f18c98a08" },
            { "air-filtering-unit", "5d494a315b56502f18c98a0a" },
            { "solar-power", "5d494a385b56502f18c98a0c" },
            { "booze-generator", "5d494a3f5b56502f18c98a0e" },
            { "bitcoin-farm", "5d494a445b56502f18c98a10" },
            { "defective-wall", "637b39f02e873739ec490215" },
            { "weapon-rack", "63db64cbf9963741dc0d741f" },
            { "gear-rack", "65e5bb1713227bb7690cea0a" },
            { "cultist-circle", "667298e75ea6b4493c08f266" }
        };

        private static readonly Dictionary<string, string> TraderNameAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Pavel Yegorovich Romanenko", "Prapor" },
            { "Elvira Khabibullina", "Therapist" },
            { "Alexander Fyodorovich Kiselyov", "Skier" },
            { "Abramyan Arshavir Sarkisivich", "Ragman" },
            { "Arshavir Sarkisivich", "Ragman" }
        };

        #endregion

        #region Normalization Entry Point

        /// <summary>
        /// Interface contract: Normalizes and joins raw wiki quests and community payload.
        /// </summary>
        public NormalizedAuditData Normalize(List<RawWikiQuest> wikiQuests, CommunityDataPayload communityData)
        {
            var auditData = new NormalizedAuditData
            {
                NormalizedAt = DateTime.UtcNow,
                SourceSummary = $"MediaWiki ({wikiQuests.Count} quests) + {communityData.Source} ({communityData.HideoutStations.Count} stations, {communityData.Quests.Count} quests)"
            };

            // Build lookups for community quests
            var communityByNormalizedName = new Dictionary<string, CommunityQuest>(StringComparer.OrdinalIgnoreCase);
            var communityByName = new Dictionary<string, CommunityQuest>(StringComparer.OrdinalIgnoreCase);

            foreach (var cq in communityData.Quests)
            {
                if (!string.IsNullOrEmpty(cq.NormalizedName) && !communityByNormalizedName.ContainsKey(cq.NormalizedName))
                {
                    communityByNormalizedName[cq.NormalizedName] = cq;
                }
                if (!string.IsNullOrEmpty(cq.Name) && !communityByName.ContainsKey(cq.Name))
                {
                    communityByName[cq.Name] = cq;
                }
            }

            // Map wiki quest titles to deterministic Base64 IDs
            var questTitleToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var questTitleToNormalizedName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var wq in wikiQuests)
            {
                var qId = ComputeQuestId(wq.Title);
                var normName = GenerateNormalizedName(wq.Title);
                questTitleToId[wq.Title] = qId;
                questTitleToNormalizedName[wq.Title] = normName;

                // Index without parenthetical suffix, e.g. "The Labyrinth (story chapter)" -> "The Labyrinth"
                if (wq.Title.Contains('(') && wq.Title.Contains(')'))
                {
                    var stripped = Regex.Replace(wq.Title, @"\s*\([^)]*\)", "").Trim();
                    if (!string.IsNullOrEmpty(stripped) && !questTitleToId.ContainsKey(stripped))
                    {
                        questTitleToId[stripped] = qId;
                        questTitleToNormalizedName[stripped] = normName;
                    }
                }
            }

            // Also map community quest titles to Base64 IDs in case wiki missed a title
            foreach (var cq in communityData.Quests)
            {
                if (!questTitleToId.ContainsKey(cq.Name))
                {
                    questTitleToId[cq.Name] = ComputeQuestId(cq.Name);
                    questTitleToNormalizedName[cq.Name] = GenerateNormalizedName(cq.Name);
                }
            }

            // 1. Normalize Quests
            var processedQuestIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var wq in wikiQuests)
            {
                var qId = questTitleToId[wq.Title];
                if (!processedQuestIds.Add(qId))
                    continue;

                var normName = questTitleToNormalizedName[wq.Title];

                // Attempt to match community quest for BSG ID and multi-language names
                CommunityQuest? matchedCq = null;
                if (communityByNormalizedName.TryGetValue(normName, out var byNorm))
                {
                    matchedCq = byNorm;
                }
                else if (communityByName.TryGetValue(wq.Title, out var byNm))
                {
                    matchedCq = byNm;
                }

                string? bsgId = null;
                if (matchedCq != null && IsHexMongoId(matchedCq.Id))
                {
                    bsgId = matchedCq.Id;
                }

                var canonicalUrl = ComputeCanonicalWikiUrl(wq.Title);

                // Disambiguate story detection:
                var cleanWikitext = string.IsNullOrEmpty(wq.Wikitext)
                    ? null
                    : Regex.Replace(wq.Wikitext, @"<!--.*?-->", "", RegexOptions.Singleline);

                bool hasWikitextStoryEvidence = false;
                if (!string.IsNullOrEmpty(cleanWikitext))
                {
                    // c) MediaWiki Category tag
                    if (cleanWikitext.Contains("Category:Story chapters", StringComparison.OrdinalIgnoreCase) ||
                        cleanWikitext.Contains("Category:Story_chapters", StringComparison.OrdinalIgnoreCase))
                    {
                        hasWikitextStoryEvidence = true;
                    }
                    // d) Template match: {{Infobox story chapter or {{Infobox story_chapter
                    else if (Regex.IsMatch(cleanWikitext, @"\{\{Infobox[\s_]+story[\s_]+chapter", RegexOptions.IgnoreCase))
                    {
                        hasWikitextStoryEvidence = true;
                    }
                    // e) Explicit lead definition pattern: '''<Title>''' is a [[Story chapters|story chapter]]
                    else if (!string.IsNullOrEmpty(wq.Title))
                    {
                        var cleanTitle = Regex.Replace(wq.Title.Trim(), @"\s*\([^)]*\)", "").Trim();
                        if (Regex.IsMatch(cleanWikitext, @"'''" + Regex.Escape(cleanTitle) + @"'''\s+is\s+a\s+\[\[Story chapters\|story chapter\]\]", RegexOptions.IgnoreCase) ||
                            Regex.IsMatch(cleanWikitext, @"'''" + Regex.Escape(wq.Title.Trim()) + @"'''\s+is\s+a\s+\[\[Story chapters\|story chapter\]\]", RegexOptions.IgnoreCase))
                        {
                            hasWikitextStoryEvidence = true;
                        }
                    }
                }

                bool isKnownTitle = IsStoryChapterTitle(wq.Title);
                bool isRecognizedNonStoryTrader = IsRecognizedNonStoryTrader(wq.Trader ?? matchedCq?.Trader);

                // Check if another quest in rawQuests has the same title disambiguated with "(story chapter)"
                bool hasDisambiguatedStorySibling = !string.IsNullOrWhiteSpace(wq.Title) && wikiQuests.Any(q =>
                    !ReferenceEquals(q, wq) &&
                    q.IsStoryChapter &&
                    (string.Equals(q.Title, wq.Title + " (story chapter)", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(q.Title, Regex.Replace(wq.Title, @"\s*\([^)]*\)", "").Trim() + " (story chapter)", StringComparison.OrdinalIgnoreCase)));

                bool isStory = false;
                if (wq.IsStoryChapter)
                {
                    isStory = true;
                }
                else if (hasWikitextStoryEvidence)
                {
                    isStory = true;
                }
                else if (isKnownTitle)
                {
                    if (isRecognizedNonStoryTrader)
                    {
                        // If it has an explicit recognized non-story trader, do NOT overwrite if:
                        // - A disambiguated story sibling exists (e.g. "The Labyrinth (story chapter)"), OR
                        // - Title is not strictly in KnownStoryChapters, OR
                        // - wq.IsStoryChapter is false and no story wikitext evidence exists
                        isStory = false;
                    }
                    else
                    {
                        isStory = !hasDisambiguatedStorySibling;
                    }
                }

                var trader = isStory ? "Story" : NormalizeTrader(wq.Trader ?? matchedCq?.Trader);
                var faction = isStory ? null : NormalizeFaction(wq.Faction ?? matchedCq?.Faction);
                var minLevel = isStory ? (wq.MinPlayerLevel ?? matchedCq?.MinPlayerLevel ?? 1) : (wq.MinPlayerLevel ?? matchedCq?.MinPlayerLevel);
                var kappaReq = isStory ? false : wq.KappaRequired;

                var nq = new NormalizedQuest
                {
                    Id = qId,
                    BsgId = bsgId,
                    Name = wq.Title,
                    NameEN = wq.Title,
                    NameKO = matchedCq?.NameKo,
                    NameJA = matchedCq?.NameJa,
                    WikiPageLink = canonicalUrl,
                    Trader = trader,
                    Location = wq.Location,
                    MinLevel = minLevel,
                    MinScavKarma = wq.MinScavKarma,
                    KappaRequired = kappaReq,
                    Faction = faction,
                    NormalizedName = normName,
                    RequiredEdition = wq.RequiredEdition,
                    ExcludedEdition = wq.ExcludedEdition,
                    RequiredPrestigeLevel = wq.RequiredPrestigeLevel,
                    RequiredDecodeCount = wq.RequiredDecodeCount,
                    Maps = ParseLocationToMaps(wq.Location),
                    Objectives = wq.Objectives.Select(o => o.Description).ToList()
                };

                auditData.Quests.Add(nq);

                // 2. Normalize Quest Requirements (Prerequisites)
                var seenPrereqKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var prereq in wq.Prerequisites)
                {
                    if (string.IsNullOrWhiteSpace(prereq.QuestTitle))
                        continue;

                    // Resolve required quest ID
                    string? reqQuestId = null;
                    string matchedReqTitle = prereq.QuestTitle;

                    if (questTitleToId.TryGetValue(prereq.QuestTitle, out var foundId))
                    {
                        reqQuestId = foundId;
                    }
                    else
                    {
                        // Fuzzy check (e.g. without suffix / prefix)
                        var strippedTitle = Regex.Replace(prereq.QuestTitle, @"\s*\([^)]*\)", "").Trim();
                        if (questTitleToId.TryGetValue(strippedTitle, out var foundStrippedId))
                        {
                            reqQuestId = foundStrippedId;
                        }
                        else
                        {
                            foreach (var kvp in questTitleToId)
                            {
                                if (string.Equals(kvp.Key, strippedTitle, StringComparison.OrdinalIgnoreCase))
                                {
                                    reqQuestId = kvp.Value;
                                    matchedReqTitle = kvp.Key;
                                    break;
                                }
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(reqQuestId))
                    {
                        // Compute standard Base64 ID for the title
                        reqQuestId = ComputeQuestId(prereq.QuestTitle);
                    }

                    var dedupeKey = $"{qId}_{reqQuestId}";
                    if (!seenPrereqKeys.Add(dedupeKey))
                        continue;

                    var reqId = ComputeRequirementId(qId, reqQuestId, prereq.GroupId);

                    auditData.QuestRequirements.Add(new NormalizedQuestRequirement
                    {
                        Id = reqId,
                        QuestId = qId,
                        QuestName = wq.Title,
                        RequiredQuestId = reqQuestId,
                        RequiredQuestName = matchedReqTitle,
                        RequirementType = NormalizeRequirementType(prereq.RequirementType),
                        DelayMinutes = prereq.DelayMinutes,
                        GroupId = prereq.GroupId
                    });
                }

                // 3. Normalize Quest Required Items
                var sortOrder = 0;
                var seenItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var itm in wq.RequiredItems)
                {
                    if (string.IsNullOrWhiteSpace(itm.ItemName))
                        continue;

                    var cleanItemName = itm.ItemName.Trim();
                    if (string.IsNullOrEmpty(cleanItemName))
                        continue;

                    var dedupeKey = $"{cleanItemName}_{itm.RequiresFIR}_{itm.RequirementType}";
                    if (!seenItems.Add(dedupeKey))
                        continue;

                    // Column ItemId in QuestRequiredItems references Base64 Wiki URL (Items.Id)
                    var itemWikiId = ComputeItemWikiId(cleanItemName);
                    var itemId = ComputeQuestItemId(qId, cleanItemName, itm.RequirementType, itm.RequiresFIR, sortOrder);

                    auditData.QuestRequiredItems.Add(new NormalizedQuestRequiredItem
                    {
                        Id = itemId,
                        QuestId = qId,
                        QuestName = wq.Title,
                        ItemId = itemWikiId,
                        ItemName = cleanItemName,
                        Count = itm.Count > 0 ? itm.Count : 1,
                        RequiresFIR = itm.RequiresFIR,
                        RequirementType = itm.RequirementType,
                        SortOrder = sortOrder++,
                        DogtagMinLevel = itm.DogtagMinLevel,
                        DogtagFaction = itm.DogtagFaction
                    });
                }

                // If wiki had no items, check if community quest had required items
                if (wq.RequiredItems.Count == 0 && matchedCq != null && matchedCq.ItemRequirements.Count > 0)
                {
                    foreach (var cir in matchedCq.ItemRequirements)
                    {
                        if (string.IsNullOrWhiteSpace(cir.ItemName))
                            continue;

                        var cleanItemName = cir.ItemName.Trim();
                        var dedupeKey = $"{cleanItemName}_{cir.FoundInRaid}_{cir.RequirementType}";
                        if (!seenItems.Add(dedupeKey))
                            continue;

                        var itemWikiId = ComputeItemWikiId(cleanItemName);
                        var itemId = ComputeQuestItemId(qId, cleanItemName, cir.RequirementType, cir.FoundInRaid, sortOrder);

                        auditData.QuestRequiredItems.Add(new NormalizedQuestRequiredItem
                        {
                            Id = itemId,
                            QuestId = qId,
                            QuestName = wq.Title,
                            ItemId = itemWikiId,
                            ItemName = cleanItemName,
                            Count = cir.Count > 0 ? cir.Count : 1,
                            RequiresFIR = cir.FoundInRaid,
                            RequirementType = cir.RequirementType,
                            SortOrder = sortOrder++
                        });
                    }
                }

                // 4. Normalize Quest Trader Loyalty Level Requirements
                int traderSort = 0;
                var seenTraderReqs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var wtr in wq.TraderRequirements)
                {
                    var tName = NormalizeTrader(wtr.TraderName);
                    if (string.IsNullOrEmpty(tName)) continue;
                    var dedupe = $"{tName}_{wtr.RequiredLevel}_{wtr.CompareMethod}";
                    if (!seenTraderReqs.Add(dedupe)) continue;

                    var trId = ComputeTraderRequirementId(qId, tName, wtr.RequiredLevel);
                    var normTr = new NormalizedQuestTraderRequirement
                    {
                        Id = trId,
                        QuestId = qId,
                        QuestName = wq.Title,
                        TraderName = tName,
                        RequirementType = wtr.RequirementType ?? "Level",
                        CompareMethod = wtr.CompareMethod ?? ">=",
                        RequiredLevel = wtr.RequiredLevel,
                        SortOrder = traderSort++
                    };
                    nq.TraderRequirements.Add(normTr);
                    auditData.QuestTraderRequirements.Add(normTr);
                }

                if (wq.TraderRequirements.Count == 0 && matchedCq != null && matchedCq.TraderRequirements.Count > 0)
                {
                    foreach (var ctr in matchedCq.TraderRequirements)
                    {
                        var tName = NormalizeTrader(ctr.TraderName);
                        if (string.IsNullOrEmpty(tName)) continue;
                        var dedupe = $"{tName}_{ctr.Value}_{ctr.CompareMethod}";
                        if (!seenTraderReqs.Add(dedupe)) continue;

                        var trId = ComputeTraderRequirementId(qId, tName, ctr.Value);
                        var normTr = new NormalizedQuestTraderRequirement
                        {
                            Id = trId,
                            QuestId = qId,
                            QuestName = wq.Title,
                            TraderId = ctr.TraderId,
                            TraderName = tName,
                            RequirementType = ctr.RequirementType ?? "Level",
                            CompareMethod = ctr.CompareMethod ?? ">=",
                            RequiredLevel = ctr.Value,
                            SortOrder = traderSort++
                        };
                        nq.TraderRequirements.Add(normTr);
                        auditData.QuestTraderRequirements.Add(normTr);
                    }
                }

                // 5. Normalize Quest Pool Requirements
                var seenPoolReqs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var wpr in wq.PoolRequirements)
                {
                    var pTrader = NormalizeTrader(wpr.PoolTrader);
                    if (string.IsNullOrEmpty(pTrader)) pTrader = trader;
                    var dedupe = $"{pTrader}_{wpr.PoolTraderLL}_{wpr.RequiredCompletedCount}";
                    if (!seenPoolReqs.Add(dedupe)) continue;

                    var prId = ComputePoolRequirementId(qId, pTrader, wpr.PoolTraderLL, wpr.RequiredCompletedCount);
                    var normPr = new NormalizedQuestPoolRequirement
                    {
                        Id = prId,
                        QuestId = qId,
                        QuestName = wq.Title,
                        PoolTrader = pTrader,
                        PoolTraderLL = wpr.PoolTraderLL,
                        RequiredCompletedCount = wpr.RequiredCompletedCount,
                        Description = wpr.Description ?? $"Complete at least {wpr.RequiredCompletedCount} {pTrader}{(wpr.PoolTraderLL.HasValue ? $" LL{wpr.PoolTraderLL}" : "")} quests"
                    };
                    nq.PoolRequirements.Add(normPr);
                    auditData.QuestPoolRequirements.Add(normPr);
                }

                if (wq.PoolRequirements.Count == 0 && matchedCq != null && matchedCq.PoolRequirements.Count > 0)
                {
                    foreach (var cpr in matchedCq.PoolRequirements)
                    {
                        var pTrader = NormalizeTrader(cpr.PoolTrader);
                        if (string.IsNullOrEmpty(pTrader)) pTrader = trader;
                        var dedupe = $"{pTrader}_{cpr.PoolTraderLL}_{cpr.RequiredCompletedCount}";
                        if (!seenPoolReqs.Add(dedupe)) continue;

                        var prId = ComputePoolRequirementId(qId, pTrader, cpr.PoolTraderLL, cpr.RequiredCompletedCount);
                        var normPr = new NormalizedQuestPoolRequirement
                        {
                            Id = prId,
                            QuestId = qId,
                            QuestName = wq.Title,
                            PoolTrader = pTrader,
                            PoolTraderLL = cpr.PoolTraderLL,
                            RequiredCompletedCount = cpr.RequiredCompletedCount,
                            Description = cpr.Description ?? $"Complete at least {cpr.RequiredCompletedCount} {pTrader}{(cpr.PoolTraderLL.HasValue ? $" LL{cpr.PoolTraderLL}" : "")} quests"
                        };
                        nq.PoolRequirements.Add(normPr);
                        auditData.QuestPoolRequirements.Add(normPr);
                    }
                }
            }

            // 4. Normalize Hideout Stations & Levels
            NormalizeHideoutData(auditData, communityData);

            return auditData;
        }

        /// <summary>
        /// Normalizes raw TarkovTracker task data combined with community Hideout stations and levels
        /// into structured, database-compatible NormalizedAuditData.
        /// </summary>
        public NormalizedAuditData Normalize(RawTarkovTrackerData trackerData, CommunityDataPayload? communityData)
        {
            if (trackerData == null)
                throw new ArgumentNullException(nameof(trackerData));

            var auditData = NormalizeTarkovTrackerData(trackerData);

            if (communityData != null)
            {
                auditData.SourceSummary = $"{auditData.SourceSummary} + {communityData.Source} ({communityData.HideoutStations.Count} stations)";
                NormalizeHideoutData(auditData, communityData);
            }

            return auditData;
        }

        /// <summary>
        /// Normalizes community Hideout stations, levels, and item requirements into the audit data container.
        /// </summary>
        public void NormalizeHideoutData(NormalizedAuditData auditData, CommunityDataPayload communityData)
        {
            if (auditData == null || communityData == null || communityData.HideoutStations == null)
                return;

            var seenStationIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var station in communityData.HideoutStations)
            {
                var canonicalStationId = ResolveStationBsgId(station.Id, station.Name, station.NormalizedName);
                if (string.IsNullOrEmpty(canonicalStationId) || !seenStationIds.Add(canonicalStationId))
                    continue;

                var normalizedStation = new NormalizedHideoutStation
                {
                    Id = canonicalStationId,
                    Name = station.Name,
                    NameKO = station.NameKo,
                    NameJA = station.NameJa,
                    NormalizedName = station.NormalizedName ?? station.Name.ToLowerInvariant().Replace(" ", "-"),
                    ImageLink = station.ImageLink,
                    MaxLevel = station.Levels.Count > 0 ? station.Levels.Max(l => l.Level) : 0
                };
                auditData.HideoutStations.Add(normalizedStation);

                foreach (var lvl in station.Levels.OrderBy(l => l.Level))
                {
                    var levelId = $"{canonicalStationId}_{lvl.Level}";
                    auditData.HideoutLevels.Add(new NormalizedHideoutLevel
                    {
                        Id = levelId,
                        StationId = canonicalStationId,
                        StationName = station.Name,
                        Level = lvl.Level,
                        ConstructionTime = lvl.ConstructionTime
                    });

                    var itemReqSort = 0;
                    foreach (var ireq in lvl.ItemRequirements)
                    {
                        var bsgItemId = ResolveItemBsgId(ireq.ItemId, ireq.ItemName, communityData.Items);
                        if (string.IsNullOrEmpty(bsgItemId))
                            bsgItemId = ireq.ItemId;

                        var reqId = $"{canonicalStationId}_{lvl.Level}_{bsgItemId}";

                        auditData.HideoutItemRequirements.Add(new NormalizedHideoutItemRequirement
                        {
                            Id = reqId,
                            StationId = canonicalStationId,
                            StationName = station.Name,
                            Level = lvl.Level,
                            ItemId = bsgItemId,
                            ItemName = ireq.ItemName,
                            ItemNameKO = ireq.ItemNameKo,
                            ItemNameJA = ireq.ItemNameJa,
                            IconLink = ireq.IconLink,
                            Count = ireq.Count > 0 ? ireq.Count : 1,
                            FoundInRaid = ireq.FoundInRaid,
                            SortOrder = itemReqSort++
                        });
                    }
                }
            }
        }

        /// <summary>
        /// Normalizes raw data extracted from TarkovTracker API (tasks-core & tasks-objectives)
        /// or fallback to json.tarkov.dev into NormalizedAuditData ready for DB diffing and atomic application.
        /// Maps all 494 tasks, 303 trader requirements, 168 pool requirements, and linear task prerequisites.
        /// </summary>
        public NormalizedAuditData NormalizeTarkovTrackerData(RawTarkovTrackerData rawData)
        {
            if (rawData == null)
                throw new ArgumentNullException(nameof(rawData));

            var auditData = new NormalizedAuditData
            {
                NormalizedAt = DateTime.UtcNow,
                SourceSummary = $"{rawData.Source} ({rawData.Tasks.Count} tasks)"
            };

            // 1. Build lookup tables for fast resolution of prerequisites and names
            var tasksByBsgId = new Dictionary<string, (TarkovTrackerTask Task, string QuestId, string CleanTitle)>(StringComparer.OrdinalIgnoreCase);
            var tasksByCleanTitle = new Dictionary<string, (TarkovTrackerTask Task, string QuestId)>(StringComparer.OrdinalIgnoreCase);
            var seenQuestIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var taskToQuestId = new Dictionary<TarkovTrackerTask, string>();

            foreach (var task in rawData.Tasks)
            {
                var cleanTitle = CleanQuestTitle(task.Name);
                string questId;
                if (!string.IsNullOrWhiteSpace(task.Id))
                {
                    questId = task.Id.Trim();
                }
                else if (!string.IsNullOrWhiteSpace(task.WikiLink))
                {
                    questId = Convert.ToBase64String(Encoding.UTF8.GetBytes(task.WikiLink.Trim()));
                }
                else
                {
                    questId = ComputeQuestId(cleanTitle);
                }

                if (seenQuestIds.Contains(questId))
                {
                    questId = $"{questId}_{task.Id}";
                }
                seenQuestIds.Add(questId);
                taskToQuestId[task] = questId;

                if (!string.IsNullOrEmpty(task.Id))
                {
                    tasksByBsgId[task.Id] = (task, questId, cleanTitle);
                }
                if (!string.IsNullOrEmpty(cleanTitle) && !tasksByCleanTitle.ContainsKey(cleanTitle))
                {
                    tasksByCleanTitle[cleanTitle] = (task, questId);
                }
            }

            // 2. Normalize Quests
            foreach (var task in rawData.Tasks)
            {
                var cleanTitle = CleanQuestTitle(task.Name);
                var questId = taskToQuestId[task];

                // Resolve trader name
                var trader = NormalizeTrader(task.Trader?.Name);
                if (string.IsNullOrEmpty(trader) && task.Trader != null && !string.IsNullOrEmpty(task.Trader.Id))
                {
                    if (TarkovTrackerExtractorService.TraderIdToName.TryGetValue(task.Trader.Id, out var mappedTrader))
                    {
                        trader = mappedTrader;
                    }
                }
                if (IsStoryChapterTitle(cleanTitle))
                {
                    trader = "Story";
                }
                if (string.IsNullOrEmpty(trader))
                {
                    trader = "Prapor";
                }

                var normName = !string.IsNullOrEmpty(task.NormalizedName)
                    ? task.NormalizedName
                    : GenerateNormalizedName(cleanTitle);

                // Objectives descriptions
                var objectivesList = (task.Objectives != null && task.Objectives.Count > 0)
                    ? task.Objectives
                    : (rawData.ObjectivesByTaskId.TryGetValue(task.Id, out var loadedObjs) ? loadedObjs : null);

                List<string>? maps = null;
                if (task.Maps != null && task.Maps.Count > 0)
                {
                    maps = task.Maps
                        .Select(m => m.Name ?? m.Id)
                        .Where(n => !string.IsNullOrEmpty(n))
                        .Select(n => n.ToLowerInvariant())
                        .ToList();
                }
                else if (task.Map != null)
                {
                    var mapName = task.Map.Name ?? task.Map.Id;
                    if (!string.IsNullOrEmpty(mapName))
                    {
                        maps = new List<string> { mapName.ToLowerInvariant() };
                    }
                }
                else if (objectivesList != null && objectivesList.Count > 0)
                {
                    var objMaps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var obj in objectivesList)
                    {
                        if (obj.Maps != null)
                        {
                            foreach (var m in obj.Maps)
                            {
                                var mName = m.Name ?? (rawData.Maps.FirstOrDefault(rm => rm.Id == m.Id)?.Name);
                                if (!string.IsNullOrEmpty(mName)) objMaps.Add(mName.ToLowerInvariant());
                            }
                        }
                        if (obj.Zones != null)
                        {
                            foreach (var z in obj.Zones)
                            {
                                var mName = z.Map?.Name ?? (rawData.Maps.FirstOrDefault(rm => rm.Id == z.Map?.Id)?.Name);
                                if (!string.IsNullOrEmpty(mName)) objMaps.Add(mName.ToLowerInvariant());
                            }
                        }
                    }
                    if (objMaps.Count > 0)
                    {
                        maps = objMaps.ToList();
                    }
                }

                var minLevel = task.MinPlayerLevel.HasValue && task.MinPlayerLevel.Value > 0
                    ? task.MinPlayerLevel.Value
                    : (int?)null;

                // Special handling: To the Light - Getting Acquainted requires min level 33
                if (string.Equals(questId, "625d700cc48e6c62a440fab5", StringComparison.OrdinalIgnoreCase) && (!minLevel.HasValue || minLevel.Value < 33))
                {
                    minLevel = 33;
                }

                var faction = NormalizeFaction(task.FactionName);

                var normQuest = new NormalizedQuest
                {
                    Id = questId,
                    BsgId = !string.IsNullOrEmpty(task.Id) ? task.Id : null,
                    Name = cleanTitle,
                    NameEN = cleanTitle,
                    WikiPageLink = task.WikiLink ?? ComputeCanonicalWikiUrl(cleanTitle),
                    Trader = trader,
                    MinLevel = minLevel,
                    Faction = faction,
                    KappaRequired = task.KappaRequired,
                    NormalizedName = normName,
                    Maps = maps
                };

                if (objectivesList != null && objectivesList.Count > 0)
                {
                    normQuest.Objectives = objectivesList
                        .Select(o => o.Description)
                        .Where(d => !string.IsNullOrWhiteSpace(d))
                        .Select(d => d!)
                        .ToList();
                }

                auditData.Quests.Add(normQuest);

                // 3. Normalize Trader Loyalty Level Requirements (303 in EFT)
                var traderReqs = (task.NormalizedTraderRequirements != null && task.NormalizedTraderRequirements.Count > 0)
                    ? task.NormalizedTraderRequirements
                    : task.TraderLevelRequirements;

                if (traderReqs != null)
                {
                    int traderSort = 0;
                    var seenTraderReqs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var tr in traderReqs)
                    {
                        var tName = NormalizeTrader(tr.Trader?.Name);
                        if (string.IsNullOrEmpty(tName) && tr.Trader != null && !string.IsNullOrEmpty(tr.Trader.Id))
                        {
                            if (TarkovTrackerExtractorService.TraderIdToName.TryGetValue(tr.Trader.Id, out var mappedTrader))
                            {
                                tName = mappedTrader;
                            }
                        }
                        if (string.IsNullOrEmpty(tName))
                            tName = trader;

                        var reqLevel = tr.Level ?? (int)Math.Round(tr.Value);
                        var reqType = string.Equals(tr.RequirementType, "reputation", StringComparison.OrdinalIgnoreCase) ? "Reputation" : "Level";
                        var cmpMethod = !string.IsNullOrEmpty(tr.CompareMethod) ? tr.CompareMethod : ">=";

                        var dedupe = $"{tName}_{reqLevel}_{cmpMethod}_{reqType}";
                        if (!seenTraderReqs.Add(dedupe))
                            continue;

                        var trId = ComputeTraderRequirementId(questId, tName, reqLevel, reqType, traderSort);
                        var normTr = new NormalizedQuestTraderRequirement
                        {
                            Id = trId,
                            QuestId = questId,
                            QuestName = cleanTitle,
                            TraderId = tr.Trader?.Id,
                            TraderName = tName,
                            RequirementType = reqType,
                            CompareMethod = cmpMethod,
                            RequiredLevel = reqLevel,
                            SortOrder = traderSort++
                        };
                        normQuest.TraderRequirements.Add(normTr);
                        auditData.QuestTraderRequirements.Add(normTr);
                    }
                }

                // 4. Normalize Pool Milestone Requirements (168 in EFT)
                if (task.OtherRequirements != null)
                {
                    int poolSort = 0;
                    var seenPoolReqs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var otherReq in task.OtherRequirements)
                    {
                        if (!string.Equals(otherReq.Type, "globalVariable", StringComparison.OrdinalIgnoreCase))
                            continue;

                        string poolTrader = string.Empty;
                        int? poolLL = null;

                        if (otherReq.TaskPool != null)
                        {
                            if (!string.IsNullOrEmpty(otherReq.TaskPool.TraderId) &&
                                TarkovTrackerExtractorService.TraderIdToName.TryGetValue(otherReq.TaskPool.TraderId, out var mappedPoolTrader))
                            {
                                poolTrader = mappedPoolTrader;
                            }
                            poolLL = otherReq.TaskPool.LoyaltyLevel;
                        }

                        if (string.IsNullOrEmpty(poolTrader) && !string.IsNullOrEmpty(otherReq.VariableId) &&
                            TarkovTrackerExtractorService.GlobalVariableRegistry.TryGetValue(otherReq.VariableId, out var regInfo))
                        {
                            poolTrader = regInfo.TraderName;
                            if (!poolLL.HasValue)
                                poolLL = regInfo.LoyaltyLevel;
                        }

                        if (string.IsNullOrEmpty(poolTrader))
                            poolTrader = trader;

                        poolTrader = NormalizeTrader(poolTrader);
                        int requiredCount = otherReq.Value ?? 0;

                        var dedupe = $"{poolTrader}_{poolLL}_{requiredCount}";
                        if (!seenPoolReqs.Add(dedupe))
                            continue;

                        var prId = ComputePoolRequirementId(questId, poolTrader, poolLL, requiredCount, poolSort++);
                        var description = $"Complete at least {requiredCount} {poolTrader}{(poolLL.HasValue ? $" LL{poolLL.Value}" : "")} quests";

                        var normPr = new NormalizedQuestPoolRequirement
                        {
                            Id = prId,
                            QuestId = questId,
                            QuestName = cleanTitle,
                            PoolTrader = poolTrader,
                            PoolTraderLL = poolLL,
                            RequiredCompletedCount = requiredCount,
                            Description = description
                        };
                        normQuest.PoolRequirements.Add(normPr);
                        auditData.QuestPoolRequirements.Add(normPr);
                    }
                }

                // 5. Normalize Linear Task Dependencies
                if (task.TaskRequirements != null)
                {
                    var seenTaskReqs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var tr in task.TaskRequirements)
                    {
                        string requiredQuestId = string.Empty;
                        string requiredQuestName = string.Empty;

                        if (tr.Task != null)
                        {
                            if (!string.IsNullOrEmpty(tr.Task.Id))
                            {
                                requiredQuestId = tr.Task.Id.Trim();
                            }
                            else if (!string.IsNullOrEmpty(tr.Task.Name) && tasksByCleanTitle.TryGetValue(CleanQuestTitle(tr.Task.Name), out var tInfo))
                            {
                                requiredQuestId = tInfo.QuestId;
                            }
                            else if (!string.IsNullOrEmpty(tr.Task.WikiLink))
                            {
                                requiredQuestId = Convert.ToBase64String(Encoding.UTF8.GetBytes(tr.Task.WikiLink.Trim()));
                            }
                            else if (!string.IsNullOrEmpty(tr.Task.Name))
                            {
                                var cleanReqTitle = CleanQuestTitle(tr.Task.Name);
                                requiredQuestId = ComputeQuestId(cleanReqTitle);
                                requiredQuestName = cleanReqTitle;
                            }

                            if (string.IsNullOrEmpty(requiredQuestName) && !string.IsNullOrEmpty(tr.Task.Name))
                            {
                                requiredQuestName = CleanQuestTitle(tr.Task.Name);
                            }

                            if ((string.IsNullOrEmpty(requiredQuestId) || string.IsNullOrEmpty(requiredQuestName)) &&
                                !string.IsNullOrEmpty(tr.Task.Id) &&
                                tasksByBsgId.TryGetValue(tr.Task.Id, out var foundPrereq))
                            {
                                if (string.IsNullOrEmpty(requiredQuestId)) requiredQuestId = foundPrereq.QuestId;
                                if (string.IsNullOrEmpty(requiredQuestName)) requiredQuestName = foundPrereq.CleanTitle;
                            }

                            // Map obsolete Knock-Knock prerequisite directly to the preceding quest in modern EFT: To the Light - The Other Side
                            if (string.Equals(requiredQuestId, "625d7005a4eb80027c4f2e09", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(requiredQuestName, "Knock-Knock", StringComparison.OrdinalIgnoreCase))
                            {
                                requiredQuestId = "placeholder_to_the_light_the_other_side";
                                requiredQuestName = "To the Light - The Other Side";
                            }
                        }

                        if (string.IsNullOrEmpty(requiredQuestId) || string.IsNullOrEmpty(requiredQuestName))
                            continue;

                        var reqType = tr.Status != null && tr.Status.Count > 0
                            ? NormalizeRequirementType(tr.Status[0])
                            : "Complete";

                        var dedupe = $"{requiredQuestId}_{reqType}";
                        if (!seenTaskReqs.Add(dedupe))
                            continue;

                        var reqId = ComputeRequirementId(questId, requiredQuestId, 0);
                        auditData.QuestRequirements.Add(new NormalizedQuestRequirement
                        {
                            Id = reqId,
                            QuestId = questId,
                            QuestName = cleanTitle,
                            RequiredQuestId = requiredQuestId,
                            RequiredQuestName = requiredQuestName,
                            RequirementType = reqType,
                            GroupId = 0
                        });
                    }
                }
            }

            return auditData;
        }

        #endregion

        #region Helper Math & Key Functions

        /// <summary>
        /// Cleans game zone suffixes such as [PVP ZONE] or [PVE ZONE] from task titles.
        /// </summary>
        public static string CleanQuestTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return string.Empty;

            return Regex.Replace(title, @"\s*\[(?:PVP|PVE)\s*ZONE\]", "", RegexOptions.IgnoreCase).Trim();
        }

        public static string ComputeCanonicalWikiUrl(string title)
        {
            var encodedName = Uri.EscapeDataString(title.Replace(" ", "_")).Replace("%28", "(").Replace("%29", ")");
            return $"https://escapefromtarkov.fandom.com/wiki/{encodedName}";
        }

        public static string ComputeQuestId(string title)
        {
            var wikiUrl = ComputeCanonicalWikiUrl(title);
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(wikiUrl));
        }

        public static string ComputeItemWikiId(string? itemName)
        {
            if (string.IsNullOrWhiteSpace(itemName))
                return string.Empty;

            var encoded = Uri.EscapeDataString(itemName.Trim().Replace(" ", "_"));
            var url = $"https://escapefromtarkov.fandom.com/wiki/{encoded}";
            var bytes = Encoding.UTF8.GetBytes(url);
            var base64 = Convert.ToBase64String(bytes);
            return base64.Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        public static string ComputeRequirementId(string questId, string requiredQuestId, int groupId)
        {
            var data = $"REQ|{questId}|{requiredQuestId}|{groupId}";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(data));
            return Convert.ToBase64String(hash)[..22].Replace("/", "_").Replace("+", "-");
        }

        public static string ComputeQuestItemId(string questId, string itemName, string requirementType, bool requiresFir, int sortOrder)
        {
            var data = $"ITEM|{questId}|{itemName}|{requirementType}|{requiresFir}|{sortOrder}";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(data));
            return Convert.ToBase64String(hash)[..22].Replace("/", "_").Replace("+", "-");
        }

        public static string ComputeTraderRequirementId(string questId, string traderName, int level)
        {
            var data = $"TRADER_REQ|{questId}|{traderName}|{level}";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(data));
            return Convert.ToBase64String(hash)[..22].Replace("/", "_").Replace("+", "-");
        }

        public static string ComputeTraderRequirementId(string questId, string traderName, int level, string requirementType, int sortOrder = 0)
        {
            var data = (string.Equals(requirementType, "Level", StringComparison.OrdinalIgnoreCase) && sortOrder == 0)
                ? $"TRADER_REQ|{questId}|{traderName}|{level}"
                : $"TRADER_REQ|{questId}|{traderName}|{level}|{requirementType}|{sortOrder}";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(data));
            return Convert.ToBase64String(hash)[..22].Replace("/", "_").Replace("+", "-");
        }

        public static string ComputePoolRequirementId(string questId, string poolTrader, int? poolLL, int count)
        {
            var data = $"POOL_REQ|{questId}|{poolTrader}|{poolLL}|{count}";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(data));
            return Convert.ToBase64String(hash)[..22].Replace("/", "_").Replace("+", "-");
        }

        public static string ComputePoolRequirementId(string questId, string poolTrader, int? poolLL, int count, int sortOrder = 0)
        {
            var data = (sortOrder == 0)
                ? $"POOL_REQ|{questId}|{poolTrader}|{poolLL}|{count}"
                : $"POOL_REQ|{questId}|{poolTrader}|{poolLL}|{count}|{sortOrder}";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(data));
            return Convert.ToBase64String(hash)[..22].Replace("/", "_").Replace("+", "-");
        }

        public static string GenerateNormalizedName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            return name.ToLowerInvariant()
                .Replace(" ", "-")
                .Replace("'", "")
                .Replace("'", "")
                .Replace(".", "")
                .Replace(",", "")
                .Replace("?", "")
                .Replace("!", "")
                .Replace(":", "")
                .Replace("\"", "");
        }

        public static readonly HashSet<string> KnownStoryChapters = new(StringComparer.OrdinalIgnoreCase)
        {
            "Tour",
            "Batya",
            "Accidental Witness",
            "Blue Fire",
            "Boreas",
            "Falling Skies",
            "The Labyrinth",
            "The Labyrinth (story chapter)",
            "The Ticket",
            "The Unheard",
            "They Are Already Here"
        };

        public static bool IsStoryChapterTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title)) return false;
            var trimmed = title.Trim();
            if (KnownStoryChapters.Contains(trimmed)) return true;

            var stripped = Regex.Replace(trimmed, @"\s*\([^)]*\)", "").Trim();
            return KnownStoryChapters.Contains(stripped);
        }

        private static readonly HashSet<string> RecognizedNonStoryTraders = new(StringComparer.OrdinalIgnoreCase)
        {
            "Prapor",
            "Therapist",
            "Skier",
            "Peacekeeper",
            "Mechanic",
            "Ragman",
            "Jaeger",
            "Fence",
            "Lightkeeper",
            "Ref",
            "BTR Driver"
        };

        public static bool IsRecognizedNonStoryTrader(string? trader)
        {
            if (string.IsNullOrWhiteSpace(trader)) return false;
            var norm = NormalizeTrader(trader);
            return RecognizedNonStoryTraders.Contains(norm);
        }

        public static string NormalizeTrader(string? trader)
        {
            if (string.IsNullOrWhiteSpace(trader))
                return "Prapor";

            var trimmed = trader.Trim();
            if (string.Equals(trimmed, "story", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "story chapters", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "story chapter", StringComparison.OrdinalIgnoreCase))
            {
                return "Story";
            }

            if (TraderNameAliases.TryGetValue(trimmed, out var normalized))
                return normalized;

            return trimmed;
        }

        public static string? NormalizeFaction(string? faction)
        {
            if (string.IsNullOrWhiteSpace(faction))
                return null;

            var f = faction.Trim().ToLowerInvariant();
            if (f.Contains("bear")) return "Bear";
            if (f.Contains("usec")) return "Usec";
            return null;
        }

        public static string NormalizeRequirementType(string? reqType)
        {
            if (string.IsNullOrWhiteSpace(reqType)) return "Complete";
            var rt = reqType.Trim().ToLowerInvariant();
            if (rt == "fail" || rt == "failed") return "Fail";
            if (rt == "accept" || rt == "active") return "Accept";
            return "Complete";
        }

        public static List<string>? ParseLocationToMaps(string? location)
        {
            if (string.IsNullOrWhiteSpace(location))
                return null;

            if (location.Equals("any", StringComparison.OrdinalIgnoreCase))
                return null;

            if (location.Contains(','))
            {
                return location.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(m => m.Trim().ToLowerInvariant())
                    .ToList();
            }

            return new List<string> { location.Trim().ToLowerInvariant() };
        }

        public static string ResolveStationBsgId(string rawId, string name, string? normalizedName)
        {
            // If rawId is already a 24-character hexadecimal MongoDB ObjectId
            if (IsHexMongoId(rawId))
                return rawId;

            if (!string.IsNullOrEmpty(name) && StationNameToBsgId.TryGetValue(name, out var bsgIdByName))
                return bsgIdByName;

            if (!string.IsNullOrEmpty(normalizedName) && StationNameToBsgId.TryGetValue(normalizedName, out var bsgIdByNorm))
                return bsgIdByNorm;

            return rawId;
        }

        public static string ResolveItemBsgId(string rawItemId, string itemName, Dictionary<string, CommunityItemInfo> communityItems)
        {
            if (IsHexMongoId(rawItemId))
                return rawItemId;

            if (!string.IsNullOrEmpty(itemName))
            {
                foreach (var kvp in communityItems)
                {
                    if (string.Equals(kvp.Value.Name, itemName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(kvp.Value.ShortName, itemName, StringComparison.OrdinalIgnoreCase))
                    {
                        return kvp.Key;
                    }
                }
            }

            return rawItemId;
        }

        public static bool IsHexMongoId(string? value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 24)
                return false;

            return Regex.IsMatch(value, @"^[a-fA-F0-9]{24}$");
        }

        #endregion
    }
}
