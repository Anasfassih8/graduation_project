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
                SELECT DISTINCT ON (segment_id)
                    segment_id AS SegmentId,
                    avg_speed AS AvgSpeed,
                    density AS Density,
                    congestion_index AS CongestionIndex,
                    recommended_speed AS RecommendedSpeed
                FROM segment_metrics
                ORDER BY segment_id, id DESC
            ";

            var result = await conn.QueryAsync(sql);

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
                    recommended_speed AS RecommendedSpeed
                FROM segment_metrics
                ORDER BY segment_id, id DESC
            ";

            var segments = (await conn.QueryAsync<SegmentDto>(sql)).ToList();

            if (!segments.Any())
                return Ok(new { });

            var totalVehicles = segments.Count; // simplified for now
            var avgSpeed = segments.Average(s => s.AvgSpeed);
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
    }

    public class SegmentDto
    {
        public int SegmentId { get; set; }
        public double AvgSpeed { get; set; }
        public double CongestionIndex { get; set; }
        public double RecommendedSpeed { get; set; }
    }
}