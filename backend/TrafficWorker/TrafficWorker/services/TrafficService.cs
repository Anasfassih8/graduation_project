using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TrafficWorker.models;

namespace TrafficWorker.services
{
    public class TrafficService
    {
        private const double RoadLength = 1000.0;
        private const double SegmentLength = 10.0;
        private const double FreeFlowSpeed = 60.0;
        private const double MaxDensity = 200.0;

        private readonly Dictionary<int, List<VehicleEvent>> _segments = new();
        private readonly object _lock = new();


        private int GetSegmentIndex(double position) => (int)(position / SegmentLength);
        public void AddVehicle(VehicleEvent v)
        {
            Console.WriteLine($"Adding vehicle {v.vehicle_id} at X={v.position.x}");
            lock (_lock)
            {
                int segmentId = GetSegmentIndex(v.position.x);
                Console.WriteLine($"Assigned to segment {segmentId}");

                if (!_segments.ContainsKey(segmentId))
                    _segments[segmentId] = new List<VehicleEvent>();

                _segments[segmentId].Add(v);
                Console.WriteLine($"Segment {segmentId} now has {_segments[segmentId].Count} vehicles");
            }
        }

        public List<SegmentMetrics> Calculate()
        {
            Console.WriteLine($"Total segments: {_segments.Count}");
            lock (_lock)
            {
                var now = DateTime.Now;
                var window = TimeSpan.FromSeconds(10);

                var results = new List<SegmentMetrics>();

                foreach (var segment in _segments)
                {
                    Console.WriteLine($"Processing segment {segment.Key} with {segment.Value.Count} vehicles");
                    int segmentId = segment.Key;

                    var vehicles = segment.Value;
                     

                    if (vehicles.Count == 0) continue;

                    int count = vehicles.Count;
                    double avgSpeed = vehicles.Average(v => v.speed_kmh);

                    double density = count / (SegmentLength / 1000.0);

                    double densityNorm = density / MaxDensity;
                    double speedNorm = (FreeFlowSpeed - avgSpeed) / FreeFlowSpeed;

                    double ci = (0.6 * densityNorm) + (0.4 * speedNorm);
                    ci = Math.Clamp(ci, 0, 1);

                    double recommendedSpeed = FreeFlowSpeed * (1 - ci * 0.7);

                    results.Add(new SegmentMetrics
                    {
                        SegmentId = segmentId,
                        VehicleCount = count,
                        AvgSpeed = avgSpeed,
                        Density = density,
                        CongestionIndex = ci,
                        RecommendedSpeed = recommendedSpeed
                    });
                }
                _segments.Clear();
                return results;
            }
        }

        public double GetStreetRecommendedSpeed(List<SegmentMetrics> segments)
        {
            if (!segments.Any()) return FreeFlowSpeed;

            // safest value = worst segment
            return segments.Min(s => s.RecommendedSpeed);
        }
    }
}
