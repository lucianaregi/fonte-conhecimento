# Plano 005: recuperação semântica

**Status**: implementado e aprovado.

## Objetivo

Dada uma pergunta, gerar seu embedding e buscar no Qdrant, pelo alias configurado, os N chunks mais similares, com documento, índice, conteúdo e score. Sem endpoint e sem geração de resposta.

## Decisões

| Tema | Decisão |
|---|---|
| Embedding da pergunta | `QueryEmbedder` (em `Embeddings/`), separado do `ChunkEmbedder`, usando o mesmo `IEmbeddingGenerator` (mesmo modelo e dimensões). Texto enviado: `task: search result \| query: {pergunta}`, formato documentado para consultas no `gemini-embedding-2`. Mesmas conferências de quantidade (1 vetor) e dimensão. Pergunta vazia ou só com espaços gera `ArgumentException` antes de qualquer chamada. |
| Resultado | `RetrievedChunk(string DocumentPath, int ChunkIndex, string Content, float Score)`, em `Retrieval/`. |
| Gateway | `IQdrantGateway.SearchAsync(string collection, ReadOnlyMemory<float> vector, int limit, CancellationToken)` retorna `IReadOnlyList<RetrievedChunk>`. O adapter usa `QueryAsync` (a Query API; `SearchAsync` do SDK está obsoleto) com `payloadSelector: true` e sem vetores; a conversão de payload e score é um método estático puro. Payload sem os campos esperados gera exceção. Nenhum tipo do SDK fora do adapter. |
| Alias | `ChunkVectorStore.SearchAsync(vector, limit)` busca pelo alias configurado; o alias continua conhecido só pelo `ChunkVectorStore`. |
| Orquestração | `ChunkRetriever.RetrieveAsync(string question, CancellationToken)`, em `Retrieval/`: embedding da pergunta e busca Top N. Recebe `Func<QueryEmbedder>` e `Func<ChunkVectorStore>`, como o `DocumentIndexer`, para que a falta de configuração seja uma falha registrada da operação. |
| Top N | `Retrieval:TopK`, padrão 3, validado na inicialização (≥ 1). |
| Índice existente sem resultados | Lista vazia, sem erro. Sem threshold de score: retorna o que o Qdrant devolver, até N, ordenado por score. |
| Alias ou índice inexistente | Falha: o erro do Qdrant propaga e é registrado na etapa `search`. Não é tratado como lista vazia. |
| Falhas do Gemini ou do Qdrant | Exceção original propaga. Sem retry. |
| Traces | `ActivitySource` `Fonte.Api`: `embedding.create` e `retrieval.search` (tags `fonte.retrieval.top_k` e `fonte.retrieval.results`). Em falha, status `Error` e `error.type`. |
| Métricas | `fonte.retrieval.duration` (histograma, s, `fonte.retrieval.outcome` = `success` ou `failed`, e `error.type` em falhas), em `RetrievalMetrics` no mesmo `Meter`. |
| Logs | Falha: Error com `Stage` (`configuration`, `embedding` ou `search`). Sucesso: Debug com quantidade de resultados e duração. |
| Privacidade | Pergunta, conteúdo dos chunks, vetores, `DocumentPath` e scores fora de traces e métricas; pergunta e conteúdo fora dos logs. |

## Escopo

- Inclui: embedding da pergunta, busca pelo alias com Top N, tipo de resultado, configuração, observabilidade e testes.
- Não inclui: `POST /questions`, geração de resposta, threshold de score, filtros por documento, retry e teste real contra Gemini/Qdrant.

## Arquivos

- `Fonte.Api/Embeddings/QueryEmbedder.cs` (novo).
- `Fonte.Api/Retrieval/RetrievalOptions.cs`, `RetrievedChunk.cs` e `ChunkRetriever.cs` (novos).
- `Fonte.Api/VectorStore/IQdrantGateway.cs`, `QdrantGateway.cs` e `ChunkVectorStore.cs`: busca.
- `Fonte.Api/Observability/FonteTelemetry.cs`: span `retrieval.search` e atributos.
- `Fonte.Api/Observability/RetrievalMetrics.cs` (novo).
- `Fonte.Api/Program.cs`: opções `Retrieval`, `QueryEmbedder`, `Func<QueryEmbedder>`, `RetrievalMetrics` e `ChunkRetriever`.
- `Fonte.Api/appsettings.json`: `Retrieval:TopK = 3`.
- `Fonte.Tests/Fakes/FakeQdrantGateway.cs`: `SearchAsync` com resultados configuráveis, falha opcional e registro da chamada.
- `Fonte.Tests/Embeddings/QueryEmbedderTests.cs` (novo).
- `Fonte.Tests/Retrieval/ChunkRetrieverTests.cs` (novo), incluindo telemetria.
- `Fonte.Tests/VectorStore/ChunkVectorStoreTests.cs` e `QdrantGatewayMappingTests.cs`: casos de busca e conversão.
- `docs/definicao-tecnica.md`: recuperação e `Retrieval:TopK`.
- `docs/plans/005-recuperacao-semantica.md` (novo): este plano.

## Passos

1. Gravar este plano.
2. Testes (vistos falhando).
3. `QueryEmbedder`, busca no gateway e no `ChunkVectorStore`, `ChunkRetriever`, telemetria.
4. Registros e configuração.
5. Documentação e resultado no plano.
6. Build e testes, `scope-check` e `dotnet-code-review`.

## Validação

Sem Gemini ou Qdrant reais.

- `QueryEmbedder`: texto `task: search result | query: {pergunta}`; uma chamada com um texto; quantidade ou dimensão erradas geram exceção; pergunta vazia gera exceção sem chamar o gerador; `CancellationToken` repassado.
- `ChunkVectorStore.SearchAsync`: busca pelo alias com o `limit` pedido e retorna o que o gateway devolver.
- Conversão no adapter: payload e score de um `ScoredPoint` viram `RetrievedChunk`; payload incompleto gera exceção.
- `ChunkRetriever`: pede o `TopK` configurado; mantém a ordem do gateway; índice sem resultados retorna lista vazia; alias inexistente (erro do gateway) propaga e é registrado na etapa `search`; falha de configuração registrada com `Stage = configuration`, sem chamadas a Gemini ou Qdrant; falha do Gemini registrada na etapa `embedding`.
- Telemetria: `embedding.create` e `retrieval.search` sob o mesmo pai, com as tags de contagem; span da etapa com falha marcado como erro; nenhum atributo de span nem log contém a pergunta, o conteúdo dos chunks ou o `DocumentPath`; métrica com o `outcome` correto.
- Comando: `dotnet test Fonte.sln`.

## Riscos

- Sem threshold, perguntas sem relação com o corpus ainda retornam N chunks.
- Não verificado: o erro exato do Qdrant para alias inexistente (a documentação não descreve); será observado no teste real.

## Em aberto

- Threshold de score e comportamento com contexto insuficiente (etapa de geração).
- Contrato de `POST /questions`.

## Resultado da implementação

Implementado conforme o plano, sem chamadas reais a Gemini ou Qdrant.

- `dotnet build`: sem avisos. `dotnet test Fonte.sln`: 104 testes passando (27 novos). Nenhuma dependência nova.
- Os testes foram conferidos com defeitos introduzidos temporariamente, cada um detectado por ao menos um teste: Qdrant resolvido só na etapa de busca (depois do Gemini), alias inexistente tratado como lista vazia, pergunta como atributo de span e `TopK` ignorado. O código foi restaurado em seguida.

### Desvios e detalhes não previstos no plano

- **Teste adicional**: falta de configuração do Qdrant é detectada antes de qualquer chamada ao Gemini (`MissingQdrantConfigurationFailsBeforeCallingGemini`).
- **Pergunta vazia também validada no `ChunkRetriever`**, antes de resolver serviços: é erro de quem chama, então não conta como falha da recuperação nas métricas nem nos logs.
- **`TraceAsync` duplicado**: o `ChunkRetriever` tem uma cópia do helper privado do `DocumentIndexer`. Extraí-lo para um ponto comum seria refatorar código já aprovado, fora do escopo desta etapa.
- **Mensagem de payload incompleto** cita o ID do ponto e o nome do campo, sem conteúdo.
