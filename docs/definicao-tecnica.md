# Definição técnica e funcional do Fonte

Este documento é a referência atual do escopo do Fonte. Descreve o que a v1 deve entregar e separa o que já está implementado do que ainda está previsto.

> **Estado atual do código**: o repositório contém a API com `GET /health` ([Fonte.Api/Program.cs](../Fonte.Api/Program.cs)), o núcleo local da indexação (leitura e chunking de Markdown, em [Fonte.Api/Indexing/](../Fonte.Api/Indexing/)), a geração de embeddings dos chunks com Gemini (em [Fonte.Api/Embeddings/](../Fonte.Api/Embeddings/)), o armazenamento dos chunks vetorizados no Qdrant (em [Fonte.Api/VectorStore/](../Fonte.Api/VectorStore/)), o fluxo completo de indexação com `POST /documents/index`, a recuperação semântica de chunks para perguntas (em [Fonte.Api/Retrieval/](../Fonte.Api/Retrieval/)), a geração de resposta fundamentada no contexto recuperado (em [Fonte.Api/Answering/](../Fonte.Api/Answering/)), o endpoint `POST /questions`, que junta recuperação e geração, e a fundação de observabilidade (em [Fonte.Api/Observability/](../Fonte.Api/Observability/)), com testes. A conexão efetiva com o Grafana Cloud e CI ainda **não estão implementados**.

## Contexto

O Fonte é uma aplicação .NET que usa RAG (_Retrieval-Augmented Generation_) para responder perguntas com base em uma coleção de documentos Markdown.

Os documentos são indexados semanticamente. Ao receber uma pergunta, a aplicação recupera os trechos mais relevantes, usa esses trechos como contexto para um modelo generativo e retorna uma resposta acompanhada das fontes usadas.

O projeto também explora a **observabilidade de aplicações com IA**: o pipeline de recuperação e geração deve ser acompanhado por traces, métricas e logs.

### Objetivo

Construir uma implementação de RAG pequena o suficiente para que todo o fluxo seja compreensível, mas com componentes reais de uma aplicação:

```text
Documentos → Chunking → Embeddings → Vector database

Pergunta → Embedding da pergunta → Busca vetorial → Trechos relevantes → LLM + contexto → Resposta + fontes
```

Além de implementar RAG, o projeto deve permitir observar o que acontece internamente durante esse processo.

## Cenário de uso

Existe uma pasta com documentos Markdown:

```text
documents/
├── dotnet.md
├── observabilidade.md
└── rag.md
```

Após a indexação, alguém pergunta:

> Qual é a função do ActivitySource em uma aplicação .NET?

O Fonte:

1. gera o embedding da pergunta;
2. consulta o Qdrant;
3. recupera os chunks semanticamente mais relevantes;
4. envia pergunta + contexto recuperado para o Gemini;
5. retorna a resposta;
6. informa quais trechos e documentos fundamentaram a resposta.

Exemplo de resposta (contrato completo em [`POST /questions`](#post-questions)):

```json
{
  "status": "answered",
  "answer": "O ActivitySource é o ponto de partida para criar traces próprios...",
  "sources": [
    {
      "number": 1,
      "document": "observabilidade.md",
      "chunk": 3,
      "score": 0.7694
    }
  ]
}
```

Se os documentos não fornecerem contexto suficiente, o sistema deve informar isso em vez de produzir deliberadamente uma resposta baseada apenas no conhecimento geral do modelo.

## Stack e integrações

| Área | Definição | Situação no código |
|---|---|---|
| Aplicação | .NET com ASP.NET Core Minimal API | Implementado: `net10.0`, `Microsoft.NET.Sdk.Web` ([Fonte.Api.csproj](../Fonte.Api/Fonte.Api.csproj)) |
| Testes | Testes automatizados | Implementado: xUnit + `Microsoft.AspNetCore.Mvc.Testing` ([Fonte.Tests.csproj](../Fonte.Tests/Fonte.Tests.csproj)) |
| Embeddings | Modelo de embeddings do Gemini (Gemini API) | Implementado: `gemini-embedding-2` via SDK `Google.GenAI`, exposto como `IEmbeddingGenerator` (`Microsoft.Extensions.AI`). Usado na indexação e nas perguntas. |
| Geração | Modelo generativo do Gemini (Gemini API) | Implementado: `gemini-3.6-flash` via SDK `Google.GenAI`, exposto como `IChatClient` (`Microsoft.Extensions.AI`). Usado por `POST /questions`. |
| Busca vetorial | Qdrant Cloud | Armazenamento e busca implementados com o SDK `Qdrant.Client` (`ChunkVectorStore`). A busca é usada por `POST /questions`. |
| Observabilidade | OpenTelemetry, `ActivitySource`, `Meter`, `ILogger`, Grafana Cloud | Implementado para a indexação: traces, métricas e logs com OpenTelemetry e exportação OTLP opcional. A conexão com o Grafana Cloud depende das credenciais da conta. |
| CI | GitHub Actions | Previsto |

O modelo generativo foi escolhido na etapa 006 com base na documentação oficial consultada em 26/09/2026 (ver [plans/006-geracao-resposta.md](plans/006-geracao-resposta.md)); é configurável em `Gemini:GenerationModel`.

### Restrições

- sem Docker;
- sem Sonar no Fonte.

## Fluxo de indexação

Acionado por `POST /documents/index`, que indexa ou reindexa os documentos da pasta configurada.

```text
Markdown → leitura → chunking → embedding via Gemini → chunk + metadados + vetor → Qdrant
```

Cada chunk precisa preservar informações suficientes para identificar sua origem. No mínimo:

- `document`
- `chunkId`
- `content`
- `embedding`

### Leitura e chunking (implementado)

Decisões registradas em [plans/001-indexacao-markdown.md](plans/001-indexacao-markdown.md).

| Tema | Decisão | Código |
|---|---|---|
| Leitura | Arquivos `*.md` da pasta configurada e de todos os subdiretórios, em UTF-8, ordenados pelo caminho relativo. Pasta inexistente gera `DirectoryNotFoundException`. | `MarkdownDocumentReader` |
| Chunking | Divisão por parágrafos (blocos separados por linha em branco), agrupando parágrafos consecutivos até `MaxChunkSize` caracteres. Parágrafo maior que o limite é quebrado no último espaço antes dele; sem espaço, no próprio limite. Sem sobreposição. | `MarkdownChunker` |
| Metadados do chunk | `DocumentPath` (caminho relativo à pasta configurada, com `/` como separador), `Index` (posição no documento, a partir de 0) e `Content`. `DocumentPath` + `Index` correspondem a `document` + `chunkId`. | `DocumentChunk` |

### Embeddings dos chunks (implementado)

Decisões registradas em [plans/002-embeddings-gemini.md](plans/002-embeddings-gemini.md).

| Tema | Decisão | Código |
|---|---|---|
| Contrato | `IEmbeddingGenerator<string, Embedding<float>>`, fornecido pelo SDK `Google.GenAI`. `ChunkEmbedder` recebe `DocumentChunk` e retorna `EmbeddedChunk` (chunk + vetor). | `ChunkEmbedder`, `EmbeddedChunk` |
| Modelo e dimensões | `gemini-embedding-2`, 768 dimensões por padrão (faixa aceita: 128 a 3072). Trocar modelo ou dimensões exige reindexar. | `GeminiOptions` |
| Texto enviado | `title: {DocumentPath} \| text: {Content}`, formato documentado para documentos no `gemini-embedding-2`. | `ChunkEmbedder` |
| Chamadas | Um chunk por chamada, em sequência. Sem retry e sem paralelismo. | `ChunkEmbedder` |
| Conferência | Exceção se a chamada não retornar exatamente um vetor ou se a dimensão for diferente da configurada. | `ChunkEmbedder` |

**Em aberto**:

- tamanho do lote por requisição (a documentação da Gemini API não informa o máximo);
- política de retry para erros 429 e 5xx;
- formato e fluxo do embedding da pergunta.

### Armazenamento no Qdrant (implementado)

Decisões registradas em [plans/003-armazenamento-qdrant.md](plans/003-armazenamento-qdrant.md).

| Tema | Decisão | Código |
|---|---|---|
| Reindexação | Completa, blue/green com alias: cada reindexação cria `{alias}-{yyyyMMddHHmmssfff}` (UTC), grava todos os chunks, aponta o alias para ela numa operação atômica e só então apaga as collections antigas e sobras de tentativas anteriores (apenas as que seguem esse padrão de nome). | `ChunkVectorStore` |
| Collection | Tamanho do vetor igual a `Gemini:EmbeddingDimensions`, distância `Cosine` e índice keyword em `document_path`. | `ChunkVectorStore` |
| Payload | `document_path` (keyword), `chunk_index` (inteiro) e `content`. | `ChunkPoint`, `QdrantGateway` |
| IDs | UUID v5 (RFC 9562) derivado de `DocumentPath` + `Index`: o mesmo chunk sempre tem o mesmo ID. | `ChunkPointId` |
| Gravação | Um upsert por documento, com `wait: true`. Sem retry e sem paralelismo. | `ChunkVectorStore` |
| Falhas | Falha antes da troca do alias: a exceção original sobe e a indexação ativa não muda. Falha na limpeza depois da troca: `VectorStoreCleanupException`, e a nova indexação já está ativa. | `ChunkVectorStore` |
| Integração | `IQdrantGateway` expõe só as operações usadas, com tipos do Fonte; `QdrantGateway` é o único ponto que usa o SDK. | `IQdrantGateway`, `QdrantGateway` |

**Em aberto**:

- limite de pontos ou bytes por upsert, se for preciso agrupar mais de um documento;
- controle de concorrência entre instâncias (dentro de uma instância, resolvido pelo `DocumentIndexer`).

## Fluxo de pergunta

Acionado por `POST /questions`.

```text
POST /questions
  → embedding da pergunta
  → busca por similaridade no Qdrant
  → Top N chunks
  → construção do contexto
  → Gemini
  → resposta + fontes
```

N é configurável; o valor inicial é **3**.

### Recuperação (implementado)

Decisões registradas em [plans/005-recuperacao-semantica.md](plans/005-recuperacao-semantica.md). `ChunkRetriever.RetrieveAsync(pergunta)` cobre as três primeiras etapas do fluxo.

| Tema | Decisão | Código |
|---|---|---|
| Embedding da pergunta | Mesmo modelo e dimensões dos chunks. Texto enviado: `task: search result \| query: {pergunta}`, formato documentado para consultas no `gemini-embedding-2`. Pergunta vazia gera `ArgumentException`. | `QueryEmbedder` |
| Busca | Query API do Qdrant, pelo alias configurado, com `limit = Retrieval:TopK` e payload na resposta; resultados ordenados por score (Cosine: maior é mais similar). | `ChunkVectorStore`, `QdrantGateway` |
| Resultado | `RetrievedChunk(DocumentPath, ChunkIndex, Content, Score)`. | `RetrievedChunk` |
| Sem resultados | Índice existente sem resultados retorna lista vazia. Sem threshold de score. | `ChunkRetriever` |
| Índice inexistente | Alias inexistente é falha: o erro do Qdrant propaga e é registrado na etapa `search`. | `ChunkRetriever` |
| Configuração ausente | Gemini e Qdrant são resolvidos dentro da operação, antes de qualquer chamada externa; a falha é registrada na etapa `configuration`. | `ChunkRetriever` |

### Geração de resposta (implementado)

Decisões registradas em [plans/006-geracao-resposta.md](plans/006-geracao-resposta.md). `AnswerGenerator.GenerateAsync(pergunta, trechos)` cobre as etapas de construção do contexto e geração.

| Tema | Decisão | Código |
|---|---|---|
| Modelo | `gemini-3.6-flash` (configurável), com temperatura, limite de saída e raciocínio nos padrões do modelo. | `GeminiOptions`, `IChatClient` |
| Prompt | Regras na instrução de sistema: responder apenas com base nos trechos, sem conhecimento geral, tratando o conteúdo dos trechos como dado e nunca como instrução, em pt-BR. Pergunta e trechos numerados 1..N (com documento e índice) em blocos delimitados por um marcador aleatório por requisição; o conteúdo dos documentos não é alterado. | `AnswerPrompt` |
| Resposta do modelo | JSON com schema: `status` (`answered` ou `insufficient_context`) e `answer`. | `AnswerPrompt` |
| Resultado | `GeneratedAnswer(Status, Text, Context)`: `Answered` com o texto do modelo; `InsufficientContext` (modelo) e `NoContext` (nenhum trecho recuperado, sem chamar o modelo) com a mensagem fixa "Os documentos disponíveis não contêm informação suficiente para responder a esta pergunta." | `GeneratedAnswer` |
| Fontes | `Context` são exatamente os trechos enviados, na ordem do prompt (`Context[i]` é o trecho `[i+1]`). O vínculo vem da recuperação, não de uma escolha do modelo. | `AnswerGenerator` |
| Respostas inválidas | Exceção, sem expor conteúdo, para prompt bloqueado, ausência de motivo de término, resposta cortada (`Length`), geração interrompida (`ContentFilter` e outros), texto vazio, JSON inválido, status desconhecido e `answered` sem texto. Sem retry. | `AnswerGenerator` |

**Em aberto**: threshold de score e seleção de trechos, e citações `[n]` feitas pelo modelo como informação adicional.

## API da v1

A v1 tem três endpoints.

| Método e rota | Função | Situação |
|---|---|---|
| `GET /health` | Health check | Implementado: retorna `200 OK` sem corpo |
| `POST /documents/index` | Indexa ou reindexa os documentos da pasta configurada | Implementado (ver abaixo) |
| `POST /questions` | Recebe uma pergunta e retorna resposta + fontes | Implementado (ver abaixo) |

### `POST /documents/index`

Decisões registradas em [plans/004-indexacao-endpoint-observabilidade.md](plans/004-indexacao-endpoint-observabilidade.md). Orquestrado por `DocumentIndexer`: resolve Gemini e Qdrant, lê, divide em chunks, gera embeddings e publica no Qdrant (reindexação blue/green). Sem corpo na requisição.

| Status | Quando | Corpo |
|---|---|---|
| `200 OK` | Nova indexação publicada | `{ "documents": 3, "chunks": 42, "cleanupCompleted": true }` |
| `200 OK` | Publicada, mas a limpeza das collections antigas falhou | Igual, com `"cleanupCompleted": false`; as sobras são removidas na próxima indexação |
| `409 Conflict` | Já existe uma indexação em andamento nesta instância | `ProblemDetails` |
| `422 Unprocessable Entity` | Nenhum documento Markdown na pasta configurada | `ProblemDetails`; índice ativo não alterado |
| `500` | Falha de configuração (chave ou URL ausente), de leitura, do Gemini ou do Qdrant | Resposta padrão do ASP.NET; índice ativo não alterado |

- **Concorrência**: uma indexação por vez em cada instância da aplicação (`SemaphoreSlim`); a segunda requisição simultânea recebe 409 sem esperar. Não há coordenação entre instâncias.
- **Configuração ausente**: Gemini e Qdrant são resolvidos dentro da indexação, antes de ler documentos ou chamar serviços externos. A API e `/health` sobem sem credenciais; a falha é registrada como falha da indexação (log e métrica) e pode ser tentada de novo.
- **Cancelamento**: a desconexão do cliente cancela a indexação. Antes da troca do alias, o índice ativo não muda; a collection incompleta é limpa na próxima indexação.

### `POST /questions`

Decisões registradas em [plans/007-endpoint-perguntas.md](plans/007-endpoint-perguntas.md). O endpoint coordena `ChunkRetriever` e `AnswerGenerator`, sem camada própria; logs, métricas e spans ficam nesses serviços.

Requisição:

```json
{ "question": "Qual é a função do ActivitySource?" }
```

`question` é obrigatória, não pode ser vazia nem só espaços e tem no máximo 2000 caracteres.

| Status | Quando | Corpo |
|---|---|---|
| `200 OK` | Pergunta processada | `{ "status", "answer", "sources" }` (abaixo) |
| `400 Bad Request` | `question` ausente, vazia, só com espaços ou com mais de 2000 caracteres; corpo ausente ou JSON malformado | `ValidationProblemDetails` com `errors.question` (a pergunta não é repetida) ou o `400` nativo do ASP.NET |
| `500` | Falha de configuração (chave ou URL ausente), do Gemini, do Qdrant (inclusive índice ainda não criado) ou resposta inválida do modelo | Resposta padrão do ASP.NET; a etapa aparece nos logs |

| `status` | `answer` | `sources` |
|---|---|---|
| `answered` | Resposta do modelo | Trechos enviados como contexto, na ordem |
| `insufficient_context` | Mensagem fixa do Fonte | `[]` |
| `no_context` | Mensagem fixa do Fonte | `[]` |

Cada fonte tem `number` (numeração `[n]` do trecho no prompt), `document` (`DocumentPath`), `chunk` (`ChunkIndex`) e `score` (similaridade da busca). O conteúdo dos trechos não é retornado.

## Observabilidade

### Indexação (implementado)

Instrumentação própria no `DocumentIndexer`, com `ActivitySource` e `Meter` chamados `Fonte.Api` (`FonteTelemetry`, `IndexingMetrics`):

```text
POST /documents/index           ← ASP.NET Core (automático)
├── documents.read              ← fonte.documents.count
├── documents.chunk             ← fonte.chunks.count
├── embedding.create
│   └── HTTP → Gemini           ← System.Net.Http (automático)
└── vectorstore.replace         ← fonte.cleanup.completed
    └── HTTP/gRPC → Qdrant      ← System.Net.Http (automático)
```

- **Traces**: em falha, o span da etapa recebe status `Error` e `error.type` (tipo da exceção), sem mensagem nem stack trace. `/health` não gera trace.
- **Métricas**: `fonte.indexing.duration` (histograma, segundos, com `fonte.indexing.outcome` = `published`, `published_cleanup_failed`, `failed`, `already_running` ou `no_documents`, e `error.type` em falhas); `fonte.indexing.documents` e `fonte.indexing.chunks` (contadores das indexações publicadas). Também são coletadas as métricas automáticas do ASP.NET Core e de `System.Net.Http`.
- **Logs**: estruturados com `ILogger` (`LoggerMessage`): início, conclusão (documentos, chunks, duração), rejeições, falha de limpeza (aviso) e falha (erro, com `Stage` = `configuration`, `read`, `chunk`, `embedding` ou `vectorstore`).
- **Privacidade**: conteúdo de documentos e chunks, embeddings e chaves nunca entram na telemetria. `DocumentPath` fica fora de traces e métricas; pode aparecer em logs de erro, pela mensagem da exceção, para identificar o documento que falhou.

### Recuperação (implementado)

Instrumentação própria no `ChunkRetriever`, no mesmo `ActivitySource` e `Meter` `Fonte.Api` (`RetrievalMetrics`):

- **Traces**: `embedding.create` (com o span HTTP do Gemini abaixo) e `retrieval.search` (tags `fonte.retrieval.top_k` e `fonte.retrieval.results`). Em falha, status `Error` e `error.type`.
- **Métricas**: `fonte.retrieval.duration` (histograma, segundos, com `fonte.retrieval.outcome` = `success` ou `failed`, e `error.type` em falhas).
- **Logs**: falha como erro, com `Stage` = `configuration`, `embedding` ou `search`; sucesso em nível Debug, com a quantidade de resultados e a duração.
- **Privacidade**: a pergunta, o conteúdo dos chunks, vetores, `DocumentPath` e scores não entram em traces nem métricas; a pergunta e o conteúdo não entram nos logs.

### Geração de resposta (implementado)

Instrumentação própria no `AnswerGenerator`, no mesmo `ActivitySource` e `Meter` `Fonte.Api` (`AnswerMetrics`):

- **Traces**: `answer.generate` (com o span HTTP do Gemini abaixo), com `fonte.answer.context_chunks`, `fonte.answer.status`, `gen_ai.request.model` e, quando informados pela API, `gen_ai.usage.input_tokens` e `gen_ai.usage.output_tokens`. Em falha, status `Error` e `error.type`.
- **Métricas**: `fonte.answer.duration` (histograma, segundos, com `fonte.answer.outcome` = `answered`, `insufficient_context`, `no_context` ou `failed`, e `error.type` em falhas).
- **Logs**: falha como erro, com `Stage` = `configuration`, `generation` ou `response`; sucesso em nível Debug, com status, quantidade de trechos e duração.
- **Privacidade**: pergunta, trechos, prompt montado, JSON e texto da resposta não entram em logs, traces nem métricas; `DocumentPath` fica fora de traces e métricas. A instrumentação automática de `IChatClient` do `Microsoft.Extensions.AI` não é usada.

### Perguntas (implementado)

Uma requisição de pergunta é acompanhada assim (o span HTTP é do ASP.NET Core; os demais, dos serviços do Fonte):

```text
POST /questions
│
├── embedding.create
│   └── Gemini
│
├── retrieval.search
│   └── Qdrant
│
└── answer.generate
    └── Gemini
```

### Traces

Instrumentação própria com `ActivitySource`. Deve permitir observar, entre outros:

- duração da geração do embedding;
- duração da busca vetorial;
- duração da geração da resposta;
- duração total da requisição;
- quantidade de chunks recuperados.

### Métricas

Instrumentação própria com `Meter`. Exemplos iniciais:

- documentos indexados;
- chunks indexados;
- perguntas processadas;
- falhas nas integrações externas;
- duração das operações relevantes.

A lista não é exaustiva. Nomes, tipos de instrumento e atributos ainda não foram definidos.

### Logs

Logs estruturados com `ILogger`, integrados à estratégia OpenTelemetry/Grafana.

Conteúdo sensível e o conteúdo integral de perguntas ou documentos **não devem ser enviados automaticamente para a telemetria**.

## Configuração e segredos

Segredos e configurações externas não entram no Git:

- Gemini API key;
- Qdrant URL;
- Qdrant API key;
- credenciais do Grafana/OpenTelemetry.

O repositório pode fornecer exemplos de configuração sem valores sensíveis. O `.gitignore` já exclui arquivos `.env`.

**Segredos em desenvolvimento**: ficam no `Fonte.Api/appsettings.Development.json` local, ignorado pelo Git e excluído do `dotnet publish`. Para criá-lo, copie [appsettings.Development.example.json](../Fonte.Api/appsettings.Development.example.json), que é versionado e mostra só a estrutura, e preencha os valores. Nos demais ambientes, use variáveis de ambiente (ex.: `Gemini__ApiKey`). Vale a precedência padrão do ASP.NET Core: `appsettings.json` → `appsettings.{Ambiente}.json` → variáveis de ambiente → linha de comando, então variáveis de ambiente sobrescrevem os arquivos.

**Risco conhecido**: os testes de integração (`WebApplicationFactory`) rodam no ambiente `Development` e carregam o `appsettings.Development.json` local. Os testes atuais que envolvem Gemini ou Qdrant zeram chave e URL ou injetam fakes; o isolamento completo será tratado antes de ampliar testes de integração que possam alcançar serviços externos.

A pasta de documentos e o tamanho dos chunks ficam na seção `Documents` do [appsettings.json](../Fonte.Api/appsettings.json) (`DocumentsOptions`), validada na inicialização:

| Chave | Padrão | Regra |
|---|---|---|
| `Documents:Path` | `documents` | Obrigatória. Caminho relativo é resolvido a partir do ContentRoot da API. |
| `Documents:MaxChunkSize` | `1000` | Maior que 0. Heurística inicial. |

O Gemini fica na seção `Gemini` (`GeminiOptions`), validada na inicialização, exceto a chave:

| Chave | Padrão | Regra |
|---|---|---|
| `Gemini:ApiKey` | — | Segredo. Em desenvolvimento, no `appsettings.Development.json` local; nos demais ambientes, via variável `Gemini__ApiKey`. Exigida apenas ao gerar embeddings. As variáveis `GEMINI_API_KEY` e `GOOGLE_API_KEY` não são usadas. |
| `Gemini:EmbeddingModel` | `gemini-embedding-2` | Obrigatória. |
| `Gemini:EmbeddingDimensions` | `768` | De 128 a 3072. |
| `Gemini:GenerationModel` | `gemini-3.6-flash` | Obrigatória. Modelo generativo usado nas respostas. |

O Qdrant fica na seção `Qdrant` (`QdrantOptions`), validada na inicialização, exceto os segredos:

| Chave | Padrão | Regra |
|---|---|---|
| `Qdrant:CollectionName` | `fonte-chunks` | Obrigatória. Nome do alias da collection ativa. |
| `Qdrant:Url` | — | Segredo. Endereço gRPC do cluster (porta 6334), no `appsettings.Development.json` local em desenvolvimento ou via `Qdrant__Url` nos demais ambientes. Exigido apenas ao usar o Qdrant. |
| `Qdrant:ApiKey` | — | Segredo, no `appsettings.Development.json` local em desenvolvimento ou via `Qdrant__ApiKey` nos demais ambientes. Exigida apenas ao usar o Qdrant. |

A recuperação fica na seção `Retrieval` (`RetrievalOptions`), validada na inicialização:

| Chave | Padrão | Regra |
|---|---|---|
| `Retrieval:TopK` | `3` | Maior ou igual a 1. Quantidade máxima de chunks retornados por pergunta. |

A telemetria é exportada por OTLP somente quando `OTEL_EXPORTER_OTLP_ENDPOINT` está configurado; sem ele, nada é enviado. As variáveis seguem o padrão do OpenTelemetry e nenhuma entra no repositório:

| Variável | Uso |
|---|---|
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Endpoint base. No Grafana Cloud: `https://otlp-gateway-<região>.grafana.net/otlp` |
| `OTEL_EXPORTER_OTLP_PROTOCOL` | `http/protobuf` para o Grafana Cloud (o padrão do SDK é `grpc`) |
| `OTEL_EXPORTER_OTLP_HEADERS` | Cabeçalho de autenticação gerado no portal do Grafana Cloud (segredo) |

Os valores do Grafana Cloud são gerados no portal (stack → OpenTelemetry → Configure). O serviço é identificado como `fonte`.

As variáveis `OTEL_EXPORTER_OTLP_*` são lidas pelo `IConfiguration` da aplicação (verificado no código-fonte do exportador OTLP), portanto seguem a mesma precedência das demais configurações.

**Em aberto**: confirmar, na primeira conexão real, se `OTEL_SERVICE_NAME` sobrepõe o nome `fonte`.

## Definição de pronto da v1

A v1 está pronta quando for possível:

1. colocar documentos Markdown na pasta configurada;
2. solicitar sua indexação;
3. gerar embeddings usando Gemini;
4. armazenar os chunks no Qdrant;
5. enviar uma pergunta;
6. recuperar semanticamente os chunks relevantes;
7. gerar uma resposta fundamentada nesses chunks;
8. receber junto à resposta as fontes usadas;
9. observar a execução no Grafana por traces, métricas e logs;
10. executar build e testes automaticamente pelo GitHub Actions.

## Fora do escopo da v1

- frontend;
- autenticação e usuários;
- upload de documentos;
- outros formatos além de Markdown;
- histórico de conversas;
- memória;
- agentes;
- MCP;
- múltiplos providers de IA;
- múltiplas estratégias sofisticadas de chunking;
- reranking;
- web search;
- Redis;
- mensageria;
- Docker;
- Kubernetes;
- avaliação automatizada de RAG.
