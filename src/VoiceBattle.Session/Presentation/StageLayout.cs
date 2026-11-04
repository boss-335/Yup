using System;
using System.Collections.Generic;
using System.Linq;

namespace VoiceBattle.Session.Presentation
{
    public sealed record StageSpot(string FighterId, float X, float Y, float Scale, int Facing, int SortingBase);

    /// <summary>
    /// Where each fighter stands on the stage (world units, floor at y = 0, stage ≈ −6.5…6.5):
    /// duel = one each side; teams = team A on the left facing right, team B on the right; free-for-all and
    /// Battle Ground = everyone across the stage, two rows past 8, all facing the centre. Characters shrink so
    /// up to 16 fit without overlapping.
    /// </summary>
    public static class StageLayout
    {
        public const float HalfWidth = 6.5f;
        public const float PuppetWidth = 1.5f;   // body + arms at scale 1

        public static IReadOnlyList<StageSpot> Arrange(IReadOnlyList<(string id, string team)> fighters, bool teamMode)
        {
            int n = fighters.Count;
            var spots = new List<StageSpot>();
            if (n == 0) return spots;
            if (n == 2 && !teamMode)
            {
                spots.Add(new StageSpot(fighters[0].id, -3.2f, 0, 1, 1, 0));
                spots.Add(new StageSpot(fighters[1].id, 3.2f, 0, 1, -1, 50));
                return spots;
            }
            if (teamMode)
            {
                var teams = fighters.Select(f => f.team).Distinct().ToList();
                for (int t = 0; t < teams.Count; t++)
                {
                    var members = fighters.Where(f => f.team == teams[t]).ToList();
                    int side = t == 0 ? -1 : 1;
                    float inner = 1.4f, width = HalfWidth - inner, slot = width / members.Count;
                    float scale = Math.Min(1f, slot / PuppetWidth * 1.15f);
                    for (int j = 0; j < members.Count; j++)
                    {
                        // Alternate a little depth so teammates read as a group, not a queue.
                        float y = j % 2 == 1 ? .35f : 0f;
                        float s = j % 2 == 1 ? scale * .92f : scale;
                        spots.Add(new StageSpot(members[j].id, side * (inner + (j + .5f) * slot), y, s, -side, (j % 2 == 1 ? 0 : 400) + t * 50 + j * 10));
                    }
                }
                return spots;
            }
            // Everyone for themself: one row up to 8, two rows beyond (back row smaller and higher).
            int rows = n > 8 ? 2 : 1;
            int front = (n + 1) / rows;
            for (int r = 0; r < rows; r++)
            {
                var row = fighters.Skip(r * front).Take(r == 0 ? front : n - front).ToList();
                float slot = 2 * HalfWidth / row.Count;
                float scale = Math.Min(1f, slot / PuppetWidth) * (r == 0 ? 1f : .82f);
                for (int j = 0; j < row.Count; j++)
                {
                    float x = -HalfWidth + (j + .5f) * slot + (r == 1 ? slot / 2 * (row.Count < front ? 1 : 0) : 0);
                    x = Math.Max(-HalfWidth + .4f, Math.Min(HalfWidth - .4f, x));
                    spots.Add(new StageSpot(row[j].id, x, r == 0 ? 0f : 1.1f, scale, x <= 0 ? 1 : -1, (r == 0 ? 400 : 0) + j * 20));
                }
            }
            return spots;
        }
    }
}
