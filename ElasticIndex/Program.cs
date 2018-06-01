// Copyright (c) 2007-2018 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu-server/master/LICENCE

using System;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Dapper;
using Microsoft.Extensions.Configuration;

namespace ElasticIndex
{
    public class Program
    {
        // TODO: readonly
        public static readonly IImmutableList<string> VALID_MODES = ImmutableList.Create("osu", "mania", "taiko", "fruits");

        internal static IConfigurationRoot Configuration { get; private set; }

        public void Run()
        {
            bool isWatching = new [] { "1", "true" }.Contains((Configuration["watch"] ?? string.Empty).ToLowerInvariant());
            if (isWatching) Console.WriteLine("Running in watch mode.");

            long? resumeFrom = null;
            if (!string.IsNullOrEmpty(Configuration["resume_from"]))
                resumeFrom = long.Parse(Configuration["resume_from"]);

            bool ranOnce = false;

            while (!ranOnce || isWatching)
            {
                // When running in watch mode, the indexer should be told to resume from the
                // last known saved point instead of the configured value.
                RunLoop(ranOnce ? null : resumeFrom);
                ranOnce = true;
                if (isWatching) Thread.Sleep(10000);
            }
        }

        public void RunLoop(long? resumeFrom)
        {
            var prefix = Configuration["elasticsearch:prefix"];
            var modesStr = Configuration["modes"] ?? string.Empty;
            var modes = modesStr.Split(',', StringSplitOptions.RemoveEmptyEntries);
            var suffix = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

            foreach (var mode in modes.Intersect(VALID_MODES))
            {
                var indexName = $"{prefix}high_scores_{mode}";
                var upcase = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(mode);
                var className = $"{typeof(HighScore).Namespace}.HighScore{upcase}";

                Type indexerType = typeof(HighScoreIndexer<>)
                    .MakeGenericType(Type.GetType(className, true));

                dynamic indexer = Activator.CreateInstance(indexerType);
                indexer.Suffix = suffix;
                indexer.Name = indexName;
                indexer.ResumeFrom = resumeFrom;
                indexer.Run();
            }
        }

        public static void Main()
        {
            Configuration = buildConfiguration();
            DefaultTypeMap.MatchNamesWithUnderscores = true;
            new Program().Run();
        }

        private static IConfigurationRoot buildConfiguration()
        {
            var env = Environment.GetEnvironmentVariable("APP_ENV") ?? "development";

            return new ConfigurationBuilder()
                   .SetBasePath(Directory.GetCurrentDirectory())
                   .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                   .AddJsonFile($"appsettings.{env}.json", optional: true, reloadOnChange: false)
                   .AddEnvironmentVariables()
                   .Build();
        }
    }
}
