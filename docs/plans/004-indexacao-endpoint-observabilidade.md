# Plano 004: fluxo de indexação, `POST /documents/index` e observabilidade

**Status**: implementado e aprovado.

**Atualização**: em desenvolvimento, os segredos passaram a vir de `appsettings.Development.json` local e não versionado, no lugar de user-secrets (ver [definição técnica](../definicao-tecnica.md)).

## Objetivo

Conectar `MarkdownDocumentReader` → `MarkdownChunker` → `ChunkEmbedder` → `ChunkVectorStore`, expor o fluxo por `POST /documents/index` e introduzir a fundação de observabilidade do Fonte (logs, traces e métricas com OpenTelemetry, exportação OTLP opcional).

## Decisões

| Tema | Decisão |
|---|---|
| Orquestração | `DocumentIndexer` (singleton) em `Fonte.Api/Indexing/`, com `Task<DocumentIndexingOutcome> IndexAsync(CancellationToken)`. `DocumentIndexingOutcome` tem `Status` (`Published`, `AlreadyRunning`, `NoDocuments`) e, quando publicado, `Documents`, `Chunks` e `CleanupCompleted`. |
| Dependências externas | `ChunkEmbedder` e `ChunkVectorStore` são resolvidos dentro de `IndexAsync`, depois de obter o lock e antes de ler documentos. Nada é resolvido na inicialização; a validação de chave e URL continua só nas factories já existentes; uma falha de configuração pode ser tentada de novo na requisição seguinte. |
| `CancellationToken` | `HttpContext.RequestAborted`, repassado a todas as etapas de I/O. |
| Pasta vazia | `NoDocuments`: 422, índice ativo não alterado. |
| Falhas de Gemini e Qdrant | A exceção propaga; o alias não muda; log de erro com a etapa; resposta 500 padrão do ASP.NET. Sem retry. |
| `VectorStoreCleanupException` | Tratada como `Published` com `CleanupCompleted = false`, log de aviso com a collection ativa e a que não foi apagada. |
| Concorrência | `SemaphoreSlim(1, 1)` no `DocumentIndexer` com `WaitAsync(0)`: uma segunda requisição simultânea recebe 409 imediatamente. Vale para uma única instância da aplicação. |
| Endpoint | `POST /documents/index`, sem corpo. 200 com `{ "documents", "chunks", "cleanupCompleted" }`; 409 e 422 com `ProblemDetails` nativo; demais falhas, 500 padrão. Duração não faz parte do contrato HTTP. |
| Traces | `ActivitySource` `Fonte.Api`. Spans próprios, irmãos sob o span HTTP: `documents.read` (tag `fonte.documents.count`), `documents.chunk` (tag `fonte.chunks.count`), `embedding.create` e `vectorstore.replace` (tag `fonte.cleanup.completed`). Em falha: status `Error` e `error.type` (tipo da exceção), sem `AddException`. Spans HTTP automáticos do ASP.NET Core e de `System.Net.Http`; `/health` filtrado. |
| Métricas | `Meter` `Fonte.Api` via `IMeterFactory`: `fonte.indexing.duration` (histograma, s, tags `fonte.indexing.outcome` = `published`, `published_cleanup_failed`, `failed`, `already_running`, `no_documents`, e `error.type` em falhas), `fonte.indexing.documents` e `fonte.indexing.chunks` (contadores). Métricas automáticas do ASP.NET Core e `System.Net.Http`. |
| Logs | `ILogger` estruturado com `LoggerMessage`: início, conclusão (documentos, chunks, duração), rejeições (em andamento, pasta vazia), limpeza falhou (aviso) e falha (erro, com `Stage` = `configuration`, `read`, `chunk`, `embedding` ou `vectorstore`). |
| Privacidade | Nunca na telemetria: conteúdo de documentos e chunks, embeddings e chaves. `DocumentPath` fora de traces e métricas; pode aparecer em logs de erro (mensagens de exceção) para identificar o documento que falhou. |
| OpenTelemetry | `OpenTelemetry.Extensions.Hosting` 1.19.1, `OpenTelemetry.Instrumentation.AspNetCore` 1.19.0 e `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.19.1. Sem `OpenTelemetry.Instrumentation.Http` (instrumentação nativa no .NET 9+ via `AddSource("System.Net.Http")`). Serviço `fonte`. |
| Exportação OTLP | `UseOtlpExporter()` somente quando `OTEL_EXPORTER_OTLP_ENDPOINT` estiver configurado. Grafana Cloud: `OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf` e `OTEL_EXPORTER_OTLP_HEADERS` gerados no portal, via user-secrets ou variáveis de ambiente. Nada disso no repositório. |
| Idioma | Documentação, plano, mensagens de log e de `ProblemDetails` em pt-BR; identificadores de código, spans, métricas e atributos em inglês. |

## Escopo

- Inclui: orquestração, endpoint, concorrência em uma instância, traces, métricas, logs estruturados e exportação OTLP desligada por padrão.
- Não inclui: `POST /questions`, embedding da pergunta, busca vetorial, geração de resposta, autenticação, retry, infraestrutura distribuída, conexão efetiva com o Grafana e dashboards.

## Arquivos

- `Fonte.Api/Fonte.Api.csproj`: pacotes OpenTelemetry.
- `Fonte.Api/Indexing/DocumentIndexer.cs` (novo).
- `Fonte.Api/Indexing/DocumentIndexingOutcome.cs` (novo).
- `Fonte.Api/Indexing/DocumentIndexingEndpoint.cs` (novo).
- `Fonte.Api/Observability/FonteTelemetry.cs` (novo): `ActivitySource` e nomes.
- `Fonte.Api/Observability/IndexingMetrics.cs` (novo).
- `Fonte.Api/Observability/OpenTelemetryRegistration.cs` (novo).
- `Fonte.Api/Program.cs`: registros e mapeamento da rota.
- `Fonte.Tests/Fakes/` (novo): `FakeEmbeddingGenerator`, `FakeQdrantGateway` e `StubHostEnvironment` movidos dos testes existentes, e `CapturingLoggerProvider`.
- `Fonte.Tests/Embeddings/ChunkEmbedderTests.cs`, `Fonte.Tests/VectorStore/ChunkVectorStoreTests.cs` e `Fonte.Tests/Indexing/MarkdownDocumentReaderTests.cs`: usar os fakes compartilhados.
- `Fonte.Tests/Indexing/DocumentIndexerTests.cs`, `DocumentIndexingEndpointTests.cs` e `IndexingTelemetryTests.cs` (novos).
- `docs/definicao-tecnica.md`: API, concorrência, observabilidade e configuração OTLP.
- `docs/plans/004-indexacao-endpoint-observabilidade.md` (novo): este plano.

## Passos

1. Gravar este plano.
2. Adicionar os pacotes e verificar vulnerabilidades.
3. Mover os fakes para `Fonte.Tests/Fakes/`.
4. Testes (vistos falhando).
5. Telemetria, orquestrador e endpoint.
6. Registros e configuração.
7. Documentação e resultado no plano.
8. Build e testes, `scope-check` e `dotnet-code-review`.

## Validação

Sem Gemini, Qdrant ou Grafana reais.

- `DocumentIndexerTests` (componentes reais, fakes só nas bordas): publicação com contagens; pasta vazia sem tocar no gateway; falha de embedding e de Qdrant propagadas, sem troca de alias; falha de limpeza como `Published` com `CleanupCompleted = false`; chamada simultânea recebe `AlreadyRunning`; lock liberado após sucesso e falha; falha de configuração sem nenhuma chamada externa nem leitura; `CancellationToken` repassado.
- `DocumentIndexingEndpointTests` (`WebApplicationFactory`): 200 com corpo; 200 com `cleanupCompleted: false`; 409; 422; 500 com alias intacto; sem chave do Gemini ou sem URL do Qdrant, 500 com métrica `failed`, log com `Stage = configuration`, nenhuma chamada externa, e `/health` respondendo.
- `IndexingTelemetryTests`: os quatro spans próprios, na ordem, sob o mesmo pai (filtrados pelo `TraceId` do teste); span da etapa com falha marcado como erro; nenhum atributo de span contém conteúdo, `DocumentPath` ou chaves; nenhum log contém conteúdo de chunk; métricas com o `outcome` e as contagens corretas (filtradas pelo `IMeterFactory` do teste). Sem verificar valores de duração nem atributos das instrumentações automáticas.
- Comando: `dotnet test Fonte.sln`.

## Riscos

- Requisição síncrona longa (uma chamada ao Gemini por chunk); desconexão do cliente antes da troca do alias deixa collection incompleta, limpa na próxima reindexação.
- Um 429 do Gemini derruba a indexação inteira (sem retry).
- Lock só vale para uma instância.
- Não verificado: se as chamadas gRPC do Qdrant aparecem como spans de `System.Net.Http`; se `UseOtlpExporter` lê `OTEL_*` de user-secrets; se `OTEL_SERVICE_NAME` sobrepõe o nome definido no código.

## Em aberto

- Conexão efetiva com o Grafana Cloud (depende das credenciais da conta).
- Retry para 429 e 5xx.
- Fluxo de perguntas e busca vetorial.

## Resultado da implementação

Implementado conforme o plano, sem Gemini, Qdrant ou Grafana reais.

- `dotnet list package --vulnerable --include-transitive`: nenhum pacote vulnerável em `Fonte.Api` e `Fonte.Tests`.
- `dotnet build`: sem avisos. `dotnet test Fonte.sln`: 77 testes passando (24 novos).
- Os testes foram conferidos com defeitos introduzidos temporariamente, cada um detectado por ao menos um teste: dependências resolvidas depois da leitura, ausência de exclusão mútua, `DocumentPath` como atributo de span e falha de limpeza tratada como falha de publicação. O código foi restaurado em seguida.
- Verificação manual: com a API rodando sem credenciais, `GET /health` respondeu 200 e `POST /documents/index` respondeu 500 com o log "Falha na indexação na etapa configuration" e a mensagem citando `Gemini:ApiKey`.

### Desvios e detalhes não previstos no plano

- **`Func<T>` em vez de `Lazy<T>`**: o `DocumentIndexer` recebe `Func<ChunkEmbedder>` e `Func<ChunkVectorStore>`, que apenas delegam ao contêiner. Atende aos quatro requisitos sem depender da semântica de cache do `Lazy<T>`: nada é resolvido na inicialização; o contêiner não guarda falhas de factory, então uma nova tentativa resolve de novo (coberto por teste); depois do sucesso, os singletons ficam em cache no próprio contêiner; a resolução acontece dentro da operação observada, antes da leitura.
- **`DocumentIndexerHarness.cs`** (novo, em `Fonte.Tests/Indexing/`): monta o `DocumentIndexer` para `DocumentIndexerTests` e `IndexingTelemetryTests`, evitando duplicação.
- **`FakeEmbeddingGenerator`** ganhou `Failure`, `Gate` e `Entered`, usados pelos testes de falha e de concorrência. `using`s que ficaram sem uso nos testes existentes foram removidos.
- **Timeouts nos testes de concorrência**: a segunda chamada é aguardada com limite de 10 s, para que uma regressão falhe rápido em vez de travar a execução.
- **Cancelamento pelo cliente** é registrado como falha (`outcome = failed`, log de erro com o tipo `TaskCanceledException`/`OperationCanceledException`).
- **Log duplicado em falhas**: além do log do `DocumentIndexer`, o Kestrel registra a exceção não tratada que gera o 500 (comportamento padrão do ASP.NET).
