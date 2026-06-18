using System.Text;
using System.Text.Json;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TrafficWorker.Models;

namespace TrafficWorker.Services;

public class ViolationConsumer : BackgroundService
{
    private readonly ILogger<ViolationConsumer> _logger;
    private readonly IConfiguration _config;
    private IConnection? _connection;
    private IModel? _channel;

    public ViolationConsumer(ILogger<ViolationConsumer> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _config["RabbitMQ:Host"] ?? "localhost"
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();

        _channel.QueueDeclare(
            queue: "violations",
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null
        );

        _channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

        var consumer = new EventingBasicConsumer(_channel);

        consumer.Received += async (_, ea) =>
        {
            var json = Encoding.UTF8.GetString(ea.Body.ToArray());
            _logger.LogInformation("[ViolationConsumer] Received: {Json}", json);

            try
            {
                var msg = JsonSerializer.Deserialize<ViolationMessage>(json);

                if (msg is not null)
                    await SaveViolationAsync(msg, stoppingToken);

                _channel.BasicAck(ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ViolationConsumer] Failed to process message");
                _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);
            }
        };

        _channel.BasicConsume(queue: "violations", autoAck: false, consumer: consumer);

        return Task.CompletedTask;
    }

    private async Task SaveViolationAsync(ViolationMessage msg, CancellationToken ct)
    {
        var connStr = _config.GetConnectionString("DefaultConnection");

        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync(ct);

        const string sql = """
            INSERT INTO violations (plate_number, speed_kmh, track_id, road_id, detected_at)
            VALUES (@plate, @speed, @trackId, @roadId, @detectedAt)
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("plate", msg.PlateNumber);
        cmd.Parameters.AddWithValue("speed", msg.SpeedKmh);
        cmd.Parameters.AddWithValue("trackId", msg.TrackId);
        cmd.Parameters.AddWithValue("roadId", msg.RoadId);
        cmd.Parameters.AddWithValue("detectedAt", msg.Timestamp.ToUniversalTime());

        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogInformation(
            "[ViolationConsumer] Saved → plate: {Plate}  speed: {Speed} km/h  road: {Road}",
            msg.PlateNumber, msg.SpeedKmh, msg.RoadId);
    }

    public override void Dispose()
    {
        _channel?.Close();
        _connection?.Close();
        base.Dispose();
    }
}