using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TrafficWorker.models
{
    internal class VehicleState
    {
        public double Speed { get; set; }
        public double X { get; set; }
        public double Y { get; set; }

        public DateTime LastSeen { get; set; }
    }
}
