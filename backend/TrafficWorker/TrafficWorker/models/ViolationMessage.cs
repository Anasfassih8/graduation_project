using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace TrafficWorker.Models;

public class ViolationMessage
{
    [JsonPropertyName("plate_number")]
    public string PlateNumber { get; set; } = string.Empty;

    [JsonPropertyName("speed_kmh")]
    public double SpeedKmh { get; set; }

    [JsonPropertyName("track_id")]
    public int TrackId { get; set; }

    [JsonPropertyName("road_id")]
    public string RoadId { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }
}
