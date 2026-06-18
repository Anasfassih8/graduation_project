using Dapper;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Traffic.API.Models;

namespace Traffic.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ViolationsController : ControllerBase
{
    private readonly IConfiguration _config;

    public ViolationsController(IConfiguration config) => _config = config;

    /// <summary>
    /// Returns the most recent violations (newest first).
    /// Use limit to control how many rows the dashboard fetches on initial load.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetViolations([FromQuery] int limit = 100)
    {
        var connStr = _config.GetConnectionString("DefaultConnection");
        await using var conn = new NpgsqlConnection(connStr);

        const string sql = """
            SELECT id, plate_number AS PlateNumber, speed_kmh AS SpeedKmh,
                   track_id AS TrackId, road_id AS RoadId,
                   detected_at AS DetectedAt, created_at AS CreatedAt
            FROM   violations
            ORDER  BY detected_at DESC
            LIMIT  @limit
            """;

        var rows = await conn.QueryAsync<Violation>(sql, new { limit });
        return Ok(rows);
    }
}