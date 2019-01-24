// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.IO;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics.Textures;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.Rulesets.Catch;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Taiko;

namespace osu.Server.DifficultyCalculator
{
    /// <summary>
    /// A <see cref="WorkingBeatmap"/> which reads from a .osu file.
    /// </summary>
    public class LocalWorkingBeatmap : WorkingBeatmap
    {
        private readonly Beatmap beatmap;

        /// <summary>
        /// Constructs a new <see cref="LocalWorkingBeatmap"/> from a .osu file.
        /// </summary>
        /// <param name="file">The .osu file.</param>
        public LocalWorkingBeatmap(string file)
            : this(File.OpenRead(file))
        {
        }

        private LocalWorkingBeatmap(Stream stream)
            : this(new StreamReader(stream))
        {
            stream.Dispose();
        }

        private LocalWorkingBeatmap(StreamReader streamReader)
            : this(Decoder.GetDecoder<Beatmap>(streamReader).Decode(streamReader))
        {
        }

        private LocalWorkingBeatmap(Beatmap beatmap)
            : base(beatmap.BeatmapInfo)
        {
            this.beatmap = beatmap;

            switch (beatmap.BeatmapInfo.RulesetID)
            {
                case 0:
                    beatmap.BeatmapInfo.Ruleset = new OsuRuleset().RulesetInfo;
                    break;
                case 1:
                    beatmap.BeatmapInfo.Ruleset = new TaikoRuleset().RulesetInfo;
                    break;
                case 2:
                    beatmap.BeatmapInfo.Ruleset = new CatchRuleset().RulesetInfo;
                    break;
                case 3:
                    beatmap.BeatmapInfo.Ruleset = new ManiaRuleset().RulesetInfo;
                    break;
            }
        }

        protected override IBeatmap GetBeatmap() => beatmap;
        protected override Texture GetBackground() => null;
        protected override Track GetTrack() => null;
    }
}
