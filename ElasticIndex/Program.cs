// Copyright (c) 2007-2018 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu-server/master/LICENCE

using System;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using Dapper;
using Microsoft.Extensions.Configuration;

namespace ElasticIndex
{
    class Program
    {
        // TODO: readonly
        public static readonly IImmutableList<string> ValidModes = ImmutableList.Create("osu", "mania", "taiko", "fruits");

        internal static IConfigurationRoot Configuration { get; private set; }

        public void Run()
        {
            var prefix = Configuration["elasticsearch:prefix"];
            var modesStr = Configuration["modes"] ?? string.Empty;
            var modes = modesStr.Split(',', StringSplitOptions.RemoveEmptyEntries);
            var suffix = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

            foreach (var mode in modes.Intersect(ValidModes))
            {
                var indexName = $"{prefix}high_scores_{mode}";
                var upcase = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(mode);
                var className = $"{typeof(HighScore).Namespace}.HighScore{upcase}";

                Type indexerType = typeof(HighScoreIndexer<>)
                    .MakeGenericType(Type.GetType(className, true));

                dynamic indexer = Activator.CreateInstance(indexerType);
                indexer.Suffix = suffix;
                indexer.Run(indexName);
            }
        }

        static void Main(string[] args)
        {
            Configuration = BuildConfiguration();
            DefaultTypeMap.MatchNamesWithUnderscores = true;
            new Program().Run();
        }

        private static IConfigurationRoot BuildConfiguration()
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
