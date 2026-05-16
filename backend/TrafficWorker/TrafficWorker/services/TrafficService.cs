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
        private const double SegmentLength = 10.0;
        private const double MaxDensity = 0.3;
        private const double MinX = 0.0;
        private const double MinValidSpeed = 2.0; // below this = tracking artifact

        private readonly Dictionary<int, Dictionary<int, VehicleEvent>> _segments = new();
        private readonly TrafficPolicyService _policy = new();
        private readonly object _lock = new();

        private int GetSegmentIndex(double position) => (int)(position / SegmentLength);

        public void AddVehicle(VehicleEvent v)
        {
            lock (_lock)
            {
                int segmentId = GetSegmentIndex(v.position.y);
                if (!_segments.ContainsKey(segmentId))
                    _segments[segmentId] = new Dictionary<int, VehicleEvent>();
                _segments[segmentId][v.vehicle_id] = v;
            }
        }

        public List<SegmentMetrics> Calculate()
        {
            lock (_lock)
            {
                var now = DateTime.Now;
                var dayType = _policy.GetDayType(now);
                var period = _policy.GetTrafficPeriod(now);
                var baseSpeed = _policy.GetBaseSpeed(RoadType.MainRoad);
                var adjustedSpeed = _policy.AdjustSpeed(baseSpeed, dayType, period);

                var results = new List<SegmentMetrics>();

                foreach (var segment in _segments)
                {
                    var vehicles = segment.Value.Values.ToList();
                    if (vehicles.Count == 0) continue;

                    int count = vehicles.Count;

                    // exclude tracking artifacts from speed calculation
                    var movingVehicles = vehicles
                        .Where(v => v.speed_kmh >= MinValidSpeed)
                        .ToList();

                    double avgSpeed;
                    if (movingVehicles.Count == 0)
                        avgSpeed = 0; // all vehicles appear stopped
                    else
                        avgSpeed = Math.Min(
                            movingVehicles.Average(v => v.speed_kmh),
                            adjustedSpeed
                        );

                    double density = (double)count / SegmentLength;
                    double densityNorm = Math.Min(density / MaxDensity, 1.0);

                    double speedNorm = adjustedSpeed > 0
                        ? Math.Clamp((adjustedSpeed - avgSpeed) / adjustedSpeed, 0, 1)
                        : 0;

                    double ci = (0.6 * densityNorm) + (0.4 * speedNorm);
                    ci = Math.Clamp(ci, 0, 1);

                    // single vehicle = low confidence, halve its CI impact
                    if (count == 1)
                        ci *= 0.5;

                    double recommendedSpeed = adjustedSpeed * (1 - ci * 0.7);

                    results.Add(new SegmentMetrics
                    {
                        SegmentId = segment.Key,
                        VehicleCount = count,
                        AvgSpeed = (int)Math.Round(avgSpeed),
                        Density = Math.Round(density, 2),
                        CongestionIndex = Math.Round(ci, 2),
                        RecommendedSpeed = (int)Math.Round(recommendedSpeed)
                    });
                }

                if (results.Any())
                    _segments.Clear();

                return results;
            }
        }

        public double GetStreetRecommendedSpeed(List<SegmentMetrics> segments)
        {
            var now = DateTime.Now;
            var adjustedSpeed = _policy.AdjustSpeed(
                _policy.GetBaseSpeed(RoadType.MainRoad),
                _policy.GetDayType(now),
                _policy.GetTrafficPeriod(now)
            );

            if (!segments.Any()) return (int)Math.Round(adjustedSpeed);

            // only consider segments with 2+ vehicles for the street decision
            var reliable = segments.Where(s => s.VehicleCount >= 2).ToList();
            var source = reliable.Any() ? reliable : segments;

            // take the worst segment but weighted by how many cars are in it
            double streetSpeed = source
                .Select(s => new {
                    s.RecommendedSpeed,
                    Weight = s.VehicleCount
                })
                .OrderBy(s => s.RecommendedSpeed)
                .ThenByDescending(s => s.Weight)
                .First()
                .RecommendedSpeed;

            return (int)Math.Round(streetSpeed);
        }
    }
 }
