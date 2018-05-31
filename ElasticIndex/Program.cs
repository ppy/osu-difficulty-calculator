using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using MySql.Data.MySqlClient;
using Nest;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ElasticIndex
{
    class Program
    {
        // TODO: readonly
        public readonly static IImmutableList<string> ValidModes = ImmutableList.Create("osu", "mania", "taiko", "fruits");

        internal static IConfigurationRoot Configuration { get; private set; }

        public void Run()
        {
            var prefix = Configuration["elasticsearch:prefix"];
            var modesStr = Configuration["modes"] ?? String.Empty;
            var modes = modesStr.Split(',', StringSplitOptions.RemoveEmptyEntries);
            var suffix = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

            foreach (var mode in modes.Intersect(ValidModes))
            {
                var indexName = $"{prefix}high_scores_{mode}";
                var upcase = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(mode);
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
            Program.Configuration = BuildConfiguration();
            Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
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
