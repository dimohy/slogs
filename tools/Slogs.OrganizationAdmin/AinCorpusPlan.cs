using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Slogs.Data;
using Npgsql;
using System.Text.Json.Nodes;

internal static class AinCorpusPlan
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    const string OrganizationId = "caff6131-5eec-4eb4-a34f-f325acf5d743";
    const string CollectionId = "ain-hospital-public-website";
    public static async Task BuildAsync(string inputDirectory, string outputPath)
    {
        using var crawl = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(inputDirectory, "crawl.json")));
        using var schedules = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(inputDirectory, "schedules.json")));
        if (!crawl.RootElement.GetProperty("exhausted").GetBoolean() || schedules.RootElement.GetProperty("failures").GetArrayLength() != 0)
            throw new InvalidDataException("Finish the admitted crawl and schedule collection before ingestion.");
        var version = "2026-09-08-v1";
        var docs = new List<KnowledgeDocumentInput>(); var nodes = new List<KnowledgeStructureInput>();
        var chunks = new List<KnowledgeChunkInput>(); var relations = new List<KnowledgeRelationInput>();
        var chunker = new KnowledgeChunkingService();
        var pageByUrl = new Dictionary<string, string>();
        void Add(string id, string title, string url, string captured, string hash, IEnumerable<string> texts, string kind, string[] aliases)
        {
            var metadata = new Dictionary<string,string> { ["sourceUrl"]=url,["capturedAt"]=captured,["sourceHash"]=hash,["hospitalApproval"]="pending",["kind"]=kind };
            docs.Add(new(id,title,"webpage",docs.Count,url,metadata));
            nodes.Add(new("section:"+id,id,null,"section",title,0,url,metadata));
            var units = new List<KnowledgeTextUnit>();
            foreach(var text in texts.Where(t=>!string.IsNullOrWhiteSpace(t)))
            {
                // Retain normal paragraphs/rows. Only oversize units are split at whitespace boundaries.
                var segments = new List<string>(); var segment = "";
                foreach(var word in Regex.Split(text.Trim(), @"\s+"))
                {
                    var candidate = segment.Length == 0 ? word : segment+" "+word;
                    if(KnowledgeChunkingService.CountTokens(candidate)>180 && segment.Length>0){segments.Add(segment);segment=word;}else segment=candidate;
                }
                if(segment.Length>0)segments.Add(segment);
                foreach(var part in segments)units.Add(new($"{id}:unit-{units.Count+1}",$"{url}#unit-{units.Count+1}",part,false,metadata));
            }
            var built = chunker.CreateChunks(CollectionId,version,id,"section:"+id,units,new(420,560,120,1));
            if(built.Any(c=>c.TokenCount>560))throw new InvalidDataException("Chunk token limit exceeded.");
            chunks.AddRange(built.Select(c=>c with {SearchAliases=aliases}));pageByUrl[url]=id;
        }
        foreach(var page in crawl.RootElement.GetProperty("pages").EnumerateArray())
            Add(page.GetProperty("id").GetString()!,page.GetProperty("title").GetString()!,page.GetProperty("url").GetString()!,page.GetProperty("capturedAt").GetString()!,page.GetProperty("contentHash").GetString()!,page.GetProperty("units").EnumerateArray().Select(u=>u.GetProperty("text").GetString()!),"public-page",[]);
        foreach(var doctor in schedules.RootElement.GetProperty("doctors").EnumerateArray())
        {
            var id=doctor.GetProperty("id").GetString()!;var name=doctor.GetProperty("name").GetString()!;var department=doctor.GetProperty("department").GetString()!;
            Add(id,$"{department} {name} 공개 진료시간표",doctor.GetProperty("detailUrl").GetString()!,doctor.GetProperty("capturedAt").GetString()!,doctor.GetProperty("contentHash").GetString()!,doctor.GetProperty("text").GetString()!.Split('\n'),"physician-schedule",[name,department,"진료시간표","오전","오후","휴진","토요일"]);
        }
        // Source-explicit graph: department -> published physician schedule -> linked profile.
        // No clinical causes or physician rankings are inferred from shared keywords.
        foreach(var doctor in schedules.RootElement.GetProperty("doctors").EnumerateArray())
        {
            var id=doctor.GetProperty("id").GetString()!;var source=chunks.First(c=>c.DocumentId==id);
            var deptNo=doctor.GetProperty("deptNo").GetString()!;
            var deptPage=docs.FirstOrDefault(d=>d.SourceLocator.Contains($"deptNo={deptNo}&deptMenuCode=001000000")||d.SourceLocator.Contains($"deptMenuCode=001000000&deptNo={deptNo}"));
            if(deptPage is null)continue;
            var from=chunks.First(c=>c.DocumentId==deptPage.DocumentId);
            relations.Add(new("relation:"+id,from.ChunkId,"has_published_physician_schedule",source.ChunkId,"source_explicit","approved",1,[new(id,source.StartLocator,"text_explicit",[source.ChunkId])],"ain-public-source-review"));
            var profile=docs.FirstOrDefault(d=>d.Metadata?["kind"]=="public-page"&&d.SourceLocator==doctor.GetProperty("detailUrl").GetString());
            if(profile is not null){var profileChunk=chunks.First(c=>c.DocumentId==profile.DocumentId);relations.Add(new("relation:"+id+":profile",source.ChunkId,"has_official_profile",profileChunk.ChunkId,"source_explicit","approved",1,[new(id,profile.SourceLocator,"source_explicit",[source.ChunkId,profileChunk.ChunkId])],"ain-public-source-review"));}
            // Consecutive chunks preserve the document's explicit continuation, not an inferred clinical relationship.
            var ordered=chunks.Where(c=>c.DocumentId==id).OrderBy(c=>c.Ordinal).ToArray();
            for(var i=1;i<ordered.Length;i++)relations.Add(new($"relation:{id}:continuation:{i}",ordered[i-1].ChunkId,"continues_in",ordered[i].ChunkId,"source_explicit","approved",1,[new(id,ordered[i].StartLocator,"source_explicit",[ordered[i-1].ChunkId,ordered[i].ChunkId])],"ain-public-source-review"));
        }
        var collection=new KnowledgeCollectionInput(CollectionId,version,"아인병원 공식 공개 안내 및 의료진 시간표","hospital-public-information","ko","Hospital copyright; internal proposal demo reference only; hospital approval pending","https://www.ainwh.co.kr/","organization",OrganizationId,"organization",OrganizationId,false,chunks.Count);
        var plan=new KnowledgeCorpusIngestRequest(collection,docs,nodes,chunks,[],relations,[new("organization",OrganizationId,"reader")]);
        await File.WriteAllTextAsync(outputPath,JsonSerializer.Serialize(plan,Json));
        Console.WriteLine(JsonSerializer.Serialize(new {documents=docs.Count,chunks=chunks.Count,relations=relations.Count,maxTokens=chunks.Max(c=>c.TokenCount),outputPath}));
    }
    public static async Task IngestAsync(string path,string actorName,string mode = "ingest-corpus")
    {
        var plan=JsonSerializer.Deserialize<KnowledgeCorpusIngestRequest>(await File.ReadAllTextAsync(path),Json)??throw new InvalidDataException("Missing plan.");
        if(plan.Collection.OwnerKind!="organization"||plan.Collection.OwnerKey!=OrganizationId||plan.Collection.Visibility!="organization"||plan.Collection.CollectionId!=CollectionId||plan.Collection.ExpectedChunkCount!=plan.Chunks.Count)throw new InvalidDataException("Ain corpus target differs.");
        var configuration=new ConfigurationBuilder().SetBasePath("/app").AddJsonFile("appsettings.json",optional:false).AddJsonFile("appsettings.Production.json",optional:true).AddEnvironmentVariables().Build();
        var connection=configuration.GetConnectionString("SlogsDatabase")??throw new InvalidOperationException("Database configuration required.");
        var services=new ServiceCollection();services.AddSingleton<IConfiguration>(configuration);services.AddLogging();
        services.AddDbContextFactory<SlogsDbContext>(o=>o.UseNpgsql(connection));services.AddDbContextFactory<OrganizationDbContext>(o=>o.UseNpgsql(connection).UseOpenIddict());
        services.AddScoped<KnowledgeCorpusPrincipalResolver>();services.AddScoped<KnowledgeCorpusService>();
        services.AddHttpClient<BgeM3EmbeddingService>(c=>BgeM3EmbeddingService.ConfigureHttpClient(c,configuration));services.AddScoped<IKnowledgeEmbeddingService>(s=>s.GetRequiredService<BgeM3EmbeddingService>());
        await using var provider=services.BuildServiceProvider();await using var scope=provider.CreateAsyncScope();
        await using var db=await scope.ServiceProvider.GetRequiredService<IDbContextFactory<SlogsDbContext>>().CreateDbContextAsync();
        if(!await db.Users.AnyAsync(u=>u.UserName==actorName))throw new InvalidOperationException("Actor missing.");
        var actor=await scope.ServiceProvider.GetRequiredService<KnowledgeCorpusPrincipalResolver>().ResolveAsync(new AuthUser{UserName=actorName});
        if(!actor.OrganizationRoles.TryGetValue(OrganizationId,out var role)||role!="owner")throw new UnauthorizedAccessException("Existing Ain organization owner required.");
        var service=scope.ServiceProvider.GetRequiredService<KnowledgeCorpusService>();
        var embedding=scope.ServiceProvider.GetRequiredService<IKnowledgeEmbeddingService>();
        var verified=await VerifyExistingChunksAsync(db, plan, actorName, embedding);
        Console.WriteLine(JsonSerializer.Serialize(new {mode, verifiedChunks=verified.Count,total=plan.Chunks.Count,remaining=plan.Chunks.Count-verified.Count}));
        if(mode is "status-corpus" or "verify-corpus")
        {
            if(mode=="verify-corpus" && verified.Count!=plan.Chunks.Count)throw new InvalidDataException("Corpus content verification incomplete.");
            var readActor=new OrganizationActorContext(Guid.Parse(OrganizationId),actorName,OrganizationActorKinds.User,"owner",new HashSet<string>{OrganizationTokenScopes.Read},null,null,null);
            if(mode=="verify-corpus")
            {
                var status=await service.ReadOrganizationStatusAsync(readActor,CollectionId);
                if(status.Version!=plan.Collection.Version||status.DocumentCount!=plan.Documents.Count||status.RelationCount!=plan.Relations.Count)throw new InvalidDataException("Active corpus identity/count differs from the plan.");
                Console.WriteLine(JsonSerializer.Serialize(status,Json));
            }
            return;
        }
        // Idempotent batches, staging until integrity-checked final activation. No reset/delete.
        foreach(var group in plan.Documents.Chunk(100))await service.IngestAsync(actor,plan with{Documents=group,StructureNodes=[],Chunks=[],Entities=[],Relations=[],RefreshContentHash=false});
        foreach(var group in plan.StructureNodes.Chunk(500))await service.IngestAsync(actor,plan with{Documents=[],StructureNodes=group,Chunks=[],Entities=[],Relations=[],RefreshContentHash=false});
        var done=verified.Count;foreach(var group in plan.Chunks.Where(chunk=>!verified.Contains(chunk.ChunkId)).Chunk(20)){await service.IngestAsync(actor,plan with{Documents=[],StructureNodes=[],Chunks=group,Entities=[],Relations=[],RefreshContentHash=false});done+=group.Length;Console.WriteLine(JsonSerializer.Serialize(new {chunks=done,total=plan.Chunks.Count}));}
        foreach(var group in plan.Relations.Chunk(500))await service.IngestAsync(actor,plan with{Documents=[],StructureNodes=[],Chunks=[],Entities=[],Relations=group,RefreshContentHash=false});
        var result=await service.IngestAsync(actor,plan with{Documents=[],StructureNodes=[],Chunks=[],Entities=[],Relations=[],Activate=true});
        Console.WriteLine(JsonSerializer.Serialize(result,Json));
    }

    private static async Task<HashSet<string>> VerifyExistingChunksAsync(SlogsDbContext db,
        KnowledgeCorpusIngestRequest plan, string storageOwner, IKnowledgeEmbeddingService embedding)
    {
        await db.Database.OpenConnectionAsync();
        await using var command=new NpgsqlCommand(
            """
            SELECT k."ChunkId", k."DocumentId", k."StructureNodeId", k."Ordinal", k."Text", k."StartLocator", k."EndLocator",
                k."PreviousChunkId", k."NextChunkId", k."OverlapUnits", k."TokenCount", k."TokenizerId",
                k."SearchAliasesJson"::text, k."MetadataJson"::text, k."ContentHash", k."EmbeddingModel", k."EmbeddingDimensions"
            FROM "LlmWikiKnowledgeChunks" k
            INNER JOIN "LlmWikiKnowledgeCollections" c
              ON c."CollectionId"=k."CollectionId" AND c."Version"=k."Version" AND c."OwnerUserName"=k."OwnerUserName"
            WHERE c."CollectionId"=@collection AND c."Version"=@version AND c."OwnerUserName"=@owner
              AND c."OwnerKind"='organization' AND c."OwnerKey"=@organization;
            """, (NpgsqlConnection)db.Database.GetDbConnection());
        command.Parameters.AddWithValue("collection",plan.Collection.CollectionId);
        command.Parameters.AddWithValue("version",plan.Collection.Version);
        command.Parameters.AddWithValue("owner",storageOwner);
        command.Parameters.AddWithValue("organization",OrganizationId);
        var expected=plan.Chunks.Select(KnowledgeCorpusService.NormalizeChunkInput).ToDictionary(chunk=>chunk.ChunkId,StringComparer.Ordinal);
        var verified=new HashSet<string>(StringComparer.Ordinal);
        await using var reader=await command.ExecuteReaderAsync();
        while(await reader.ReadAsync())
        {
            var id=reader.GetString(0);
            if(!expected.TryGetValue(id,out var chunk))throw new InvalidDataException($"Unexpected existing chunk: {id}");
            string? Nullable(int index)=>reader.IsDBNull(index)?null:reader.GetString(index);
            if(reader.GetString(1)!=chunk.DocumentId||Nullable(2)!=chunk.StructureNodeId||reader.GetInt32(3)!=chunk.Ordinal
                ||reader.GetString(4)!=chunk.Text.Trim()||reader.GetString(5)!=chunk.StartLocator||reader.GetString(6)!=chunk.EndLocator
                ||Nullable(7)!=chunk.PreviousChunkId||Nullable(8)!=chunk.NextChunkId||reader.GetInt32(9)!=chunk.OverlapUnits
                ||reader.GetInt32(10)!=chunk.TokenCount||reader.GetString(11)!=chunk.TokenizerId
                ||!JsonNode.DeepEquals(JsonNode.Parse(reader.GetString(12)),JsonSerializer.SerializeToNode(chunk.SearchAliases??[]))
                ||!JsonNode.DeepEquals(JsonNode.Parse(reader.GetString(13)),JsonSerializer.SerializeToNode(chunk.Metadata??new Dictionary<string,string>()))
                ||!reader.GetString(14).Equals(KnowledgeCorpusService.ComputeChunkContentHash(plan.Collection,chunk),StringComparison.OrdinalIgnoreCase)
                ||reader.GetString(15)!=embedding.Model||reader.GetInt32(16)!=embedding.Dimensions)
                throw new InvalidDataException($"Existing chunk content or embedding identity differs; refusing unsafe resume: {id}");
            verified.Add(id);
        }
        return verified;
    }
}
