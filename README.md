# Fonte

Aplicação .NET que usa RAG (_Retrieval-Augmented Generation_) para responder perguntas com base em uma coleção de documentos Markdown, retornando a resposta junto com as fontes usadas. O projeto também explora a observabilidade de aplicações com IA por meio de traces, métricas e logs.

A stack prevista é ASP.NET Core Minimal API, Gemini API (embeddings e geração), Qdrant Cloud (busca vetorial) e OpenTelemetry com Grafana Cloud.

O projeto está em desenvolvimento. Hoje a API expõe apenas `GET /health`. A indexação de documentos e o endpoint de perguntas ainda não foram implementados.

A definição completa (fluxos, API da v1, observabilidade, configuração, definição de pronto e fora do escopo) está em [docs/definicao-tecnica.md](docs/definicao-tecnica.md).

## Pré-requisitos

- .NET SDK 10

## Como executar

```bash
dotnet run --project Fonte.Api
```

A API sobe em `http://localhost:5023` (perfil `http` em [launchSettings.json](Fonte.Api/Properties/launchSettings.json)).

## Como testar

```bash
dotnet test Fonte.sln
```

## Estrutura

- `Fonte.Api/`: API ASP.NET Core Minimal API.
- `Fonte.Tests/`: testes automatizados (xUnit).
- `docs/`: documentação técnica.

## Licença

[MIT](LICENSE)
