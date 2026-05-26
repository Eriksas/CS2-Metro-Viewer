using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace MetroDiagram.Core.Exporting
{
    public sealed class ExportSnapshotPaths
    {
        public string ExportRootDirectory { get; set; } = string.Empty;

        public string LatestJsonPath { get; set; } = string.Empty;

        public string LatestDiagnosticsPath { get; set; } = string.Empty;

        public string SnapshotDirectory { get; set; } = string.Empty;

        public string SnapshotJsonPath { get; set; } = string.Empty;

        public string SnapshotDiagnosticsPath { get; set; } = string.Empty;

        public string CitySlug { get; set; } = string.Empty;

        public string TimestampToken { get; set; } = string.Empty;
    }

    public static class ExportSnapshotPathBuilder
    {
        public const string FallbackCitySlug = "UnnamedCity";
        private const int MaxCitySlugLength = 48;

        public static ExportSnapshotPaths Build(
            string exportRootDirectory,
            string latestJsonFileName,
            string latestDiagnosticsFileName,
            string snapshotJsonPrefix,
            string snapshotDiagnosticsPrefix,
            string cityName,
            DateTime localTimestamp)
        {
            if (string.IsNullOrWhiteSpace(exportRootDirectory))
            {
                throw new ArgumentException("Export root directory is required.", nameof(exportRootDirectory));
            }

            if (string.IsNullOrWhiteSpace(latestJsonFileName))
            {
                throw new ArgumentException("Latest JSON file name is required.", nameof(latestJsonFileName));
            }

            if (string.IsNullOrWhiteSpace(latestDiagnosticsFileName))
            {
                throw new ArgumentException("Latest diagnostics file name is required.", nameof(latestDiagnosticsFileName));
            }

            if (string.IsNullOrWhiteSpace(snapshotJsonPrefix))
            {
                throw new ArgumentException("Snapshot JSON prefix is required.", nameof(snapshotJsonPrefix));
            }

            if (string.IsNullOrWhiteSpace(snapshotDiagnosticsPrefix))
            {
                throw new ArgumentException("Snapshot diagnostics prefix is required.", nameof(snapshotDiagnosticsPrefix));
            }

            string citySlug = SanitizeCitySlug(cityName);
            string timestampToken = localTimestamp.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string snapshotDirectory = Path.Combine(exportRootDirectory, "exports");

            return new ExportSnapshotPaths
            {
                ExportRootDirectory = exportRootDirectory,
                LatestJsonPath = Path.Combine(exportRootDirectory, latestJsonFileName),
                LatestDiagnosticsPath = Path.Combine(exportRootDirectory, latestDiagnosticsFileName),
                SnapshotDirectory = snapshotDirectory,
                SnapshotJsonPath = Path.Combine(snapshotDirectory, string.Format(CultureInfo.InvariantCulture, "{0}-{1}-{2}.json", snapshotJsonPrefix, citySlug, timestampToken)),
                SnapshotDiagnosticsPath = Path.Combine(snapshotDirectory, string.Format(CultureInfo.InvariantCulture, "{0}-{1}-{2}.txt", snapshotDiagnosticsPrefix, citySlug, timestampToken)),
                CitySlug = citySlug,
                TimestampToken = timestampToken
            };
        }

        public static string SanitizeCitySlug(string cityName)
        {
            if (string.IsNullOrWhiteSpace(cityName))
            {
                return FallbackCitySlug;
            }

            HashSet<char> invalidCharacters = new HashSet<char>(Path.GetInvalidFileNameChars());
            StringBuilder normalized = new StringBuilder(cityName.Length);
            string trimmedCityName = cityName.Trim();

            foreach (char character in trimmedCityName)
            {
                if (char.IsControl(character) || invalidCharacters.Contains(character))
                {
                    normalized.Append(' ');
                    continue;
                }

                normalized.Append(char.IsWhiteSpace(character) ? ' ' : character);
            }

            string collapsed = CollapseWhitespace(normalized.ToString(), '-').Trim(' ', '-', '.', '_');

            if (collapsed.Length > MaxCitySlugLength)
            {
                collapsed = collapsed.Substring(0, MaxCitySlugLength).TrimEnd(' ', '-', '.', '_');
            }

            return string.IsNullOrWhiteSpace(collapsed) ? FallbackCitySlug : collapsed;
        }

        private static string CollapseWhitespace(string value, char separator)
        {
            StringBuilder result = new StringBuilder(value.Length);
            bool previousWasSeparator = false;

            foreach (char character in value)
            {
                if (char.IsWhiteSpace(character))
                {
                    if (!previousWasSeparator)
                    {
                        result.Append(separator);
                        previousWasSeparator = true;
                    }

                    continue;
                }

                result.Append(character);
                previousWasSeparator = false;
            }

            return result.ToString();
        }
    }
}
