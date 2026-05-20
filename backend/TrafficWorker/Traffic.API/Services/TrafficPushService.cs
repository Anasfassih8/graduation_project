using Dapper;
using Microsoft.AspNetCore.SignalR;
using Npgsql;
using Traffic.API.Controllers;
using Traffic.API.Hubs;

namespace Traffic.API.Services
{
    public class TrafficPushService : BackgroundService
    {
        private readonly IHubContext<TrafficHub> _hub;
        private readonly string _connectionString;

        public TrafficPushService(IHubContext<TrafficHub> hub, IConfiguration config)
        {
            _hub = hub;
            _connectionString = config.GetConnectionString("DefaultConnection");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PushLatestData();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Push error: {ex.Message}");
                }

                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }

        private async Task PushLatestData()
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            // ── Segments ──────────────────────────────────────────
            var segments = (await conn.QueryAsync<SegmentDto>(@"
                SELECT DISTINCT ON (segment_id)
                    segment_id       AS SegmentId,
                    avg_speed        AS AvgSpeed,
                    density          AS Density,
                    congestion_index AS CongestionIndex,
                    recommended_speed AS RecommendedSpeed,
                    vehicle_count    AS VehicleCount,
                    timestamp
                FROM segment_metrics
                ORDER BY segment_id, timestamp DESC
            ")).ToList();

            // ── Summary ───────────────────────────────────────────
            var totalVehicles = await conn.ExecuteScalarAsync<int>(
                "SELECT COALESCE(total_count, 0) FROM vehicle_stats WHERE id = 1"
            );

            if (segments.Any())
            {
                var segTotal = segments.Sum(s => s.VehicleCount);
                var avgSpeed = segTotal == 0 ? 0.0
                                : segments.Sum(s => s.AvgSpeed * s.VehicleCount) / segTotal;
                var avgCong = segments.Average(s => s.CongestionIndex);
                var worst = segments.OrderByDescending(s => s.CongestionIndex).First();
                var streetSpd = segments.Min(s => s.RecommendedSpeed);

                var summary = new
               {
                    totalVehicles,
                    avgSpeed = (int)Math.Round(avgSpeed),
                    avgCongestion = Math.Round(avgCong, 2),
                    worstSegment = worst.SegmentId,
                    recommendedSpeed = (int)Math.Round(streetSpd)
                };

                await _hub.Clients.All.SendAsync("SegmentUpdated", segments);
                await _hub.Clients.All.SendAsync("SummaryUpdated", summary);
            }

            // ── Traffic Light ─────────────────────────────────────
            var light = await conn.QueryFirstOrDefaultAsync<TrafficLightDto>(@"
                SELECT state              AS State,
                next_state         AS NextState,
                duration           AS Duration,
                recommended_speed  AS RecommendedSpeed,
                timestamp          AS TimeStamp
                FROM traffic_light WHERE id = 1
            ");

            if (light != null)
                await _hub.Clients.All.SendAsync("LightUpdated", light);
        }
    }
}