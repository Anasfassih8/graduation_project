using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Npgsql;
using Dapper;
using TrafficWorker.services;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly TrafficService _trafficService = new();

    private IConnection _connection; //connection to RabbitMQ
    private IModel _channel; //communication channel which sends/receives messsages
    private Timer _timer;

    private HashSet<int> seenVehicles = new(); //tracks unique Vehicle IDs
    private int totalCount = 0; //total count of unique vehicles

    private readonly string _connectionString =
        "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=postgres";

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;

        var factory = new ConnectionFactory() { HostName = "localhost" };
        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();

        _channel.QueueDeclare(
            queue: "vehicle_data_v2",
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null
        );
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _timer = new Timer(async _ =>
        {
            var metrics = _trafficService.Calculate();
            Console.WriteLine("Calculating metrics...");

            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            foreach (var m in metrics)
            {
                Console.WriteLine($"Segment {m.SegmentId} CI: {m.CongestionIndex}");
                _logger.LogInformation(
                    $"Segment {m.SegmentId} | CI: {m.CongestionIndex:F2} | RecSpeed: {m.RecommendedSpeed:F1}");

                string sql = @"
            INSERT INTO segment_metrics 
            (segment_id, avg_speed, density, congestion_index, recommended_speed)
            VALUES (@SegmentId, @AvgSpeed, @Density, @CongestionIndex, @RecommendedSpeed)
        ";

                await conn.ExecuteAsync(sql, m);
            }

        }, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        var consumer = new EventingBasicConsumer(_channel);

        consumer.Received += async (model, ea) =>
        {
            //convert from byte => string
            var body = ea.Body.ToArray();
            var json = Encoding.UTF8.GetString(body);

            try
            {
                //  DESERIALIZE JSON TO C# OBJECT
                var vehicle = JsonSerializer.Deserialize<VehicleEvent>(json);

                if (vehicle == null) return;

                _trafficService.AddVehicle(vehicle);

                //  COUNT UNIQUE VEHICLES
                if (!seenVehicles.Contains(vehicle.vehicle_id))
                {
                    seenVehicles.Add(vehicle.vehicle_id);
                    totalCount++;

                    _logger.LogInformation($"New Vehicle Count: {totalCount}");
                }

                //  INSERT INTO POSTGRES
                using var conn = new NpgsqlConnection(_connectionString);//needs fixing
                await conn.OpenAsync();

                string sql = @"
                    INSERT INTO vehicle_events (vehicle_id, speed_kmh, pos_x, pos_y, timestamp)
                    VALUES (@vehicle_id, @speed_kmh, @x, @y, @timestamp)
                ";

                await conn.ExecuteAsync(sql, new
                {
                    vehicle.vehicle_id,
                    vehicle.speed_kmh,
                    x = vehicle.position.x,
                    y = vehicle.position.y,
                    vehicle.timestamp
                });

                


                _channel.BasicAck(ea.DeliveryTag, false);//processed ==> remove it from queue
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message");
                _channel.BasicNack(ea.DeliveryTag, false, true);//not processed ==> put it back in queue 
            }
        };

        _channel.BasicConsume(
            queue: "vehicle_data_v2",
            autoAck: false,
            consumer: consumer
        );

        return Task.CompletedTask;
    }

    public override void Dispose()
    {
        _channel?.Close();
        _connection?.Close();
        base.Dispose();
    }
}

// 📦 MODEL (matches your Python JSON)
public class VehicleEvent
{
    public int vehicle_id { get; set; }
    public double speed_kmh { get; set; }

    public Position position { get; set; }
    public DateTime timestamp { get; set; }
}

public class Position
{
    public double x { get; set; }
    public double y { get; set; }
}