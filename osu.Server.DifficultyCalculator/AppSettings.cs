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
        public static readonly bool INSERT_BEATMAPS;

        /// <summary>
        /// Whether to insert entries to `osu_difficulty_attributes`. This is quite an intensive operation, and may be skipped when not required (ie. for sandbox runs).
        /// </summary>
        public static readonly bool SKIP_INSERT_ATTRIBUTES;

        /// <summary>
        /// A full or relative path used to store beatmaps.
        /// </summary>
        public static readonly string BEATMAPS_PATH;

        /// <summary>
        /// Whether beatmaps should be downloaded if they don't exist in <see cref="BEATMAPS_PATH"/>.
        /// </summary>
        public static readonly bool ALLOW_DOWNLOAD;

        /// <summary>
        /// A URL used to download beatmaps with {0} being replaced with the beatmap_id.
        /// ie. "https://osu.ppy.sh/osu/{0}"
        /// </summary>
        public static readonly string DOWNLOAD_PATH;

        /// <summary>
        /// Whether downloaded files should be cached to <see cref="BEATMAPS_PATH"/>.
        /// </summary>
        public static readonly bool SAVE_DOWNLOADED;

        static AppSettings()
        {
            INSERT_BEATMAPS = Environment.GetEnvironmentVariable("INSERT_BEATMAPS") == "1";
            SKIP_INSERT_ATTRIBUTES = Environment.GetEnvironmentVariable("SKIP_INSERT_ATTRIBUTES") == "1";
            ALLOW_DOWNLOAD = Environment.GetEnvironmentVariable("ALLOW_DOWNLOAD") == "1";
            SAVE_DOWNLOADED = Environment.GetEnvironmentVariable("SAVE_DOWNLOADED") == "1";

            BEATMAPS_PATH = Environment.GetEnvironmentVariable("BEATMAPS_PATH") ?? "osu";
            DOWNLOAD_PATH = Environment.GetEnvironmentVariable("BEATMAP_DOWNLOAD_PATH") ?? "https://osu.ppy.sh/osu/{0}";
        }
    }
}
