using System;
using System.Collections.Generic;
using System.Text;

namespace VoiceHorror.Dsp
{
    /// <summary>
    /// Stored reference for one weapon (spec §3.4). Contains extracted features only.
    /// </summary>
    public sealed class VoiceReference
    {
        public const int CurrentVersion = 1;

        public int Version = CurrentVersion;
        public string WeaponId = "";
        public ReferenceType Type = ReferenceType.Melody;
        /// <summary>Analysis sample rate the features were computed at.</summary>
        public int SampleRate;
        /// <summary>Hop (in samples at <see cref="SampleRate"/>) between frames.</summary>
        public int Hop;
        public List<TakeFeatures> Takes = new List<TakeFeatures>();

        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"version\": ").Append(Version).Append(",\n");
            sb.Append("  \"weaponId\": ").Append(MiniJson.Quote(WeaponId ?? "")).Append(",\n");
            sb.Append("  \"type\": ").Append(MiniJson.Quote(Type.ToString())).Append(",\n");
            sb.Append("  \"sampleRate\": ").Append(SampleRate).Append(",\n");
            sb.Append("  \"hop\": ").Append(Hop).Append(",\n");
            sb.Append("  \"takes\": [");
            for (int t = 0; t < Takes.Count; t++)
            {
                var take = Takes[t];
                sb.Append(t == 0 ? "\n" : ",\n");
                sb.Append("    { \"pitchSemitones\": ").Append(MiniJson.NumArray(take.PitchSemitones));
                sb.Append(", \"onset\": ").Append(MiniJson.NumArray(take.Onset));
                sb.Append(", \"mfcc\": ");
                if (take.Mfcc == null) sb.Append("null");
                else
                {
                    sb.Append('[');
                    for (int i = 0; i < take.Mfcc.Length; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append(MiniJson.NumArray(take.Mfcc[i]));
                    }
                    sb.Append(']');
                }
                sb.Append(", \"durationSec\": ").Append(MiniJson.Num(take.DurationSec)).Append(" }");
            }
            sb.Append("\n  ]\n}\n");
            return sb.ToString();
        }

        public static VoiceReference FromJson(string json)
        {
            if (!(MiniJson.Parse(json) is Dictionary<string, object> root)) throw new FormatException("Reference root must be an object");
            var r = new VoiceReference
            {
                Version = (int)MiniJson.GetNumber(root, "version", 1),
                WeaponId = MiniJson.GetString(root, "weaponId"),
                SampleRate = (int)MiniJson.GetNumber(root, "sampleRate", 16000),
                Hop = (int)MiniJson.GetNumber(root, "hop", 171),
            };
            if (r.Version > CurrentVersion) throw new FormatException("Unsupported reference version " + r.Version);
            r.Type = MiniJson.GetString(root, "type") == "Phrase" ? ReferenceType.Phrase : ReferenceType.Melody;

            if (root.TryGetValue("takes", out var takesObj) && takesObj is List<object> takes)
            {
                foreach (var o in takes)
                {
                    if (!(o is Dictionary<string, object> td)) continue;
                    var take = new TakeFeatures
                    {
                        PitchSemitones = MiniJson.GetFloatArray(td, "pitchSemitones"),
                        Onset = MiniJson.GetFloatArray(td, "onset") ?? Array.Empty<float>(),
                        DurationSec = (float)MiniJson.GetNumber(td, "durationSec"),
                    };
                    if (td.TryGetValue("mfcc", out var mo) && mo is List<object> rows)
                    {
                        take.Mfcc = new float[rows.Count][];
                        for (int i = 0; i < rows.Count; i++)
                        {
                            var row = rows[i] as List<object> ?? new List<object>();
                            var a = new float[row.Count];
                            for (int k = 0; k < row.Count; k++) a[k] = row[k] is double x ? (float)x : 0f;
                            take.Mfcc[i] = a;
                        }
                    }
                    r.Takes.Add(take);
                }
            }
            return r;
        }
    }
}
