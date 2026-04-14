using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Dapper;

namespace Traffic.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TrafficController : ControllerBase
    {
        private readonly string _connectionString =
            "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=postgres";

        // ================= SEGMENTS =================
        [HttpGet("segments")]
        public async Task<IActionResult> GetSegments()
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            string sql = @"
                SELECT 
                    segment_id AS SegmentId,
                    avg_speed AS AvgSpeed,
                    density AS Density,
                    congestion_index AS CongestionIndex,
                    recommended_speed AS RecommendedSpeed,
                    vehicle_count AS VehicleCount
                FROM segment_metrics
                ORDER BY segment_id
            ";

            var result = await conn.QueryAsync<SegmentDto>(sql);

            return Ok(result);
        }

        // ================= SUMMARY =================
        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary()
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            string sql = @"
                SELECT DISTINCT ON (segment_id)
                    segment_id AS SegmentId,
                    avg_speed AS AvgSpeed,
                    congestion_index AS CongestionIndex,
                    recommended_speed AS RecommendedSpeed,
                    vehicle_count AS VehicleCount,
                    timestamp
                FROM segment_metrics
                ORDER BY segment_id, timestamp DESC
            ";

            var segments = (await conn.QueryAsync<SegmentDto>(sql)).ToList();

            if (!segments.Any())
                return Ok(new { });

            var totalVehicles = segments.Sum(s => s.VehicleCount);
            var avgSpeed = totalVehicles == 0
            ? 0
            : segments.Sum(s => s.AvgSpeed * s.VehicleCount) / totalVehicles;
            var avgCongestion = segments.Average(s => s.CongestionIndex);

            var worst = segments.OrderByDescending(s => s.CongestionIndex).First();

            // 🔥 street-level recommended speed
            var streetSpeed = segments.Min(s => s.RecommendedSpeed);

            return Ok(new
            {
                totalVehicles,
                avgSpeed,
                avgCongestion,
                worstSegment = worst.SegmentId,
                recommendedSpeed = streetSpeed
            });
        }

        // ================= TRAFFIC LIGHT =================
        [HttpGet("light")]
        public async Task<IActionResult> GetTrafficLight()
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            string sql = @"
        SELECT 
            state AS State,
            next_state AS NextState,
            duration AS Duration,
            recommended_speed AS RecommendedSpeed,
            timestamp AS TimeStamp
        FROM traffic_light
        ORDER BY timestamp DESC
        LIMIT 1
    ";

            var result = await conn.QueryFirstOrDefaultAsync<TrafficLightDto>(sql);

            if (result == null)
                return Ok(new { });

            return Ok(result);
        }
    }

    public class SegmentDto
    {
        public int SegmentId { get; set; }
        public double AvgSpeed { get; set; }
        public double CongestionIndex { get; set; }
        public double RecommendedSpeed { get; set; }

        public double VehicleCount { get; set; }
    }

    public class TrafficLightDto
    {
        public string State { get; set; }
        public string NextState { get; set; }
        public int Duration { get; set; }
        public double RecommendedSpeed { get; set; }

        public DateTime TimeStamp { get; set; }
    }
}