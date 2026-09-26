# Definição técnica e funcional do Fonte

Este documento é a referência atual do escopo do Fonte. Descreve o que a v1 deve entregar e separa o que já está implementado do que ainda está previsto.

> **Estado atual do código**: o repositório contém a API com `GET /health` ([Fonte.Api/Program.cs](../Fonte.Api/Program.cs)) e o núcleo local da indexação (leitura e chunking de Markdown, em [Fonte.Api/Indexing/](../Fonte.Api/Indexing/)), com testes. Endpoints de indexação e perguntas, integrações, observabilidade e CI ainda **não estão implementados**.

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

Exemplo conceitual de resposta (o contrato final ainda não foi definido):

```json
{
  "answer": "ActivitySource é utilizado para...",
  "sources": [
    {
      "document": "observabilidade.md",
      "chunk": 3,
      "score": 0.89
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
| Embeddings | Modelo de embeddings do Gemini (Gemini API) | Previsto |
| Geração | Modelo generativo do Gemini (Gemini API) | Previsto |
| Busca vetorial | Qdrant Cloud | Previsto |
| Observabilidade | OpenTelemetry, `ActivitySource`, `Meter`, `ILogger`, Grafana Cloud | Previsto |
| CI | GitHub Actions | Previsto |

Os modelos específicos do Gemini (nomes e versões) ainda não foram escolhidos.

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

**Em aberto**:

- a associação do embedding ao chunk e a estratégia de ID dos pontos no Qdrant;
- o comportamento da reindexação (substituir tudo ou atualizar incrementalmente) não foi definido.

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

**Em aberto**: o formato do prompt, o critério para considerar o contexto insuficiente e o contrato exato de requisição e resposta.

## API da v1

A v1 tem três endpoints.

| Método e rota | Função | Situação |
|---|---|---|
| `GET /health` | Health check | Implementado: retorna `200 OK` sem corpo |
| `POST /documents/index` | Indexa ou reindexa os documentos da pasta configurada | Previsto |
| `POST /questions` | Recebe uma pergunta e retorna resposta + fontes | Previsto |

## Observabilidade

Uma requisição de pergunta deve poder ser acompanhada aproximadamente assim:

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

A pasta de documentos e o tamanho dos chunks ficam na seção `Documents` do [appsettings.json](../Fonte.Api/appsettings.json) (`DocumentsOptions`), validada na inicialização:

| Chave | Padrão | Regra |
|---|---|---|
| `Documents:Path` | `documents` | Obrigatória. Caminho relativo é resolvido a partir do ContentRoot da API. |
| `Documents:MaxChunkSize` | `1000` | Maior que 0. Heurística inicial. |

**Em aberto**: o mecanismo de configuração local dos segredos (por exemplo, user secrets ou variáveis de ambiente).

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
