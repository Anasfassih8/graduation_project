namespace Traffic.API.Models;

public class Violation
{
    public int Id { get; set; }
    public string PlateNumber { get; set; } = string.Empty;
    public double SpeedKmh { get; set; }
    public int TrackId { get; set; }
    public string RoadId { get; set; } = string.Empty;
    public DateTime DetectedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}