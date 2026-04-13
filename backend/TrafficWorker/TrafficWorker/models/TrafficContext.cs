using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TrafficWorker.models
{
    public enum RoadType
    {
        Highway,
        MainRoad,
        Urban
    }

    public enum DayType
    {
        Weekday,
        Weekend,
        Holiday
    }

    public enum TrafficPeriod
    {
        RushHour,
        Normal,
        FreeFlow
    }
    internal class TrafficContext
    {
    }
}
