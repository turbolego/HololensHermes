using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using HololensSatelliteViewer.Models;

namespace HololensSatelliteViewer.Services
{
    public class TleService
    {
        private const string StationsUrl = "https://celestrak.org/NORAD/elements/stations.txt";
        private const string ActiveUrl = "https://celestrak.org/NORAD/elements/gp.php?GROUP=active&FORMAT=tle";

        private static readonly HttpClient HttpClient = new HttpClient();

        public async Task<List<TleRecord>> DownloadStationTlesAsync()
        {
            return await DownloadTlesAsync(StationsUrl);
        }

        public async Task<List<TleRecord>> DownloadActiveTlesAsync(int maxCount = 50)
        {
            var all = await DownloadTlesAsync(ActiveUrl);
            return all.Take(maxCount).ToList();
        }

        private async Task<List<TleRecord>> DownloadTlesAsync(string url)
        {
            var records = new List<TleRecord>();

            try
            {
                var raw = await HttpClient.GetStringAsync(url);
                var lines = raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                for (int i = 0; i + 2 < lines.Length; i += 3)
                {
                    var name = lines[i].Trim();
                    var line1 = lines[i + 1].Trim();
                    var line2 = lines[i + 2].Trim();

                    if (line1.StartsWith("1 ") && line2.StartsWith("2 "))
                    {
                        var noradId = ExtractNoradId(line1);

                        records.Add(new TleRecord
                        {
                            Name = name,
                            Line1 = line1,
                            Line2 = line2,
                            NoradId = noradId
                        });
                    }
                }
            }
            catch
            {
            }

            return records;
        }

        private int ExtractNoradId(string line1)
        {
            if (line1.Length < 10)
            {
                return 0;
            }

            var segment = line1.Substring(2, 5).Trim();
            int id;
            return int.TryParse(segment, out id) ? id : 0;
        }
    }
}
