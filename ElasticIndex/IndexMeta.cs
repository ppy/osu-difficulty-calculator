// Copyright (c) 2007-2018 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu-server/master/LICENCE

using System;
using System.Collections.Generic;
using System.Linq;
using Nest;

namespace ElasticIndex
{
    [ElasticsearchType(Name = "index_meta", IdProperty = nameof(Index))]
    public class IndexMeta
    {
        private static readonly ElasticClient client = new ElasticClient(
            new ConnectionSettings(
                new Uri(AppSettings.ElasticsearchHost)
            ).DefaultIndex($"{AppSettings.ElasticsearchPrefix}_index_meta")
        );


        [Text(Name = "index")]
        public string Index { get; set; }

        [Text(Name = "alias")]
        public string Alias { get; set; }

        [Number(NumberType.Long, Name = "last_id")]
        public long LastId { get; set; }

        [Date(Name = "updated_at")]
        public DateTimeOffset UpdatedAt { get; set; }

        public static void Update(IndexMeta indexMeta)
        {
            client.IndexDocumentAsync(indexMeta);
        }

        public static IndexMeta GetByName(string name)
        {
            var response = client.Search<IndexMeta>(s => s
                .Query(q => q
                    .Ids(d => d.Values(name))
                )
            );

            return response.Documents.FirstOrDefault();
        }

        public static IEnumerable<IndexMeta> GetByAlias(string name)
        {
            var response = client.Search<IndexMeta>(s => s
                .Query(q => q
                    .Term(d => d.Alias, name)
                )
            );

            return response.Documents;
        }
    }
}
