// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.IO;
using McMaster.Extensions.CommandLineUtils;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics.Textures;
using osu.Framework.IO.Network;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.Rulesets.Catch;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Taiko;

namespace osu.Server.DifficultyCalculator
{
    public static class BeatmapLoader
    {
        public static WorkingBeatmap GetBeatmap(int beatmapId, bool verbose, bool forceDownload, IReporter reporter)
        {
            string fileLocation = Path.Combine(AppSettings.BeatmapsPath, beatmapId.ToString()) + ".osu";

            if ((forceDownload || !File.Exists(fileLocation)) && AppSettings.AllowDownload)
            {
                if (verbose)
                    reporter.Verbose($"Downloading {beatmapId}.");

                var req = new WebRequest(string.Format(AppSettings.DownloadPath, beatmapId));

                req.Failed += _ =>
                {
                    if (verbose)
                        reporter.Error($"Failed to download {beatmapId}.");
                };

                req.Finished += () =>
                {
                    if (verbose)
                        reporter.Verbose($"{beatmapId} successfully downloaded.");
                };

                req.Perform();

                return req.ResponseStream != null ? new LoaderWorkingBeatmap(req.ResponseStream) : null;
            }

            return !File.Exists(fileLocation) ? null : new LoaderWorkingBeatmap(fileLocation);
        }

        private class LoaderWorkingBeatmap : WorkingBeatmap
        {
            private readonly Beatmap beatmap;

            /// <summary>
            /// Constructs a new <see cref="LocalWorkingBeatmap"/> from a .osu file.
            /// </summary>
            /// <param name="file">The .osu file.</param>
            public LoaderWorkingBeatmap(string file)
                : this(File.OpenRead(file))
            {
            }

            public LoaderWorkingBeatmap(Stream stream)
                : this(new StreamReader(stream))
            {
                stream.Dispose();
            }

            private LoaderWorkingBeatmap(StreamReader streamReader)
                : this(Decoder.GetDecoder<Beatmap>(streamReader).Decode(streamReader))
            {
            }

            private LoaderWorkingBeatmap(Beatmap beatmap)
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
}
