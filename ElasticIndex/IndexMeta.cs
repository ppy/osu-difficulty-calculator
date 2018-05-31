using Nest;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ElasticIndex
{
    [ElasticsearchType(Name = "index_meta", IdProperty = nameof(Index))]
    public class IndexMeta
    {
        static readonly ElasticClient Client = new ElasticClient(
          new ConnectionSettings(
            new Uri(Program.Configuration["elasticsearch:host"])
          ).DefaultIndex($"{Program.Configuration["elasticsearch:prefix"]}_index_meta")
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
            IndexMeta.Client.IndexDocumentAsync(indexMeta);
        }

        public static IndexMeta GetByName(string name)
        {
            var response = Client.Search<IndexMeta>(s => s
                .Query(q => q
                    .Ids(d => d.Values(name))
                )
            );

            return response.Documents.FirstOrDefault();
        }

        public static IEnumerable<IndexMeta> GetByAlias(string name)
        {
            var response = Client.Search<IndexMeta>(s => s
                .Query(q => q
                    .Term(d => d.Alias, name)
                )
            );

            return response.Documents;
        }
    }
}
