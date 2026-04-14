using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System;

namespace TrafficWorker.services
{
    public class TrafficLightResult
    {
        public string State { get; set; } // GREEN, RED, YELLOW
        public string NextState { get; set; } // used only in YELLOW
        public int Duration { get; set; } // seconds
        public double RecommendedSpeed { get; set; }

        public bool IsNewCycle { get; set; }
    }

    public class TrafficLightService
    {
        private DateTime _stateEndTime = DateTime.MinValue;
        private TrafficLightResult _currentResult = null;

        private const double FreeFlowSpeed = 60.0;

        private const int MinGreen = 20;
        private const int MaxGreen = 60;

        private const int MinRed = 15;
        private const int MaxRed = 50;

        private const int YellowDuration = 5;

        private string _currentState = "GREEN";

        public TrafficLightResult Calculate(double streetRecommendedSpeed)
        {
            var now = DateTime.Now;

            // 🔒 If still in current state → KEEP IT
            if (_currentResult != null && now < _stateEndTime)
            {
                _currentResult.IsNewCycle = false;
                return _currentResult;
            }

            double normalized = streetRecommendedSpeed / FreeFlowSpeed;
            normalized = Math.Clamp(normalized, 0, 1);

            string newState = streetRecommendedSpeed >= 35 ? "GREEN" : "RED";

            // 🚨 Handle transition (YELLOW)
            if (_currentResult != null && newState != _currentState)
            {
                var yellow = new TrafficLightResult
                {
                    State = "YELLOW",
                    NextState = newState,
                    Duration = YellowDuration,
                    RecommendedSpeed = streetRecommendedSpeed,
                    IsNewCycle = true
                };

                _currentState = newState;

                _stateEndTime = now.AddSeconds(YellowDuration);
                _currentResult = yellow;

                return yellow;
            }

            // ⏱️ Calculate durations
            int greenTime = (int)(MinGreen + (normalized * (MaxGreen - MinGreen)));
            int redTime = (int)(MinRed + ((1 - normalized) * (MaxRed - MinRed)));

            int duration = newState == "GREEN" ? greenTime : redTime;

            var result = new TrafficLightResult
            {
                State = newState,
                NextState = newState,
                Duration = duration,
                RecommendedSpeed = streetRecommendedSpeed,
                IsNewCycle = true
            };

            _currentState = newState;
            _stateEndTime = now.AddSeconds(duration);
            _currentResult = result;

            return result;
        }
    }
}
