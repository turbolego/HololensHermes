using System;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.Data.Json;

namespace HololensHermes.Services
{
    /// <summary>
    /// Lightweight wrapper over the Hermes assistant API.
    ///
    /// Contract (TBD with Hermes team):
    ///   GET  /api/goal?text=<goal>&botId=<botId>
    ///     → 200 { "target": { "x", "y", "label" }, "steps": [...] } 200
    ///     → 400 { "error": "..." } on bad request
    ///     → 500 on server error
    ///
    ///   GET  /api/floorplan?uri=<imageUri>
    ///     → 200 { "widthMeters", "heightMeters", "northRotationDeg" } 200
    ///     → 404 on unknown floor plan
    ///
    ///   POST /api/feedback
    ///     → 200 { "ok": true }
    ///
    /// Base address is configured in BasicHologramMain. Replace with real Hermes endpoint.
    /// </summary>
    public sealed class HermesApiService
    {
        private readonly HttpClient _http;
        private readonly string _baseAddress;

        public HermesApiService(string baseAddress)
        {
            _baseAddress = baseAddress.TrimEnd('/');
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        }

        /// <summary>
        /// Ask Hermes to locate a target (book, furniture, POI) given the user's current floor-plan space.
        /// </summary>
        public async Task<HermesGoalResponse> ResolveGoalAsync(string goalText, string botId)
        {
            var uri = $"{_baseAddress}/api/goal?text={Uri.EscapeDataString(goalText)}&botId={Uri.EscapeDataString(botId)}";
            try
            {
                var raw = await _http.GetStringAsync(uri);
                var root = JsonObject.Parse(raw);
                if (root.Keys.Contains("error"))
                {
                    return new HermesGoalResponse { Error = root["error"].GetString() };
                }
                var t = root["target"];
                return new HermesGoalResponse
                {
                    Target = new HermesTarget
                    {
                        X = (float)t["x"].GetNumber(),
                        Y = (float)t["y"].GetNumber(),
                        Label = t["label"].GetString()
                    }
                };
            }
            catch (HttpRequestException e)
            {
                return new HermesGoalResponse { Error = $"network: {e.Message}" };
            }
            catch (TaskCanceledException)
            {
                return new HermesGoalResponse { Error = "timeout" };
            }
        }

        public async Task<HermesFloorPlanResponse> FetchFloorPlanMetaAsync(string imageUri)
        {
            var uri = $"{_baseAddress}/api/floorplan?uri={Uri.EscapeDataString(imageUri)}";
            try
            {
                var raw = await _http.GetStringAsync(uri);
                var root = JsonObject.Parse(raw);
                return new HermesFloorPlanResponse
                {
                    WidthMeters = (float)root["widthMeters"].GetNumber(),
                    HeightMeters = (float)root["heightMeters"].GetNumber(),
                    NorthRotationDegrees = (float)root["northRotationDeg"].GetNumber()
                };
            }
            catch (Exception e)
            {
                return new HermesFloorPlanResponse { Error = e.Message };
            }
        }

        public async Task<HermesFeedbackResponse> SendFeedbackAsync(string payload)
        {
            try
            {
                var raw = await _http.PostAsync($"{_baseAddress}/api/feedback", new StringContent(payload));
                var j = JsonObject.Parse(await raw.Content.ReadAsStringAsync());
                return new HermesFeedbackResponse { Ok = j["ok"].GetBoolean() };
            }
            catch (Exception e)
            {
                return new HermesFeedbackResponse { Error = e.Message };
            }
        }
    }

    public sealed class HermesGoalResponse
    {
        public HermesTarget Target { get; set; }
        public string Error { get; set; }
        public bool HasError => !string.IsNullOrEmpty(Error);
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
