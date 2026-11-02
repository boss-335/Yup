using System;
using System.Threading.Tasks;
using UnityEngine;
using VoiceHorror.Core;
using VoiceHorror.Dsp;

namespace VoiceHorror.Audio
{
    /// <summary>
    /// Runs the pure-C# DSP on a worker thread so analysis of a 10 s performance never stalls a frame.
    /// Results are handed back on the main thread via the returned job's polling.
    /// </summary>
    public static class ScoringService
    {
        public sealed class Job<T>
        {
            internal Task<T> Task;
            public bool IsDone => Task.IsCompleted;
            public bool Failed => Task.IsFaulted;
            public T Result => Task.Status == TaskStatus.RanToCompletion ? Task.Result : default;
            public string Error => Task.Exception?.GetBaseException().Message;
        }

        public static DspConfig CurrentConfig() => GameConfig.Tuning.ToDspConfig();

        /// <summary>Extract features from raw audio and score them against a reference.</summary>
        public static Job<(ScoreResult score, TakeFeatures perf)> ScoreAsync(VoiceReference reference, float[] audio, int sampleRate)
        {
            var cfg = CurrentConfig();
            return new Job<(ScoreResult, TakeFeatures)>
            {
                Task = Task.Run(() =>
                {
                    var fx = new FeatureExtractor(cfg) { ComputeMfcc = reference.Type == ReferenceType.Phrase };
                    var frames = fx.Extract(audio, sampleRate);
                    var perf = TakeBuilder.Build(frames, reference.Type, cfg);
                    var score = new VoiceScorer(cfg).Score(reference, perf);
                    return (score, perf);
                })
            };
        }

        /// <summary>Validate one reference take (length, loudness, clipping, voicing).</summary>
        public static Job<TakeCheck> CheckTakeAsync(float[] audio, int sampleRate, ReferenceType type, float minSec, float maxSec, float noiseFloorDb)
        {
            var cfg = CurrentConfig();
            return new Job<TakeCheck>
            {
                Task = Task.Run(() => new ReferenceValidator(cfg).CheckTake(audio, sampleRate, type, minSec, maxSec, noiseFloorDb))
            };
        }

        public static float TakeSimilarity(TakeFeatures a, TakeFeatures b, ReferenceType type)
            => new ReferenceValidator(CurrentConfig()).TakeSimilarity(a, b, type);

        public static VoiceReference BuildReference(string weaponId, ReferenceType type, params TakeFeatures[] takes)
            => new ReferenceValidator(CurrentConfig()).BuildReference(weaponId, type, takes);

        public static void LogIfFailed<T>(Job<T> job, string what)
        {
            if (job != null && job.Failed) Debug.LogError($"[Scoring] {what} failed: {job.Error}");
        }
    }
}
