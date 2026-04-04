using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TrafficWorker.models
{
    public class VehicleEvents
    {
        public int VehicleId { get; set; }
        public double Position { get; set; } // meters
        public double Speed { get; set; }    // km/h
        public DateTime Timestamp { get; set; }
    }
}
