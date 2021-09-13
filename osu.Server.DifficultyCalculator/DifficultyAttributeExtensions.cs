// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Game.Rulesets.Catch.Difficulty;
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
                    yield return (11, attributes.StarRating);
                    yield return (17, osu.FlashlightStrain);

                    break;

                case TaikoDifficultyAttributes taiko:
                    yield return (9, taiko.MaxCombo);
                    yield return (11, attributes.StarRating);
                    yield return (13, taiko.GreatHitWindow);

                    break;

                case CatchDifficultyAttributes @catch:
                    // Todo: Catch should not output star rating in the 'aim' attribute.
                    yield return (1, @catch.StarRating);
                    yield return (7, @catch.ApproachRate);
                    yield return (9, @catch.MaxCombo);

                    break;

                case ManiaDifficultyAttributes mania:
                    // Todo: Mania doesn't output MaxCombo attribute for some reason.
                    yield return (11, attributes.StarRating);
                    yield return (13, mania.GreatHitWindow);
                    yield return (15, mania.ScoreMultiplier);

                    break;
            }
        }
    }
}
