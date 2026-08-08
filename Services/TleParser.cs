using System;
using System.Collections.Generic;
using HololensSatelliteViewer.Models;

namespace HololensSatelliteViewer.Services
{
    /// <summary>
    /// Pure TLE text parsing — extracted from OrbitService so it can be unit
    /// tested directly (no network, no UWP dependencies).
    ///
    /// Handles standard 3-line format (name / line1 / line2) and 2-line
    /// format (line1 / line2, name = "UNKNOWN").
    /// </summary>
    public static class TleParser
    {
        /// <summary>
        /// Parses a standard 3-line TLE text block (name / line1 / line2).
        /// Also handles 2-line format (line1 / line2, no name).
        /// Returns only records that parsed cleanly; skips junk lines.
        /// </summary>
        public static List<TleRecord> Parse(string text)
        {
            var records = new List<TleRecord>();
            var lines   = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            int i = 0;
            while (i < lines.Length)
            {
                var l = lines[i].Trim();

                if (string.IsNullOrWhiteSpace(l) || l.StartsWith("#"))
                {
                    i++; continue;
                }

                // 3-line format: name line → line1 → line2
                if (!l.StartsWith("1 ") && !l.StartsWith("2 ")
                    && i + 2 < lines.Length)
                {
                    var l1 = lines[i + 1].Trim();
                    var l2 = lines[i + 2].Trim();
                    if (l1.StartsWith("1 ") && l1.Length >= 69
                     && l2.StartsWith("2 ") && l2.Length >= 69)
                    {
                        var rec = TryParse(l, l1, l2);
                        if (rec != null) records.Add(rec);
                        i += 3; continue;
                    }
                }

                // 2-line format: line1 → line2
                if (l.StartsWith("1 ") && l.Length >= 69 && i + 1 < lines.Length)
                {
                    var l2 = lines[i + 1].Trim();
                    if (l2.StartsWith("2 ") && l2.Length >= 69)
                    {
                        var rec = TryParse("UNKNOWN", l, l2);
                        if (rec != null) records.Add(rec);
                        i += 2; continue;
                    }
                }

                i++;
            }

            return records;
        }

        private static TleRecord TryParse(string name, string line1, string line2)
        {
            try
            {
                int.TryParse(line1.Substring(2, 5).Trim(),
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out int norad);
                return new TleRecord
                {
                    Name    = name.Trim(),
                    NoradId = norad,
                    Line1   = line1,
                    Line2   = line2
                };
            }
            catch { return null; }
        }
    }
}
