# Fonte

Aplicação .NET que usa RAG (_Retrieval-Augmented Generation_) para responder perguntas com base em uma coleção de documentos Markdown, retornando a resposta junto com as fontes usadas. O projeto também explora a observabilidade de aplicações com IA por meio de traces, métricas e logs.

A stack é ASP.NET Core Minimal API, Gemini API (embeddings e geração), Qdrant Cloud (busca vetorial) e OpenTelemetry, com exportação OTLP para o Grafana Cloud.

O projeto está em desenvolvimento. O fluxo completo de indexação e de pergunta está implementado e a telemetria já foi validada no Grafana Cloud; a execução de build e testes no GitHub Actions ainda está prevista.

A definição completa (fluxos, API da v1, observabilidade, configuração, definição de pronto e fora do escopo) está em [docs/definicao-tecnica.md](docs/definicao-tecnica.md). As decisões de cada etapa estão em [docs/plans/](docs/plans/).

## Pipeline RAG

```text
Indexação: Markdown → leitura → chunking → embedding (Gemini) → Qdrant

Pergunta:  pergunta → embedding (Gemini) → busca no Qdrant → Top N chunks → Gemini + contexto → resposta + fontes
```

- **Leitura e chunking**: arquivos `*.md` da pasta configurada (incluindo subdiretórios), divididos por parágrafos em chunks de até `Documents:MaxChunkSize` caracteres, sem sobreposição.
- **Embeddings**: `gemini-embedding-2`, 768 dimensões por padrão. Trocar modelo ou dimensões exige reindexar.
- **Armazenamento**: cada indexação cria uma nova collection no Qdrant e, ao final, aponta o alias `Qdrant:CollectionName` para ela (reindexação blue/green); só então as collections antigas são removidas. Se a indexação falhar, o índice ativo não muda.
- **Recuperação**: busca por similaridade (Cosine) no alias configurado, retornando até `Retrieval:TopK` chunks (padrão 3).
- **Geração**: `gemini-3.6-flash`, instruído a responder em pt-BR apenas com base nos trechos recuperados. Sem contexto suficiente, a API informa isso em vez de responder com conhecimento geral do modelo.

## Pré-requisitos

- .NET SDK 10
- Chave da Gemini API
- Cluster no Qdrant Cloud (URL gRPC e chave de API)
- Opcional: stack no Grafana Cloud, para receber a telemetria

## Configuração local

Copie `Fonte.Api/appsettings.Development.example.json` para `Fonte.Api/appsettings.Development.json` e preencha os valores. Esse arquivo é ignorado pelo Git e não vai para a saída do `dotnet publish`. Nos demais ambientes, use variáveis de ambiente (por exemplo, `Gemini__ApiKey`, `Qdrant__Url` e `Qdrant__ApiKey`).

| Chave | Uso |
|---|---|
| `Gemini:ApiKey` | Chave da Gemini API. |
| `Qdrant:Url` | Endereço do cluster, na porta gRPC `6334`. |
| `Qdrant:ApiKey` | Chave do Qdrant. |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Opcional. Endpoint OTLP do Grafana Cloud (`https://otlp-gateway-<região>.grafana.net/otlp`). Sem ele, nenhuma telemetria é exportada. |
| `OTEL_EXPORTER_OTLP_PROTOCOL` | `http/protobuf` (já preenchido no exemplo). |
| `OTEL_EXPORTER_OTLP_HEADERS` | Cabeçalho de autenticação gerado no portal do Grafana Cloud, no formato `Authorization=Basic <credencial>`. |

As chaves `OTEL_EXPORTER_OTLP_*` ficam na raiz do arquivo, fora de qualquer seção. A API e o `GET /health` sobem sem credenciais; a falta de uma chave só aparece como erro ao indexar ou perguntar.

Os demais parâmetros têm padrão em [appsettings.json](Fonte.Api/appsettings.json):

| Chave | Padrão |
|---|---|
| `Documents:Path` | `documents` (relativo à pasta da API) |
| `Documents:MaxChunkSize` | `1000` |
| `Gemini:EmbeddingModel` | `gemini-embedding-2` |
| `Gemini:EmbeddingDimensions` | `768` |
| `Gemini:GenerationModel` | `gemini-3.6-flash` |
| `Qdrant:CollectionName` | `fonte-chunks` |
| `Retrieval:TopK` | `3` |

Detalhes e regras de validação em [Configuração e segredos](docs/definicao-tecnica.md#configuração-e-segredos).

## Como executar

```bash
dotnet run --project Fonte.Api
```

A API sobe em `http://localhost:5023` (perfil `http` em [launchSettings.json](Fonte.Api/Properties/launchSettings.json)), no ambiente `Development`.

Para indexar os documentos de `Fonte.Api/documents/`:

```bash
curl -X POST http://localhost:5023/documents/index
```

```json
{ "documents": 3, "chunks": 15, "cleanupCompleted": true }
```

Para fazer uma pergunta (é preciso indexar antes):

```bash
curl -X POST http://localhost:5023/questions -H "Content-Type: application/json" -d '{"question": "Qual é a função do ActivitySource?"}'
```

```json
{
  "status": "answered",
  "answer": "O ActivitySource é o ponto de partida para criar traces próprios...",
  "sources": [
    { "number": 1, "document": "observabilidade.md", "chunk": 3, "score": 0.7694 }
  ]
}
```

## Endpoints

| Método e rota | Função | Respostas |
|---|---|---|
| `GET /health` | Health check | `200` sem corpo |
| `POST /documents/index` | Indexa ou reindexa os documentos da pasta configurada. Sem corpo na requisição. | `200` com `documents`, `chunks` e `cleanupCompleted`; `409` se já houver uma indexação em andamento na instância; `422` se não houver documentos Markdown; `500` em falha de configuração, leitura, Gemini ou Qdrant |
| `POST /questions` | Recebe `{ "question": "..." }` e retorna resposta e fontes | `200` com `status`, `answer` e `sources`; `400` se `question` estiver ausente, vazia ou tiver mais de 2000 caracteres, ou se o JSON for inválido; `500` em falha de configuração, Gemini, Qdrant (inclusive índice ainda não criado) ou resposta inválida do modelo |

Em `POST /questions`, `status` pode ser:

- `answered`: resposta do modelo; `sources` lista os trechos enviados como contexto, na ordem, com `number`, `document`, `chunk` e `score`. O conteúdo dos trechos não é retornado.
- `insufficient_context`: o modelo indicou que os trechos não bastam; `answer` traz uma mensagem fixa e `sources` vem vazio.
- `no_context`: a busca não retornou trechos (o modelo não é chamado); mesma mensagem fixa e `sources` vazio.

Toda resposta de erro é `application/problem+json` (`ProblemDetails`) com títulos em pt-BR e um campo `traceId`, o mesmo identificador do trace exportado para o Grafana e presente nos logs. O `500` nunca expõe mensagem, tipo ou stack trace da exceção. Contrato completo em [Contrato de erros](docs/definicao-tecnica.md#contrato-de-erros).

## Observabilidade

A aplicação coleta traces, métricas e logs com OpenTelemetry e os exporta por OTLP quando `OTEL_EXPORTER_OTLP_ENDPOINT` está configurado. O serviço aparece como `fonte`. No Grafana Cloud, os traces ficam no Tempo, as métricas no Mimir/Prometheus e os logs no Loki, correlacionados por `trace_id`.

Trace de uma pergunta:

```text
POST /questions
├── embedding.create
│   └── HTTP → Gemini
├── retrieval.search                     fonte.retrieval.top_k, fonte.retrieval.results
│   └── Grpc.Net.Client.GrpcOut          grpc.method, grpc.status_code
│       └── HTTP → Qdrant
└── answer.generate                      gen_ai.request.model, gen_ai.usage.*, fonte.answer.status
    └── HTTP → Gemini
```

A indexação gera os spans `documents.read`, `documents.chunk`, `embedding.create` e `vectorstore.replace` sob `POST /documents/index`. `GET /health` não gera trace.

- **Métricas próprias**: `fonte.indexing.duration`, `fonte.indexing.documents`, `fonte.indexing.chunks`, `fonte.retrieval.duration` e `fonte.answer.duration`, com o resultado de cada operação como atributo. Também são coletadas as métricas automáticas do ASP.NET Core e do `HttpClient`. As métricas são exportadas a cada 60 s.
- **Logs**: estruturados com `ILogger`; falhas registram a etapa em que ocorreram (`Stage`).
- **Privacidade**: pergunta, resposta, prompt, conteúdo dos chunks, embeddings e chaves não entram na telemetria.

Detalhes em [Observabilidade](docs/definicao-tecnica.md#observabilidade) e em [plans/008-observabilidade-grafana.md](docs/plans/008-observabilidade-grafana.md).

## Como testar

```bash
dotnet test Fonte.sln
```

Os testes de integração sobem a aplicação no ambiente `Testing`, que não carrega o `appsettings.Development.json` e não exporta telemetria. Gemini e Qdrant são substituídos por fakes, então os testes não precisam de credenciais nem chamam serviços reais.

## Estrutura

- `Fonte.Api/`: API ASP.NET Core Minimal API.
  - `Indexing/`: leitura, chunking, orquestração da indexação e `POST /documents/index`.
  - `Embeddings/`: embeddings dos chunks e da pergunta com Gemini.
  - `VectorStore/`: gravação e busca no Qdrant.
  - `Retrieval/`: recuperação semântica dos chunks.
  - `Answering/`: geração da resposta e `POST /questions`.
  - `Observability/`: OpenTelemetry, traces e métricas.
  - `ErrorHandling/`: contrato de erros (`ProblemDetails`).
  - `documents/`: corpus Markdown de demonstração.
- `Fonte.Tests/`: testes automatizados (xUnit).
- `docs/`: documentação técnica.
- `docs/plans/`: planos de cada etapa.

## Licença

[MIT](LICENSE)
