# Plano 008: observabilidade no Grafana Cloud e isolamento dos testes

**Status**: implementado e aprovado.

## Objetivo

Esta etapa fecha duas coisas:

1. **Exportação e validação real da observabilidade no Grafana Cloud**: traces (Tempo), métricas (Mimir/Prometheus) e logs (Loki) do Fonte, com a árvore completa de `POST /questions` (incluindo a chamada gRPC/HTTP ao Qdrant) e de `POST /documents/index`, a correlação de logs com `trace_id` e a confirmação, no backend real, da política de privacidade da telemetria.
2. **Isolamento entre o ambiente local de desenvolvimento e os testes de integração**, como decisão arquitetural do projeto: testes automatizados não consomem credenciais locais, não chamam Gemini nem Qdrant reais e não exportam telemetria para o backend real.

## Diagnóstico que motivou a etapa

- O `Qdrant.Client` 1.19.0 usa o `Grpc.Net.Client` 2.71.0, que tem um `ActivitySource` próprio (`Grpc.Net.Client`). Sem ouvinte para essa fonte, o cliente cria a `Activity` `Grpc.Net.Client.GrpcOut` sem fonte (legado); ela vira pai do span HTTP do Qdrant, mas não é exportada, deixando uma lacuna na árvore (observado no teste real da etapa 007).
- A configuração atual já exporta os três sinais por `UseOtlpExporter()` quando `OTEL_EXPORTER_OTLP_ENDPOINT` está no `IConfiguration`, inclusive logs (`WithLogging`).
- Os testes de integração rodam em `Development` e carregam o `appsettings.Development.json` local; com o endpoint OTLP preenchido nesse arquivo, cada `dotnet test` exportaria para o Grafana, e as credenciais reais ficariam ao alcance dos testes.

## Decisões

| Tema | Decisão |
|---|---|
| Span gRPC | `.AddSource("Grpc.Net.Client")` no tracing: usa a instrumentação nativa do `Grpc.Net.Client` 2.71.0 (span `Client` com `grpc.method` e `grpc.status_code`), sem dependência nova. Descartados: `AddLegacySource("Grpc.Net.Client.GrpcOut")` (mecanismo para bibliotecas sem `ActivitySource`; span `Internal`, fora do sampler) e `OpenTelemetry.Instrumentation.GrpcNetClient` (1.19.1-beta.1, pré-release, convenções `rpc.*` em desenvolvimento). |
| Configuração do Grafana | Chaves `OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_EXPORTER_OTLP_PROTOCOL` (`http/protobuf`) e `OTEL_EXPORTER_OTLP_HEADERS` na raiz do `appsettings.Development.json` local, com os valores gerados no portal do Grafana Cloud. O exportador lê essas chaves pelo `IConfiguration`, aceita `Authorization=Basic <base64>` com espaço ou `%20` e acrescenta `/v1/{sinal}` ao endpoint base. |
| Arquivo de exemplo | `appsettings.Development.example.json` com as três chaves OTLP: protocolo preenchido com `http/protobuf`, endpoint e headers vazios. |
| Nome do serviço | `fonte`, definido por `AddService` no código; `OTEL_SERVICE_NAME` **não** o sobrepõe (o recurso adicionado por último prevalece no `Resource.Merge`). `OTEL_RESOURCE_ATTRIBUTES` continua disponível para outros atributos. |
| Ambiente `Testing` | Factory compartilhada dos testes de integração (`FonteApiFactory`) sobe a aplicação no ambiente `Testing`, que não carrega `appsettings.Development.json`. Todas as classes de teste de integração passam a usá-la. |
| Exportação desligada em `Testing` | A exportação OTLP só é ligada fora do ambiente `Testing`, mesmo que `OTEL_EXPORTER_OTLP_ENDPOINT` venha de uma variável de ambiente da máquina. A regra fica numa função pura, testada. |
| Privacidade | Sem mudanças nas regras: pergunta, resposta, conteúdo dos chunks, prompt, embeddings e chaves fora da telemetria; `DocumentPath` fora de traces e métricas. A confirmação passa a ser feita também no Grafana. |

## Contrato de erro da aplicação (decisão tomada durante o isolamento dos testes)

**Origem**: ao colocar os testes de integração no ambiente `Testing`, os 7 testes que esperavam `500` passaram a falhar. O `500` documentado não era definido pela aplicação: em `Development` a Developer Exception Page capturava a exceção e respondia 500; em outros ambientes o `TestServer` propaga a exceção ao cliente e o Kestrel responderia 500 sem corpo. Em vez de emular esse comportamento só nos testes, o contrato de erro passa a ser responsabilidade explícita da aplicação, igual em `Development`, `Testing` e produção, e correlacionável com os traces no Grafana.

| Tema | Decisão |
|---|---|
| Registro | `AddProblemDetails()` com `CustomizeProblemDetails`; `UseExceptionHandler()` e `UseStatusCodePages()` como primeiros middlewares da aplicação. A Developer Exception Page (adicionada pelo ASP.NET só em `Development`, antes do pipeline) não chega a ver as exceções e não define o contrato. |
| `500` | Toda exceção não tratada vira `application/problem+json` com exatamente `type` (padrão RFC 9110), `title` "Erro interno do servidor", `status` 500, `detail` "Ocorreu um erro inesperado ao processar a requisição." e `traceId`. Nenhuma mensagem, tipo ou stack trace da exceção; nenhuma credencial, pergunta, resposta, conteúdo de chunk ou detalhe dos SDKs. A exceção continua registrada no log de erro do `ExceptionHandlerMiddleware` (risco residual já aceito para os logs). |
| `traceId` | Os 32 caracteres hexadecimais de `Activity.Current.TraceId` (o trace da requisição, o mesmo exportado para o Tempo e presente nos logs), em vez do `traceparent` W3C inteiro, padrão do ASP.NET; sem `Activity`, `HttpContext.TraceIdentifier`. Incluído em **todos** os `ProblemDetails`. |
| Respostas de erro sem corpo | `UseStatusCodePages()` escreve `ProblemDetails` com `traceId` para respostas de erro sem corpo: `400` nativo (corpo ausente ou JSON malformado), `404` (rota inexistente) e `405` (método não permitido). Faz parte do contrato, com teste para cada caso. |
| Títulos em pt-BR | `400`: "Requisição inválida" (validação e nativo); `404`: "Recurso não encontrado"; `405`: "Método não permitido"; `500`: "Erro interno do servidor". `409` e `422` mantêm os títulos e detalhes já definidos. |
| `400` nativo em todos os ambientes | `RouteHandlerOptions.ThrowOnBadRequest = false` em todos os ambientes (o padrão do ASP.NET é lançar exceção só em `Development`, o que viraria 500). |

## Árvore esperada de `POST /questions`

```text
POST /questions                          (servidor, ASP.NET Core)
├── embedding.create
│   └── POST generativelanguage…         (HTTP)
├── retrieval.search                     fonte.retrieval.top_k, fonte.retrieval.results
│   └── Grpc.Net.Client.GrpcOut          (gRPC client; grpc.method, grpc.status_code)
│       └── POST …cloud.qdrant.io        (HTTP)
└── answer.generate                      gen_ai.request.model, gen_ai.usage.*, fonte.answer.status
    └── POST generativelanguage…         (HTTP)
```

## Escopo

- Inclui: span gRPC, configuração OTLP local, arquivo de exemplo, ambiente `Testing` e factory compartilhada, desligamento da exportação em `Testing`, contrato de erro da aplicação (`500`, `400`, `404`, `405` com `ProblemDetails` e `traceId`), documentação, README e validação real dos três sinais no Grafana Cloud.
- Não inclui: dashboards e alertas, mudança de nível de log (sucessos de recuperação e geração seguem em Debug), tratamento de cancelamento como resultado próprio, erro específico para índice inexistente, extração do `TraceAsync` duplicado. Esses itens foram apontados na análise e ficam em aberto por decisão explícita.

## Arquivos

- `Fonte.Api/Observability/OpenTelemetryRegistration.cs`: `.AddSource("Grpc.Net.Client")` e exportação OTLP desligada em `Testing`.
- `Fonte.Api/appsettings.Development.example.json`: chaves OTLP.
- `Fonte.Api/ErrorHandling/ProblemDetailsRegistration.cs` (novo): `AddProblemDetails`, `ThrowOnBadRequest` e middlewares do contrato de erro.
- `Fonte.Api/Program.cs`: registro do contrato de erro.
- `Fonte.Api/Answering/QuestionEndpoint.cs`: título "Requisição inválida" no `400` de validação.
- `Fonte.Tests/FonteApiFactory.cs` (novo): factory com ambiente `Testing`.
- `Fonte.Tests/HealthEndpointTests.cs`, `Embeddings/GeminiConfigurationTests.cs`, `VectorStore/QdrantConfigurationTests.cs`, `Indexing/DocumentIndexingEndpointTests.cs` e `Answering/QuestionEndpointTests.cs`: usar a `FonteApiFactory`.
- `Fonte.Tests/TestingEnvironmentTests.cs` (novo): ambiente `Testing` sem `appsettings.Development.json`; regra de exportação OTLP.
- `Fonte.Tests/ErrorContractTests.cs` (novo): contrato de erro (`500`, `400` nativo, `404`, `405`, `traceId`, privacidade, igualdade em `Development`).
- `Fonte.Tests/Answering/QuestionEndpointTests.cs`: título e `traceId` do `400` de validação; corpo do `400` nativo.
- `docs/definicao-tecnica.md`, `README.md` e `docs/plans/008-observabilidade-grafana.md` (este plano).

## Passos

1. Gravar este plano.
2. Testes do isolamento e da regra de exportação (vistos falhando).
3. Factory, ambiente `Testing`, span gRPC e regra de exportação.
4. Arquivo de exemplo, documentação e README.
5. Build e testes, `scope-check` e `dotnet-code-review`.
6. **Parar** até a configuração OTLP local ser preenchida; depois, validação real.

## Validação

Automatizada (sem serviços reais):

- A aplicação de teste roda no ambiente `Testing` e nenhuma fonte de configuração é `appsettings.Development.json`.
- A regra de exportação: ligada com endpoint fora de `Testing`; desligada sem endpoint; desligada em `Testing` mesmo com endpoint.
- Toda a suíte existente continua passando com a `FonteApiFactory`.
- Contrato de erro: `500` com exatamente `type`, `title`, `status`, `detail` e `traceId`, textos fixos em pt-BR; `traceId` igual ao trace do `traceparent` enviado; corpo sem mensagem, tipo, stack trace, pergunta ou segredo da exceção; `400` nativo, `404` e `405` com `ProblemDetails` e `traceId`; o mesmo `500` e o `400` nativo em `Development` (com pasta de conteúdo vazia, sem configuração local).

Real (Grafana Cloud, após a configuração local):

1. Conferir o formato da configuração OTLP local sem exibir valores e que o arquivo segue ignorado.
2. `dotnet build` e `dotnet test`.
3. `dotnet run --project Fonte.Api` (Development).
4. `POST /documents/index`; `POST /questions` com as três perguntas de referência; uma pergunta inválida (400), conferindo que o `traceId` do `ProblemDetails` leva ao trace no Tempo. Registrar horários, status e `traceparent` enviado.
5. Aguardar mais de 60 s e encerrar a API de forma limpa.
6. Checklist no Grafana (feito pela usuária): Tempo com as árvores completas (inclusive `Grpc.Net.Client.GrpcOut`) e atributos; Mimir/Prometheus com `fonte.indexing.*`, `fonte.retrieval.duration`, `fonte.answer.duration` e métricas HTTP; Loki com logs do `fonte` correlacionados por `trace_id`; ausência de pergunta, resposta e conteúdo dos chunks.
7. Encerrar processos e confirmar o estado do repositório.

## Riscos

- Nomes das métricas no Prometheus dependem da conversão do Grafana Cloud (por exemplo, sufixo de unidade); serão confirmados na validação.
- Mensagens de exceção do SDK do Gemini ou do Qdrant exportadas nos logs podem conter texto da API de terceiros (risco residual já aceito para `DocumentPath`).
- O intervalo padrão de exportação de métricas é de 60 s.

## Em aberto (apontado explicitamente)

- Dashboards e alertas no Grafana.
- Cancelamento pelo cliente registrado como falha.
- Erro específico para índice inexistente em `POST /questions`.
- Extração do `TraceAsync` duplicado.

## Resultado da implementação (antes da validação real)

- `dotnet build`: sem avisos. `dotnet test Fonte.sln`: 175 testes passando (16 novos: 7 do ambiente `Testing` e da regra de exportação, 9 do contrato de erro). Nenhuma dependência nova.
- O teste `IntegrationTestsDoNotLoadLocalDevelopmentSettings` falhou antes da `FonteApiFactory`, confirmando que os testes carregavam o `appsettings.Development.json` local.
- Os testes do contrato de erro foram conferidos com defeitos introduzidos temporariamente, cada um detectado: `traceId` no formato padrão do ASP.NET, mensagem da exceção no `detail`, ausência de `ThrowOnBadRequest = false` (o `400` em `Development` virava `500`) e ausência de `UseStatusCodePages` (`400` nativo, `404` e `405` sem `ProblemDetails`). O código foi restaurado em seguida.

### Desvios e detalhes não previstos no plano

- **Contrato de erro da aplicação**: surgiu durante a implementação, quando o ambiente `Testing` revelou que o `500` dependia da Developer Exception Page; decidido e registrado na seção própria deste plano antes de ser implementado.
- **Teste em `Development` sem configuração local**: para provar que o contrato de erro é o mesmo em `Development` sem violar o isolamento, o teste sobe a aplicação em `Development` com a pasta de conteúdo apontando para uma pasta temporária vazia (nenhum `appsettings` é carregado).

## Validação real (27/09/2026, 02:00–02:03 UTC)

Executada com a API real (`dotnet run`, `Development`) exportando para o Grafana Cloud pela configuração local.

- `POST /documents/index`: 200, 3 documentos e 15 chunks, `cleanupCompleted = true` (nova collection publicada pelo blue/green).
- `POST /questions`: as duas perguntas do corpus retornaram `answered` com fontes 1 a 3 na ordem de score; a pergunta fora do domínio retornou `insufficient_context` com `sources: []`.
- Pergunta inválida: 400 `ProblemDetails` "Requisição inválida", com `traceId` idêntico ao trace do `traceparent` enviado.
- Autodiagnóstico do SDK do OpenTelemetry (nível Warning, ativado só durante o teste): nenhuma falha de exportação; apenas avisos de meters não assinados (QUIC e runtime).
- Nenhum `warn`, `fail` ou `crit` nos logs da aplicação.
- A conferência na interface do Grafana (árvores no Tempo, métricas no Mimir, logs e correlação no Loki, privacidade) ficou com a usuária, pelo checklist deste plano.
- Observação: após a reindexação, os scores variaram levemente (por exemplo, 0,7694 → 0,7736) porque o Gemini devolveu embeddings ligeiramente diferentes para o mesmo texto; fontes e ordem não mudaram.
