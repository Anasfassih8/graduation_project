using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TrafficWorker.models;

namespace TrafficWorker.services
{
    public class TrafficPolicyService
    {
        public DayType GetDayType(DateTime now)
        {
            if (IsHoliday(now)) return DayType.Holiday;

            if (now.DayOfWeek == DayOfWeek.Friday ||
                now.DayOfWeek == DayOfWeek.Saturday)
                return DayType.Weekend;

            return DayType.Weekday;
        }

        public TrafficPeriod GetTrafficPeriod(DateTime now)
        {
            var hour = now.Hour;

            if ((hour >= 7 && hour <= 10) || (hour >= 16 && hour <= 19))
                return TrafficPeriod.RushHour;

            if (hour >= 22 || hour <= 5)
                return TrafficPeriod.FreeFlow;

            return TrafficPeriod.Normal;
        }

        public double GetBaseSpeed(RoadType roadType)
        {
            return roadType switch
            {
                RoadType.Highway => 100,
                RoadType.MainRoad => 60,
                RoadType.Urban => 40,
                _ => 60
            };
        }

        public double AdjustSpeed(double baseSpeed, DayType day, TrafficPeriod period)
        {
            double factor = 1.0;

            // Rush hour → reduce
            if (period == TrafficPeriod.RushHour)
                factor *= 0.8;

            // Free flow → increase
            if (period == TrafficPeriod.FreeFlow)
                factor *= 1.1;

            // Weekend → slightly faster
            if (day == DayType.Weekend)
                factor *= 1.05;

            // Holiday → less traffic
            if (day == DayType.Holiday)
                factor *= 1.1;

            return baseSpeed * factor;
        }

        private bool IsHoliday(DateTime date)
        {
            // TODO: Replace with real holiday list
            return false;
        }
    }
}
