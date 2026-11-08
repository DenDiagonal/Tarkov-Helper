using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using TarkovHelper.ViewModels;

namespace TarkovHelper.Tests.Common
{
    /// <summary>
    /// Shared test helpers and fixture factories for Requirements R1 & R2 test suites.
    /// Provides repository-relative path resolution, XAML parsing, and test model creation.
    /// </summary>
    public static class R1R2TestHelpers
    {
        public static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

        /// <summary>
        /// Retrieves the Name or x:Name attribute value of an XElement safely without throwing XmlException on namespace prefixes.
        /// </summary>
        public static string? GetXamlName(this XElement element)
        {
            if (element == null) return null;
            return (string?)element.Attribute("Name")
                ?? (string?)element.Attribute(XamlNamespace + "Name")
                ?? element.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, "Name", StringComparison.OrdinalIgnoreCase))?.Value;
        }

        /// <summary>
        /// Retrieves an attribute value by its local name, ignoring XML namespaces.
        /// </summary>
        public static string? GetAttr(this XElement element, string localName)
        {
            if (element == null) return null;
            return element.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase))?.Value;
        }

        private static readonly HashSet<string> CanonicalSpecialEquipment = new(StringComparer.OrdinalIgnoreCase)
        {
            "ms2000 marker", "ms2000_marker", "wi-fi camera", "wi-fi_camera", "wifi camera", "signal jammer", "signal_jammer",
            "5991b51486f77447b112d44f", "5b4391a586f7745321235ab2", "5ac78a9b86f7741cca0bbd8d"
        };

        /// <summary>
        /// Canonical list of the 12 Escape from Tarkov maps.
        /// </summary>
        public static readonly string[] Canonical12Maps = new[]
        {
            "ground-zero",
            "customs",
            "woods",
            "factory",
            "shoreline",
            "interchange",
            "reserve",
            "lighthouse",
            "streets",
            "the-lab",
            "the-labyrinth",
            "terminal"
        };

        /// <summary>
        /// Checks whether an item qualifies as placeable special equipment (MS2000, Wi-Fi Camera, Signal Jammer).
        /// </summary>
        public static bool IsSpecialEquipment(string? normName, string? itemId, string? category)
        {
            if (string.Equals(category, "Special equipment", StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrEmpty(normName) && CanonicalSpecialEquipment.Contains(normName))
                return true;

            if (!string.IsNullOrEmpty(itemId) && CanonicalSpecialEquipment.Contains(itemId))
                return true;

            return false;
        }

        /// <summary>
        /// Locates a file relative to the repository root directory.
        /// </summary>
        public static string ResolveRepoPath(params string[] relativeParts)
        {
            var current = AppDomain.CurrentDomain.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                var candidate = Path.Combine(current, Path.Combine(relativeParts));
                if (File.Exists(candidate) || Directory.Exists(candidate))
                    return Path.GetFullPath(candidate);

                var gitDir = Path.Combine(current, ".git");
                var slnFile = Path.Combine(current, "TarkovHelper.sln");
                if (Directory.Exists(gitDir) || File.Exists(slnFile))
                {
                    return Path.GetFullPath(Path.Combine(current, Path.Combine(relativeParts)));
                }

                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }

            return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), Path.Combine(relativeParts)));
        }

        /// <summary>
        /// Loads and parses a XAML file as an XDocument with XML namespaces stripped for simple querying.
        /// </summary>
        public static XDocument LoadXamlDocument(params string[] relativeParts)
        {
            var fullPath = ResolveRepoPath(relativeParts);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"XAML file not found at '{fullPath}'.", fullPath);
            }

            var raw = File.ReadAllText(fullPath);
            var doc = XDocument.Parse(raw);

            // Strip XML namespaces to allow easy unqualified element name matching
            foreach (var node in doc.Descendants())
            {
                node.Name = node.Name.LocalName;
            }

            return doc;
        }

        /// <summary>
        /// Factory helper to build a NeededItemCardViewModel with customizable test defaults.
        /// </summary>
        public static NeededItemCardViewModel CreateCard(
            string needKey = "test_key",
            string sourceType = "Quest",
            int amount = 1,
            int currentCount = 0,
            bool isKappaRequired = false,
            string? questNormalizedName = null,
            bool foundInRaid = false,
            bool isSpecialEquipment = false,
            string itemDisplayName = "Test Item",
            string sourceName = "Test Source",
            string? traderName = null,
            int priorityScore = 100)
        {
            return new NeededItemCardViewModel
            {
                NeedKey = needKey,
                SourceType = sourceType,
                Amount = amount,
                CurrentCount = currentCount,
                IsKappaRequired = isKappaRequired,
                QuestNormalizedName = questNormalizedName,
                FoundInRaid = foundInRaid,
                IsSpecialEquipment = isSpecialEquipment,
                ItemDisplayName = itemDisplayName,
                SourceName = sourceName,
                TraderName = traderName,
                PriorityScore = priorityScore
            };
        }
    }
}
