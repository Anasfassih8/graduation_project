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
        private string _targetState = null; // decided in the look-ahead window

        private const int DecisionWindowSeconds = 10; // check road this many seconds before end
        private const double SpeedThreshold = 35.0;
        private const double FreeFlowSpeed = 60.0;
        private const int MinGreen = 30, MaxGreen = 60;
        private const int MinRed = 20, MaxRed = 50;
        private const int YellowDuration = 5;

        public TrafficLightResult Calculate(double streetRecommendedSpeed)
        {
            var now = DateTime.Now;
            double secondsRemaining = _stateEndTime == DateTime.MinValue
                ? 0
                : (_stateEndTime - now).TotalSeconds;

            // ── ACTIVE STATE ──────────────────────────────────────────────
            if (secondsRemaining > 0 && _currentResult != null)
            {
                // YELLOW is locked completely — no logic runs during transition
                if (_currentState == "YELLOW")
                {
                    _currentResult.IsNewCycle = false;
                    return _currentResult;
                }

                // Look-ahead window: decide next state once
                if (secondsRemaining <= DecisionWindowSeconds && _targetState == null)
                {
                    _targetState = streetRecommendedSpeed >= SpeedThreshold ? "GREEN" : "RED";

                    // Update the result so dashboard sees the upcoming NextState
                    _currentResult.NextState = _targetState;
                    _currentResult.IsNewCycle = true; // write this update to DB
                    return _currentResult;
                }

                // Outside decision window — fully locked, return cached state
                _currentResult.IsNewCycle = false;
                return _currentResult;
            }

            // ── STATE ENDED ───────────────────────────────────────────────

            // Coming out of YELLOW → go to the target we decided earlier
            if (_currentState == "YELLOW")
            {
                string dest = _targetState ?? "GREEN";
                _targetState = null;
                return CreateMainState(dest, streetRecommendedSpeed, now);
            }

            // Coming out of GREEN or RED
            string nextState = _targetState ?? (streetRecommendedSpeed >= SpeedThreshold ? "GREEN" : "RED");
            _targetState = null;

            // State changes → go through YELLOW
            if (nextState != _currentState)
                return CreateYellowState(nextState, streetRecommendedSpeed, now);

            // Same state → just renew the cycle
            return CreateMainState(nextState, streetRecommendedSpeed, now);
        }

        private TrafficLightResult CreateYellowState(string nextState, double speed, DateTime now)
        {
            _currentState = "YELLOW";
            _targetState = nextState;
            _stateEndTime = now.AddSeconds(YellowDuration);

            _currentResult = new TrafficLightResult
            {
                State = "YELLOW",
                NextState = nextState,
                Duration = YellowDuration,
                RecommendedSpeed = (int)Math.Round(speed),
                IsNewCycle = true
            };
            return _currentResult;
        }

        private TrafficLightResult CreateMainState(string state, double speed, DateTime now)
        {
            double normalized = Math.Clamp(speed / FreeFlowSpeed, 0, 1);
            int duration = state == "GREEN"
                ? (int)(MinGreen + normalized * (MaxGreen - MinGreen))
                : (int)(MinRed + (1 - normalized) * (MaxRed - MinRed));

            _currentState = state;
            _stateEndTime = now.AddSeconds(duration);

            _currentResult = new TrafficLightResult
            {
                State = state,
                NextState = "?",  // unknown until decision window
                Duration = duration,
                RecommendedSpeed = (int)Math.Round(speed),
                IsNewCycle = true
            };
            return _currentResult;
        }
    }
}


