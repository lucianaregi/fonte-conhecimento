# Plano 009: especificação OpenAPI e interface Swagger

**Status**: implementado e aprovado.

## Objetivo

Expor a especificação OpenAPI da API e uma interface Swagger para testar os endpoints existentes, documentando `GET /health`, `POST /documents/index` e `POST /questions` (requisições, respostas e `ProblemDetails`), somente em `Development` e sem alterar os contratos atuais.

## Decisões

| Tema | Decisão |
|---|---|
| Geração da especificação | `Microsoft.AspNetCore.OpenApi` 10.0.12 (MIT), abordagem nativa recomendada pela documentação do ASP.NET Core 10. Documento `v1`, OpenAPI 3.1 (padrão no .NET 10). |
| Interface | `Swashbuckle.AspNetCore.SwaggerUI` 10.2.3 (MIT, sem dependências NuGet), apenas os assets da Swagger UI apontando para o documento nativo. O gerador do Swashbuckle não é usado. |
| URLs | Especificação em `/openapi/v1.json`; interface em `/swagger`. |
| Ambiente | `AddOpenApi` sempre registrado; `MapOpenApi` e `UseSwaggerUI` apenas em `Development`, depois de `UseFonteErrorResponses`. Fora de `Development`, as rotas respondem `404` com `ProblemDetails`. |
| Metadados | Só metadados nos endpoints (`WithName`, `WithSummary`, `WithDescription`, `WithTags`, `Produces`, `ProducesProblem`, `ProducesValidationProblem`); os handlers não mudam. |
| `QuestionRequest` | `[Required]`, `[MaxLength(2000)]` e `[Description]` apenas para o schema. Sem `AddValidation`, não há efeito em tempo de execução: a validação e as mensagens atuais continuam as do endpoint. |
| `traceId` | Transformador de schema acrescenta `traceId` aos schemas de `ProblemDetails` e `HttpValidationProblemDetails`, pois a extensão não aparece no schema padrão. |
| `launchSettings` | `"launchUrl": "swagger"` no perfil `http`. |
| Traces | Requisições a `/swagger` e `/openapi` não são filtradas da telemetria. |

## Escopo

- Inclui: pacotes, registro da OpenAPI e da Swagger UI, metadados dos três endpoints, transformador do `traceId`, `launchUrl`, testes, README e definição técnica.
- Não inclui: mudança de contratos, rotas, status ou validação; filtro de traces; `AddValidation`; comentários XML na especificação; exposição fora de `Development`.

## Arquivos

- `Fonte.Api/Fonte.Api.csproj`: pacotes.
- `Fonte.Api/OpenApi/OpenApiRegistration.cs` (novo): documento `v1`, informações em pt-BR, transformador do `traceId`, mapeamento condicional.
- `Fonte.Api/Program.cs`: registro e metadados de `/health`.
- `Fonte.Api/Indexing/DocumentIndexingEndpoint.cs` e `Fonte.Api/Answering/QuestionEndpoint.cs`: metadados e descrições dos schemas.
- `Fonte.Api/Properties/launchSettings.json`: `launchUrl`.
- `Fonte.Tests/OpenApiTests.cs` (novo).
- `README.md`, `docs/definicao-tecnica.md` e este plano.

## Validação

- Testes novos (aplicação em `Development` com pasta de conteúdo vazia, sem credenciais): documento 3.1 com os três caminhos; respostas e schemas de `/documents/index` e `/questions`; `traceId` nos schemas de erro; `/swagger/index.html` com HTML; `404` com `ProblemDetails` para `/openapi/v1.json` e `/swagger` fora de `Development`.
- Suíte completa com `dotnet test Fonte.sln`, sem regressões.
- `dotnet list package --vulnerable --include-transitive`.
- API local em `Development`: abrir `/swagger` e `/openapi/v1.json`, sem indexar nem perguntar.

## Resultado da implementação

- `dotnet build` sem avisos; `dotnet test Fonte.sln`: 183 testes passando (8 novos em `OpenApiTests`). `dotnet list package --vulnerable --include-transitive`: nenhum pacote vulnerável.
- Os 5 testes de `Development` falharam antes da implementação; os 3 de `404` fora de `Development` já passavam e protegem contra regressão.
- Conferência com defeitos temporários, cada um detectado: sem o `traceId` nos schemas de erro (2 testes falham) e sem a restrição a `Development` (3 testes falham). O código foi restaurado em seguida.
- API real em `Development` (sem indexar nem perguntar): `/openapi/v1.json` 200 (OpenAPI 3.1.1, três operações com os status previstos), `/swagger` 301 para `/swagger/index.html` 200, sem avisos ou erros nos logs.
- Observação: os campos inteiros aparecem como `integer` ou `string` com `pattern`, porque as opções JSON web do ASP.NET aceitam números em string na leitura. É a representação do framework; o contrato não mudou.
