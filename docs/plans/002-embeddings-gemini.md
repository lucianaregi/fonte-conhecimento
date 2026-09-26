# Plano 002: embeddings dos chunks com Gemini

**Status**: implementado e aprovado.

## Objetivo

Gerar um vetor para cada `DocumentChunk` usando a Gemini API, sem armazenamento e sem novos endpoints.

## Decisões

| Tema | Decisão |
|---|---|
| Dependência | `Google.GenAI` 1.22.0, SDK oficial do Google para .NET (GA, Apache-2.0, `net8.0`). Vulnerabilidades verificadas com `dotnet list package --vulnerable --include-transitive` após a adição. |
| Contrato | O Fonte depende de `IEmbeddingGenerator<string, Embedding<float>>` (`Microsoft.Extensions.AI`), fornecido pelo SDK via `client.AsIEmbeddingGenerator(model, dimensions)`. Sem interface própria. `ChunkEmbedder.EmbedAsync(IReadOnlyList<DocumentChunk>, CancellationToken)` retorna `IReadOnlyList<EmbeddedChunk>`, com `EmbeddedChunk(DocumentChunk Chunk, ReadOnlyMemory<float> Vector)`. |
| Modelo | `gemini-embedding-2`, configurável em `Gemini:EmbeddingModel`. |
| Dimensões | `Gemini:EmbeddingDimensions`, padrão 768. Validação de 128 a 3072, faixa documentada para `gemini-embedding-2`. |
| Texto do documento | `title: {DocumentPath} \| text: {Content}`, formato documentado para documentos no `gemini-embedding-2` (que não aceita `task_type`). Sem extração de título do Markdown. |
| Batching | Um chunk por chamada, chamadas sequenciais. A documentação não informa o máximo de entradas por `batchEmbedContents`; nenhum tamanho de lote é adotado nem configurável. |
| Conferência da resposta | Exceção se a chamada não retornar exatamente um vetor ou se o vetor não tiver a dimensão configurada. |
| Chave | `Gemini:ApiKey`: user-secrets em desenvolvimento (`UserSecretsId` no `.csproj`) e `Gemini__ApiKey` nos demais ambientes. Passada explicitamente para `new Client(apiKey: ..., vertexAI: false)`, sem depender de `GEMINI_API_KEY`, `GOOGLE_API_KEY` ou `GOOGLE_GENAI_USE_VERTEXAI`. Exigida apenas quando o gerador é criado; a API e `/health` sobem sem ela. Nenhum segredo no repositório. |
| Validação na inicialização | `EmbeddingModel` obrigatório e `EmbeddingDimensions` entre 128 e 3072. A chave fica fora. |

## Escopo

- Inclui: geração de embeddings a partir de `DocumentChunk`, configuração e tratamento da chave.
- Não inclui: Qdrant e armazenamento, `POST /documents/index`, `POST /questions`, embedding da pergunta, geração de resposta, lote com mais de um chunk, retry, paralelismo, Batch API e telemetria.

## Arquivos

- `Fonte.Api/Fonte.Api.csproj`: pacote `Google.GenAI` e `UserSecretsId`.
- `Fonte.Api/Embeddings/GeminiOptions.cs` (novo): `ApiKey`, `EmbeddingModel` e `EmbeddingDimensions`, com validação.
- `Fonte.Api/Embeddings/EmbeddedChunk.cs` (novo).
- `Fonte.Api/Embeddings/ChunkEmbedder.cs` (novo).
- `Fonte.Api/Program.cs`: registro das opções, do `Client` (singleton criado sob demanda, exigindo a chave), do `IEmbeddingGenerator` e do `ChunkEmbedder`. Nenhuma rota alterada.
- `Fonte.Api/appsettings.json`: seção `Gemini` com `EmbeddingModel` e `EmbeddingDimensions`, sem a chave.
- `Fonte.Tests/Embeddings/ChunkEmbedderTests.cs` (novo).
- `docs/definicao-tecnica.md`: registrar as decisões desta etapa.
- `docs/plans/002-embeddings-gemini.md` (novo): este plano.

## Passos

1. Gravar este plano.
2. Adicionar o pacote e verificar vulnerabilidades.
3. Testes (vistos falhando).
4. `GeminiOptions`, `EmbeddedChunk` e `ChunkEmbedder`.
5. DI, `appsettings.json` e `.csproj`.
6. Documentação e resultado no plano.
7. Build e testes, `scope-check` e `dotnet-code-review`.

## Validação

Sem chamada real à Gemini API. `IEmbeddingGenerator` falso, escrito à mão no projeto de testes:

- texto enviado é `title: {DocumentPath} | text: {Content}`;
- uma chamada por chunk, com um único texto cada, na ordem dos chunks;
- cada vetor associado ao chunk correto;
- lista vazia não chama o gerador;
- zero ou dois vetores retornados, ou dimensão diferente da configurada, geram exceção;
- `CancellationToken` repassado ao gerador;
- configuração inválida (dimensão fora de 128 a 3072, modelo vazio) impede a API de subir;
- `/health` continua passando sem chave;
- comando: `dotnet test Fonte.sln`.

## Riscos

- Uma requisição por chunk consome mais do limite de requisições por minuto do plano gratuito; sem retry, um 429 interrompe a geração.
- O SDK converte `double` para `float` (perda de precisão desprezível).

## Em aberto

- Tamanho do lote, quando houver base documentada ou medida.
- Política de retry para 429 e 5xx.
- Formato e fluxo do embedding da pergunta (etapa de `POST /questions`).
- ID dos pontos no Qdrant e reindexação.

## Resultado da implementação

Implementado conforme o plano, sem chamada real à Gemini API.

- `dotnet list package --vulnerable --include-transitive`: nenhum pacote vulnerável em `Fonte.Api` e `Fonte.Tests`.
- `dotnet build`: sem avisos. `dotnet test Fonte.sln`: 30 testes passando (12 novos).
- O caso "sem chave" também foi verificado com uma chave falsa temporária no user-secrets, removida em seguida: a configuração do teste prevalece sobre o user-secrets.

### Desvios e detalhes não previstos no plano

- **Arquivo de teste adicional**: os casos de configuração inválida e de chave ausente ficaram em `Fonte.Tests/Embeddings/GeminiConfigurationTests.cs`, separados de `ChunkEmbedderTests.cs`, porque exercitam a inicialização da API e não o `ChunkEmbedder`.
- **Teste de chave ausente**: resolver o `IEmbeddingGenerator` sem `Gemini:ApiKey` lança `InvalidOperationException` com o nome da chave na mensagem.
- **BOM removido do `.csproj`**: o `dotnet user-secrets init` acrescentou um BOM à primeira linha; foi removido para não gerar diff fora do escopo.
