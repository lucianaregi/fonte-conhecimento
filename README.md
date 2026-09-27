# Fonte

Aplicação .NET que usa RAG (_Retrieval-Augmented Generation_) para responder perguntas com base em uma coleção de documentos Markdown, retornando a resposta junto com as fontes usadas. O projeto também explora a observabilidade de aplicações com IA por meio de traces, métricas e logs.

A stack é ASP.NET Core Minimal API, Gemini API (embeddings e geração), Qdrant Cloud (busca vetorial) e OpenTelemetry (exportação OTLP, com envio ao Grafana Cloud previsto).

O projeto está em desenvolvimento. Hoje a API expõe `GET /health`, `POST /documents/index`, que indexa os documentos Markdown de `Fonte.Api/documents/` no Qdrant, e `POST /questions`, que responde a uma pergunta com base nos documentos indexados e informa as fontes usadas.

A definição completa (fluxos, API da v1, observabilidade, configuração, definição de pronto e fora do escopo) está em [docs/definicao-tecnica.md](docs/definicao-tecnica.md).

## Pré-requisitos

- .NET SDK 10

## Configuração local

Copie `Fonte.Api/appsettings.Development.example.json` para `Fonte.Api/appsettings.Development.json` e preencha as credenciais do Gemini e do Qdrant (a URL do Qdrant usa a porta gRPC `6334`). Esse arquivo é ignorado pelo Git. Detalhes em [Configuração e segredos](docs/definicao-tecnica.md#configuração-e-segredos).

## Como executar

```bash
dotnet run --project Fonte.Api
```

A API sobe em `http://localhost:5023` (perfil `http` em [launchSettings.json](Fonte.Api/Properties/launchSettings.json)). Para indexar os documentos:

```bash
curl -X POST http://localhost:5023/documents/index
```

Para fazer uma pergunta:

```bash
curl -X POST http://localhost:5023/questions -H "Content-Type: application/json" -d '{"question": "Qual é a função do ActivitySource?"}'
```

## Como testar

```bash
dotnet test Fonte.sln
```

## Estrutura

- `Fonte.Api/`: API ASP.NET Core Minimal API.
- `Fonte.Api/documents/`: corpus Markdown de demonstração.
- `Fonte.Tests/`: testes automatizados (xUnit).
- `docs/`: documentação técnica e planos.
- `docs/plans/`: planos de cada etapa.

## Licença

[MIT](LICENSE)
