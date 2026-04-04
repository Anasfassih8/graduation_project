using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TrafficWorker.models
{
    public class SegmentMetrics
    {
        public int SegmentId { get; set; }
        public double Density { get; set; }
        public double AvgSpeed { get; set; }
        public double CongestionIndex { get; set; }
        public double RecommendedSpeed { get; set; }
        public int VehicleCount { get; set; }
    }
}
