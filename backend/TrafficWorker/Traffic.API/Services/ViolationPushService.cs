using Dapper;
using Microsoft.AspNetCore.SignalR;
using Npgsql;
using Traffic.API.Hubs;
using Traffic.API.Models;

namespace Traffic.API.Services;

public class ViolationsPushService : BackgroundService
{
    private readonly IHubContext<TrafficHub> _hub;
    private readonly IConfiguration _config;
    private readonly ILogger<ViolationsPushService> _logger;

    // Only push violations that arrive after the API starts
    private DateTime _lastPushedAt = DateTime.UtcNow;

    public ViolationsPushService(
        IHubContext<TrafficHub> hub,
        IConfiguration config,
        ILogger<ViolationsPushService> logger)
    {
        _hub = hub;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await PushNewViolationsAsync();
            await Task.Delay(3_000, stoppingToken);
        }
    }

    private async Task PushNewViolationsAsync()
    {
        try
        {
            var connStr = _config.GetConnectionString("DefaultConnection");
            await using var conn = new NpgsqlConnection(connStr);

            const string sql = """
                SELECT id, plate_number AS PlateNumber, speed_kmh AS SpeedKmh,
                       track_id AS TrackId, road_id AS RoadId,
                       detected_at AS DetectedAt, created_at AS CreatedAt
                FROM   violations
                WHERE  created_at > @since
                ORDER  BY created_at ASC
                """;

            var rows = (await conn.QueryAsync<Violation>(sql, new { since = _lastPushedAt }))
                       .ToList();

            if (rows.Count == 0) return;

            _lastPushedAt = rows.Max(v => v.CreatedAt);

            await _hub.Clients.All.SendAsync("ReceiveViolations", rows);

            _logger.LogInformation("[ViolationsPush] Pushed {Count} violation(s)", rows.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ViolationsPush] Error pushing violations");
        }
    }
}