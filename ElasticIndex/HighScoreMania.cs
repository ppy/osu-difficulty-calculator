// Copyright (c) 2007-2018 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu-server/master/LICENCE

using Dapper.Contrib.Extensions;

namespace ElasticIndex
{
    [Table("osu_scores_mania_high")]
    public class HighScoreMania : HighScore
    {
    }
}
