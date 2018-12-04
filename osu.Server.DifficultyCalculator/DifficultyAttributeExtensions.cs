// Copyright (c) 2007-2018 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu-server/master/LICENCE

using System.Collections.Generic;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mania.Difficulty;
using osu.Game.Rulesets.Osu.Difficulty;
using osu.Game.Rulesets.Taiko.Difficulty;

namespace osu.Server.DifficultyCalculator
{
    public static class DifficultyAttributeExtensions
    {
        public static IEnumerable<(int id, object value)> Map(this DifficultyAttributes attributes)
        {
            switch (attributes)
            {
                case OsuDifficultyAttributes osu:
                    yield return (1, osu.AimStrain);
                    yield return (3, osu.SpeedStrain);
                    yield return (5, osu.OverallDifficulty);
                    yield return (7, osu.ApproachRate);
                    yield return (9, osu.MaxCombo);
                    break;
                case TaikoDifficultyAttributes taiko:
                    yield return (9, taiko.MaxCombo);
                    yield return (13, taiko.GreatHitWindow);
                    break;
                case ManiaDifficultyAttributes mania:
                    yield return (13, mania.GreatHitWindow);
                    break;
            }

            yield return (11, attributes.StarRating);
        }
    }
}
