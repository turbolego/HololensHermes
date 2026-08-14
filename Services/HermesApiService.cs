using System;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.Data.Json;
using HololensHermes.Navigation;

namespace HololensHermes.Services
{
    /// <summary>
    /// Lightweight wrapper over the Hermes assistant API.
    ///
    /// Contract:
    ///   GET /api/goal?text=&botId=&latitude=&longitude=&accuracyMeters=&venueId=
    ///       -> 200 { "target": { "x", "y", "label" }, "steps": [...] }
    ///   GET /api/floorplan?uri=
    ///       -> 200 { "widthMeters", "heightMeters", "northRotationDeg" }
    ///   POST /api/feedback
    ///       -> 200 { "ok": true }
    ///
    /// Location fields are optional for backward compatibility. When supplied,
    /// they describe a Wi-Fi/network-derived estimate and its uncertainty; Hermes
    /// must use them to select a candidate venue, never as indoor anchor data.
    /// </summary>
    public sealed class HermesApiService : IDisposable
    {
        private readonly HttpClient _http;
        private readonly string _baseAddress;

        public HermesApiService(string baseAddress)
        {
            if (string.IsNullOrWhiteSpace(baseAddress))
                throw new ArgumentException("A Hermes base address is required.", "baseAddress");

            _baseAddress = baseAddress.TrimEnd('/');
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        }

        public Task<HermesGoalResponse> ResolveGoalAsync(string goalText, string botId)
        {
            return ResolveGoalAsync(goalText, botId, null, null);
        }

        /// <summary>
        /// Resolves a target with a coarse location estimate. The caller should
        /// supply venueId only after VenueResolver has selected it conservatively.
        /// </summary>
        public async Task<HermesGoalResponse> ResolveGoalAsync(
            string goalText,
            string botId,
            LocationEstimate location,
            string venueId)
        {
            if (string.IsNullOrWhiteSpace(goalText))
                return new HermesGoalResponse { Error = "A navigation goal is required." };
            if (string.IsNullOrWhiteSpace(botId))
                return new HermesGoalResponse { Error = "A Hermes bot id is required." };

            var uri = BuildGoalUri(goalText, botId, location, venueId);
            try
            {
                var raw = await _http.GetStringAsync(uri);
                var root = JsonObject.Parse(raw);
                if (root.ContainsKey("error"))
                {
                    return new HermesGoalResponse { Error = root["error"].GetString() };
                }

                if (!root.ContainsKey("target") || root["target"].ValueType != JsonValueType.Object)
                {
                    return new HermesGoalResponse { Error = "Hermes returned no target." };
                }

                var target = root["target"].GetObject();
                if (!target.ContainsKey("x") || !target.ContainsKey("y") || !target.ContainsKey("label"))
                {
                    return new HermesGoalResponse { Error = "Hermes returned an incomplete target." };
                }

                return new HermesGoalResponse
                {
                    Target = new HermesTarget
                    {
                        X = (float)target["x"].GetNumber(),
                        Y = (float)target["y"].GetNumber(),
                        Label = target["label"].GetString()
                    }
                };
            }
            catch (HttpRequestException)
            {
                return new HermesGoalResponse { Error = "Unable to contact Hermes." };
            }
            catch (TaskCanceledException)
            {
                return new HermesGoalResponse { Error = "Hermes request timed out." };
            }
            catch (Exception)
            {
                return new HermesGoalResponse { Error = "Hermes returned an invalid response." };
            }
        }

        public async Task<HermesFloorPlanResponse> FetchFloorPlanMetaAsync(string imageUri)
        {
            if (string.IsNullOrWhiteSpace(imageUri))
                return new HermesFloorPlanResponse { Error = "A floor-plan URI is required." };

            var uri = string.Format(
                "{0}/api/floorplan?uri={1}",
                _baseAddress,
                Uri.EscapeDataString(imageUri));
            try
            {
                var raw = await _http.GetStringAsync(uri);
                var root = JsonObject.Parse(raw);
                if (root.ContainsKey("error"))
                    return new HermesFloorPlanResponse { Error = root["error"].GetString() };

                if (!root.ContainsKey("widthMeters") || !root.ContainsKey("heightMeters") || !root.ContainsKey("northRotationDeg"))
                    return new HermesFloorPlanResponse { Error = "Hermes returned incomplete floor-plan metadata." };

                return new HermesFloorPlanResponse
                {
                    WidthMeters = (float)root["widthMeters"].GetNumber(),
                    HeightMeters = (float)root["heightMeters"].GetNumber(),
                    NorthRotationDegrees = (float)root["northRotationDeg"].GetNumber()
                };
            }
            catch (HttpRequestException)
            {
                return new HermesFloorPlanResponse { Error = "Unable to contact Hermes." };
            }
            catch (TaskCanceledException)
            {
                return new HermesFloorPlanResponse { Error = "Hermes request timed out." };
            }
            catch (Exception)
            {
                return new HermesFloorPlanResponse { Error = "Hermes returned invalid floor-plan metadata." };
            }
        }

        public async Task<HermesFeedbackResponse> SendFeedbackAsync(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
                return new HermesFeedbackResponse { Error = "Feedback payload is required." };

            try
            {
                var response = await _http.PostAsync(
                    string.Format("{0}/api/feedback", _baseAddress),
                    new StringContent(payload));
                var raw = await response.Content.ReadAsStringAsync();
                var json = JsonObject.Parse(raw);
                if (!response.IsSuccessStatusCode || !json.ContainsKey("ok"))
                    return new HermesFeedbackResponse { Error = "Hermes did not accept feedback." };

                return new HermesFeedbackResponse { Ok = json["ok"].GetBoolean() };
            }
            catch (HttpRequestException)
            {
                return new HermesFeedbackResponse { Error = "Unable to contact Hermes." };
            }
            catch (TaskCanceledException)
            {
                return new HermesFeedbackResponse { Error = "Hermes request timed out." };
            }
            catch (Exception)
            {
                return new HermesFeedbackResponse { Error = "Hermes returned an invalid feedback response." };
            }
        }

        public void Dispose()
        {
            _http.Dispose();
        }

        private string BuildGoalUri(string goalText, string botId, LocationEstimate location, string venueId)
        {
            var uri = string.Format(
                "{0}/api/goal?text={1}&botId={2}",
                _baseAddress,
                Uri.EscapeDataString(goalText),
                Uri.EscapeDataString(botId));

            if (location != null && location.IsAvailable)
            {
                uri += string.Format(
                    "&latitude={0:R}&longitude={1:R}&accuracyMeters={2:R}",
                    location.Coordinate.Latitude,
                    location.Coordinate.Longitude,
                    location.AccuracyMeters);
            }

            if (!string.IsNullOrWhiteSpace(venueId))
            {
                uri += "&venueId=" + Uri.EscapeDataString(venueId);
            }

            return uri;
        }
    }

    public sealed class HermesGoalResponse
    {
        public HermesTarget Target { get; set; }
        public string Error { get; set; }
        public bool HasError { get { return !string.IsNullOrEmpty(Error); } }
    }

    public sealed class HermesTarget
    {
        public float X { get; set; }
        public float Y { get; set; }
        public string Label { get; set; }
    }

    public sealed class HermesFloorPlanResponse
    {
        public float WidthMeters { get; set; }
        public float HeightMeters { get; set; }
        public float NorthRotationDegrees { get; set; }
        public string Error { get; set; }
    }

    public sealed class HermesFeedbackResponse
    {
        public bool Ok { get; set; }
        public string Error { get; set; }
    }
}
