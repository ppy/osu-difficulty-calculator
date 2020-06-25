// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Server.DifficultyCalculator
{
    public static class AppSettings
    {
        /// <summary>
        /// Whether to insert entries into the beatmaps table should they not exist. Should be false for production (beatmaps should already exist).
        /// </summary>
        public static readonly bool InsertBeatmaps;

        /// <summary>
        /// A full or relative path used to store beatmaps.
        /// </summary>
        public static readonly string BeatmapsPath;

        /// <summary>
        /// Whether beatmaps should be downloaded if they don't exist in <see cref="BeatmapsPath"/>.
        /// </summary>
        public static readonly bool AllowDownload;

        /// <summary>
        /// A URL used to download beatmaps with {0} being replaced with the beatmap_id.
        /// ie. "https://osu.ppy.sh/osu/{0}"
        /// </summary>
        public static readonly string DownloadPath;

        /// <summary>
        /// Whether downloaded files should be cached to <see cref="BeatmapsPath"/>.
        /// </summary>
        public static readonly bool SaveDownloaded;

        /// <summary>
        /// Whether the difficulty command should wait for docker to be ready and perform automatic operations.
        /// </summary>
        public static readonly bool RunAsSandboxDocker;

        static AppSettings()
        {
            bool.TryParse(Environment.GetEnvironmentVariable("INSERT_BEATMAPS"), out InsertBeatmaps);
            bool.TryParse(Environment.GetEnvironmentVariable("ALLOW_DOWNLOAD"), out AllowDownload);
            bool.TryParse(Environment.GetEnvironmentVariable("SAVE_DOWNLOADED"), out SaveDownloaded);

            BeatmapsPath = Environment.GetEnvironmentVariable("BEATMAPS_PATH") ?? "osu";
            DownloadPath = Environment.GetEnvironmentVariable("DOWNLOAD_PATH") ?? "https://osu.ppy.sh/osu/{0}";

            RunAsSandboxDocker = Environment.GetEnvironmentVariable("DOCKER")?.Contains("1") ?? false;
        }
    }
}
