using UnityEngine;

namespace Project.Progression
{
    public static class RunProgressTracker
    {
        private const string BestSegmentKey = "run.best.segment";

        private static bool _runStarted;
        private static int _runEssenceEarned;

        public static void EnsureRunStarted()
        {
            if (_runStarted)
                return;

            _runStarted = true;
            _runEssenceEarned = 0;
        }

        public static void AddRunEssence(int amount)
        {
            if (amount <= 0)
                return;

            EnsureRunStarted();
            _runEssenceEarned += amount;
        }

        public static int GetRunEssenceEarned()
        {
            return Mathf.Max(0, _runEssenceEarned);
        }

        public static int GetBestSegment()
        {
            return Mathf.Max(0, PlayerPrefs.GetInt(BestSegmentKey, 0));
        }

        public static void FinalizeRun(int segmentReached)
        {
            int best = GetBestSegment();
            if (segmentReached > best)
            {
                PlayerPrefs.SetInt(BestSegmentKey, segmentReached);
                PlayerPrefs.Save();
            }

            _runStarted = false;
        }
    }
}
