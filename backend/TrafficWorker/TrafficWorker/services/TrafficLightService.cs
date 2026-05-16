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
        private string _currentState = "GREEN";
        private int _consecutiveOppositeReadings = 0;  // add this

        private const double FreeFlowSpeed = 60.0;
        private const int MinGreen = 20, MaxGreen = 60;
        private const int MinRed = 15, MaxRed = 50;
        private const int YellowDuration = 5;
        private const int RequiredReadings = 2; // consecutive ticks needed to change mid-cycle

        public TrafficLightResult Calculate(double streetRecommendedSpeed)
        {
            var now = DateTime.Now;
            string wouldBe = streetRecommendedSpeed >= 35 ? "GREEN" : "RED";

            if (_currentResult != null && now < _stateEndTime)
            {
                if (_currentState == "YELLOW")
                {
                    // during YELLOW, ignore opposite readings until duration ends
                    _currentResult.IsNewCycle = false;
                    return _currentResult;
                }
                if (wouldBe == _currentState)
                {
                    // state agrees — reset the opposite counter
                    _consecutiveOppositeReadings = 0;
                    _currentResult.IsNewCycle = false;
                    return _currentResult;
                }

                // state disagrees — require 2 consecutive readings before acting
                _consecutiveOppositeReadings++;
                if (_consecutiveOppositeReadings < RequiredReadings)
                {
                    _currentResult.IsNewCycle = false;
                    return _currentResult; // not convinced yet, hold current state
                }

                _consecutiveOppositeReadings = 0;
                // fall through — two consecutive opposite readings confirmed
            }

            double normalized = Math.Clamp(streetRecommendedSpeed / FreeFlowSpeed, 0, 1);

            // YELLOW transition
            if (_currentResult != null && wouldBe != _currentState)
            {
                var yellow = new TrafficLightResult
                {
                    State = "YELLOW",
                    NextState = wouldBe,
                    Duration = YellowDuration,
                    RecommendedSpeed = (int)Math.Round(streetRecommendedSpeed),
                    IsNewCycle = true
                };
                _currentState = wouldBe;
                _stateEndTime = now.AddSeconds(YellowDuration);
                _currentResult = yellow;
                return yellow;
            }

            int greenTime = (int)(MinGreen + (normalized * (MaxGreen - MinGreen)));
            int redTime = (int)(MinRed + ((1 - normalized) * (MaxRed - MinRed)));
            int duration = wouldBe == "GREEN" ? greenTime : redTime;

            var result = new TrafficLightResult
            {
                State = wouldBe,
                NextState = wouldBe,
                Duration = duration,
                RecommendedSpeed = (int)Math.Round(streetRecommendedSpeed),
                IsNewCycle = true
            };
            _currentState = wouldBe;
            _stateEndTime = now.AddSeconds(duration);
            _currentResult = result;
            return result;
        }
    }
}

