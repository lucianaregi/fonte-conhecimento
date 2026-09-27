# Plano 007: `POST /questions`

**Status**: implementado e aprovado.

## Objetivo

Expor pela API o fluxo pergunta → recuperação → geração, devolvendo a resposta, o status e as fontes que identificam os trechos usados como contexto.

## Decisões

| Tema | Decisão |
|---|---|
| Orquestração | Sem classe nova: o endpoint coordena `ChunkRetriever.RetrieveAsync` → `AnswerGenerator.GenerateAsync`. Os serviços já cuidam de logs, métricas, spans, resolução da configuração e falhas por etapa; o span HTTP do ASP.NET Core é o pai de `embedding.create`, `retrieval.search` e `answer.generate`. |
| Arquivo | `Fonte.Api/Answering/QuestionEndpoint.cs`, no padrão de `DocumentIndexingEndpoint`: `MapQuestions()`, tipos de request e response e uma função pura de conversão de `GeneratedAnswer` para a resposta HTTP. |
| Request | `{ "question": "..." }`. |
| Validação | `question` obrigatória, não vazia nem só espaços, com no máximo 2000 caracteres (escolha de produto, não imposta pela documentação). Resposta `400` com `ValidationProblemDetails` nativo em `errors.question`, sem repetir a pergunta. Corpo ausente ou JSON malformado: `400` nativo do ASP.NET. |
| Response | `200 OK` com `{ "status", "answer", "sources" }`. `status` = `answered`, `insufficient_context` ou `no_context`. Cada fonte: `number` (numeração `[n]` do prompt), `document` (`DocumentPath`), `chunk` (`ChunkIndex`) e `score`. Sem conteúdo dos trechos. |
| Fontes por status | `answered`: os trechos enviados como contexto, na ordem. `insufficient_context`: `[]` (a resposta não se apoia em trechos). `no_context`: `[]`. |
| Falhas | Configuração ausente, Gemini, Qdrant (inclusive alias inexistente) e respostas inválidas do modelo resultam em `500` padrão do ASP.NET, como em `/documents/index`; cada serviço registra a falha com a etapa. |
| Privacidade | O endpoint não registra nada por conta própria; a pergunta não é repetida em mensagens de validação; as regras dos serviços continuam valendo. |

## Escopo

- Inclui: endpoint, contrato, validação, conversão da resposta, testes, definição técnica e README.
- Não inclui: streaming, histórico de conversa, autenticação, retry, threshold de score, filtros por documento, conteúdo ou excerto nas fontes, erro específico para alias inexistente, mudanças na recuperação ou no prompt.

## Arquivos

- `Fonte.Api/Answering/QuestionEndpoint.cs` (novo).
- `Fonte.Api/Program.cs`: `app.MapQuestions()`.
- `Fonte.Tests/Answering/QuestionEndpointTests.cs` (novo): endpoint e conversão.
- `docs/definicao-tecnica.md`: API, contrato, fluxo de pergunta, estado atual e exemplo do cenário de uso.
- `README.md`: estado atual e exemplo `curl` de pergunta.
- `docs/plans/007-endpoint-perguntas.md` (novo): este plano.

## Passos

1. Gravar este plano.
2. Testes (vistos falhando).
3. Endpoint e registro da rota.
4. Documentação, README e resultado no plano.
5. Build e testes, `scope-check` e `dotnet-code-review`.

## Validação

Sem Gemini ou Qdrant reais.

- Conversão (função pura): os três status mapeados para `answered`, `insufficient_context` e `no_context`; `sources` na ordem de `Context`, com `number` a partir de 1; `sources` vazio em `insufficient_context` e `no_context`.
- Endpoint (`WebApplicationFactory` com `FakeEmbeddingGenerator`, `FakeQdrantGateway` e `FakeChatClient`): 200 `answered` com corpo completo, busca com o `TopK` e trechos recuperados enviados ao modelo; 200 `insufficient_context`; 200 `no_context` sem chamar o modelo; 400 para campo ausente, vazio, só espaços e 2001 caracteres, sem chamadas a Gemini/Qdrant e sem a pergunta no corpo do erro; 400 para corpo ausente e JSON malformado; 500 para falha do Gemini, alias inexistente, resposta inválida do modelo e chave do Gemini ausente; spans `embedding.create`, `retrieval.search` e `answer.generate` sob o span HTTP; logs sem pergunta nem resposta.
- Comando: `dotnet test Fonte.sln`.

## Riscos

- Requisição síncrona de alguns segundos (embedding, busca e geração); desconexão do cliente cancela a operação.
- Antes da primeira indexação, `POST /questions` retorna 500 genérico; a causa aparece só no log (etapa `search`).
- Sem threshold de score, o contexto pode incluir trechos pouco relevantes.

## Em aberto

- Erro específico para índice inexistente.
- Conteúdo ou excerto dos trechos nas fontes.
- Threshold de score.

## Resultado da implementação

Implementado conforme o plano, sem chamadas reais a Gemini ou Qdrant.

- `dotnet build`: sem avisos. `dotnet test Fonte.sln`: 159 testes passando (20 novos), estáveis em três execuções seguidas. Nenhuma dependência nova.
- Os testes foram conferidos com defeitos introduzidos temporariamente, cada um detectado por ao menos um teste: fontes devolvidas em `insufficient_context`, limite de tamanho errado por um caractere, pergunta só com espaços aceita e numeração das fontes a partir de 0. O código foi restaurado em seguida.

### Desvios e detalhes não previstos no plano

- **Verificação dos spans sem aguardar o span do servidor**: com o `TestServer`, o span HTTP do ASP.NET termina depois que a resposta chega ao cliente. Em vez de esperar por ele (espera fixa tornaria o teste frágil), o teste envia um `traceparent` conhecido e confere que `embedding.create`, `retrieval.search` e `answer.generate` estão nesse trace e têm um único pai, diferente do span do cliente, que é o span do servidor.
- **Casos adicionais de validação**: `question` nula e pergunta com exatamente 2000 caracteres (aceita).
