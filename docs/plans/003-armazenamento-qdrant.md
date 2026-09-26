# Plano 003: armazenamento dos chunks vetorizados no Qdrant

**Status**: implementado e aprovado.

## Objetivo

Persistir os `EmbeddedChunk` no Qdrant Cloud com reindexação completa blue/green, deixando a collection ativa acessível pelo alias. Sem leitura, sem busca e sem endpoints.

## Decisões

| Tema | Decisão |
|---|---|
| Dependência | `Qdrant.Client` 1.19.0, SDK oficial (Apache-2.0). Vulnerabilidades verificadas com `dotnet list package --vulnerable --include-transitive` após a adição. |
| Reindexação | Blue/green com alias (documentado pelo Qdrant para reindexar sem interrupção). Cada reindexação cria `{alias}-{yyyyMMddHHmmssfff}` (UTC), grava tudo, troca o alias atomicamente e só então apaga as collections antigas. |
| Collection | Tamanho do vetor vindo de `Gemini:EmbeddingDimensions`, distância `Cosine` (recomendada pelo Gemini) e índice keyword em `document_path`. |
| Payload | `document_path` (keyword), `chunk_index` (inteiro) e `content`. |
| IDs | UUID v5 (baseado em nome, SHA-1, RFC 9562) de `DocumentPath` + `Index`, implementado sem dependência. |
| Contrato do Fonte | `ChunkVectorStore.ReplaceAllAsync(IReadOnlyList<EmbeddedChunk>, CancellationToken)`, única operação pública. |
| Abstração | `IQdrantGateway`, só com tipos do Fonte e as operações desta etapa: `GetAliasTargetAsync`, `ListCollectionsAsync`, `CreateCollectionAsync(name, dimensions, VectorDistance)`, `CreateKeywordIndexAsync`, `UpsertAsync(collection, IReadOnlyList<ChunkPoint>)` (com `wait: true`), `SwitchAliasAsync(alias, newCollection, replaceExisting)` (uma única chamada atômica de `UpdateAliasesAsync`) e `DeleteCollectionAsync`. |
| Adapter | `QdrantGateway` implementa `IQdrantGateway` com `QdrantClient`; único ponto que conhece o SDK. |
| Unidade de upsert | Um documento por chamada. A documentação não informa limite por requisição; por documento, o tamanho de cada requisição fica limitado pelo documento. A abstração aceita lista de pontos, então mudar a unidade não altera o contrato. |
| Falha antes da troca do alias | A exceção original sobe; alias e collections existentes intactos; a collection incompleta fica para a próxima reindexação bem-sucedida limpar. |
| Falha na limpeza depois da troca | A nova indexação já está ativa. Lança exceção própria (`VectorStoreCleanupException`), distinta da falha de publicação, com a collection ativa e a que não pôde ser apagada. Sem retry e sem mecanismo novo de recuperação: a próxima reindexação bem-sucedida limpa as sobras. |
| Configuração | `Qdrant:CollectionName` (alias, padrão `fonte-chunks`) no `appsettings.json`. `Qdrant:Url` (gRPC, porta 6334) e `Qdrant:ApiKey` via user-secrets em desenvolvimento e `Qdrant__Url` / `Qdrant__ApiKey` nos demais ambientes; exigidos só ao criar o cliente. A API e `/health` sobem sem Qdrant. |

## Sequência do `ReplaceAllAsync`

1. Lê o alvo atual do alias.
2. Cria a collection nova (dimensão configurada, `Cosine`) e o índice em `document_path`.
3. Um upsert por documento.
4. Troca o alias, somente depois de todos os upserts.
5. Apaga as collections com o padrão exato `{alias}-` + 17 dígitos, exceto a nova (inclui a antiga ativa e sobras de tentativas anteriores).

Lista vazia de chunks gera uma collection vazia, que se torna a ativa.

## Escopo

- Inclui: persistência com reindexação completa blue/green, abstração e adapter, configuração e segredos.
- Não inclui: leitura por documento, busca vetorial, endpoints, embedding da pergunta, geração de resposta, telemetria, retry, paralelismo e detecção de mudanças.

## Arquivos

- `Fonte.Api/Fonte.Api.csproj`: pacote `Qdrant.Client`.
- `Fonte.Api/VectorStore/QdrantOptions.cs` (novo).
- `Fonte.Api/VectorStore/ChunkPointId.cs` (novo).
- `Fonte.Api/VectorStore/ChunkPoint.cs` e `VectorDistance.cs` (novos).
- `Fonte.Api/VectorStore/IQdrantGateway.cs` (novo).
- `Fonte.Api/VectorStore/QdrantGateway.cs` (novo): adapter, com conversões puras estáticas.
- `Fonte.Api/VectorStore/ChunkVectorStore.cs` (novo).
- `Fonte.Api/VectorStore/VectorStoreCleanupException.cs` (novo).
- `Fonte.Api/Program.cs`: registro das opções, do `QdrantClient` (singleton sob demanda), do `IQdrantGateway`, do `ChunkVectorStore` e de `TimeProvider.System`. Nenhuma rota alterada.
- `Fonte.Api/appsettings.json`: `Qdrant:CollectionName`.
- `Fonte.Tests/VectorStore/ChunkVectorStoreTests.cs`, `ChunkPointIdTests.cs`, `QdrantGatewayMappingTests.cs` e `QdrantConfigurationTests.cs` (novos).
- `docs/definicao-tecnica.md`: registrar as decisões desta etapa.
- `docs/plans/003-armazenamento-qdrant.md` (novo): este plano.

## Passos

1. Gravar este plano.
2. Adicionar o pacote e verificar vulnerabilidades.
3. Testes (vistos falhando).
4. Implementação.
5. DI e configuração.
6. Documentação e resultado no plano.
7. Build e testes, `scope-check` e `dotnet-code-review`.

## Validação

Sem Qdrant real.

- `ChunkVectorStoreTests`, com dublê manual de `IQdrantGateway` que registra as operações em ordem e pode falhar em operações específicas, e `TimeProvider` falso:
  - collection nova com nome `{alias}-{timestamp}`, dimensão configurada e `Cosine`;
  - índice keyword em `document_path`;
  - um upsert por documento com IDs, vetores e payload corretos;
  - troca do alias depois de todos os upserts;
  - falha num upsert: sem troca de alias, sem remoção de collections, exceção original propagada;
  - remoção de collections antigas só depois da troca;
  - sem alias existente: alias criado sem substituição;
  - sobra de tentativa anterior removida; antiga ativa removida após a troca; a nova nunca removida;
  - collections com outro padrão de nome não são tocadas;
  - falha na limpeza depois da troca: `VectorStoreCleanupException`, com o alias já apontando para a nova;
  - `CancellationToken` repassado.
- `ChunkPointIdTests`: determinismo, variação por documento e por índice, vetor de teste conhecido do UUID v5.
- `QdrantGatewayMappingTests`: `PointStruct` com ID, vetor e payload corretos; `VectorDistance.Cosine` para `Distance.Cosine`.
- `QdrantConfigurationTests`: sem URL ou sem chave, resolver o cliente lança exceção; `/health` continua passando.
- Comando: `dotnet test Fonte.sln`.
- Sem teste automatizado: apenas as chamadas diretas do `QdrantGateway` ao SDK.

## Riscos

- Cluster gratuito suspenso após 1 semana sem uso e apagado após 4; exigiria reindexação.
- Sobras de falhas ocupam espaço até a próxima reindexação bem-sucedida.
- Duas reindexações simultâneas poderiam apagar a collection uma da outra; sem gatilho concorrente nesta etapa.
- Não verificado: segurança do `QdrantClient` para uso concorrente (não documentado).

## Em aberto

- Limite de pontos ou bytes por upsert, se for preciso agrupar mais de um documento.
- Controle de concorrência da reindexação (etapa de `POST /documents/index`).
- Busca vetorial pelo alias (etapa de perguntas).

## Resultado da implementação

Implementado conforme o plano, sem acesso ao Qdrant real.

- `dotnet list package --vulnerable --include-transitive`: nenhum pacote vulnerável em `Fonte.Api` e `Fonte.Tests`.
- `dotnet build`: sem avisos. `dotnet test Fonte.sln`: 53 testes passando (23 novos).
- Os testes da sequência foram conferidos com defeitos introduzidos temporariamente (alias trocado antes das gravações, collection nova incluída na limpeza, falha de limpeza sem distinção): cada defeito fez ao menos um teste falhar. O código foi restaurado em seguida.

### Desvios e detalhes não previstos no plano

- **Cultura no nome da collection** (achado do `dotnet-code-review`): o timestamp é formatado com `CultureInfo.InvariantCulture`; com a cultura atual, um calendário não gregoriano (ex.: `th-TH`) geraria o ano errado. Coberto por teste.
- **Cancelamento durante a limpeza** também vira `VectorStoreCleanupException`: depois da troca do alias, o chamador precisa saber que a nova indexação está ativa.
- **`VectorStoreCleanupException.FailedCollection`** é `null` quando a falha ocorre ao listar as collections, antes de tentar apagar alguma.
- **Nome usado no UUID v5**: `{index}:{documentPath}`, com um namespace fixo do Fonte. `ChunkPointId.CreateVersion5` é público para permitir o teste com o vetor do RFC 9562.
