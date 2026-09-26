# Plano 001: núcleo local da indexação de Markdown

**Status**: implementado e aprovado.

## Objetivo

Ler os arquivos `.md` de uma pasta configurada e dividi-los em chunks que preservam a origem, sem Gemini, sem Qdrant e sem novos endpoints.

## Fora desta etapa

Gemini, embeddings, Qdrant, `POST /documents/index`, `POST /questions`, OpenTelemetry/Grafana e reindexação.

## Decisões

| Tema | Decisão |
|---|---|
| Chunking | Divisão por parágrafos (blocos separados por linha em branco), agrupando parágrafos consecutivos até `MaxChunkSize`. Parágrafo maior que o limite é quebrado no último espaço antes do limite; sem espaço, no próprio limite. Sem sobreposição. Tamanho medido em caracteres (sem tokenizador, sem dependências). |
| Metadados do chunk | `DocumentPath` (caminho relativo à pasta configurada, preservando subdiretórios, com `/` como separador), `Index` (posição do chunk no documento, a partir de 0) e `Content`. `DocumentPath` + `Index` identificam a origem e correspondem a `document` + `chunkId` da definição técnica. |
| Configuração | Seção `Documents` com `Path` (padrão `documents`) e `MaxChunkSize` (padrão `1000`, heurística inicial). Caminho relativo é resolvido a partir do ContentRoot da API. Validação na inicialização: `Path` obrigatório e `MaxChunkSize` > 0. |
| Leitura | Arquivos `*.md` da pasta configurada e de todos os subdiretórios, em UTF-8, ordenados por `DocumentPath`. Pasta inexistente lança `DirectoryNotFoundException`. Arquivo vazio gera zero chunks. |

## Arquivos afetados

- `Fonte.Api/Indexing/DocumentsOptions.cs` (novo): opções `Path` e `MaxChunkSize` com validação.
- `Fonte.Api/Indexing/MarkdownDocument.cs` (novo): `record` com `Path` e `Content`.
- `Fonte.Api/Indexing/DocumentChunk.cs` (novo): `record` com `DocumentPath`, `Index` e `Content`.
- `Fonte.Api/Indexing/MarkdownDocumentReader.cs` (novo): leitura recursiva assíncrona da pasta, com `CancellationToken`.
- `Fonte.Api/Indexing/MarkdownChunker.cs` (novo): chunking puro.
- `Fonte.Api/Program.cs`: registro das opções (com validação na inicialização) e dos serviços no DI. Nenhuma rota alterada.
- `Fonte.Api/appsettings.json`: seção `Documents` com os padrões.
- `Fonte.Tests/Indexing/MarkdownChunkerTests.cs` (novo).
- `Fonte.Tests/Indexing/MarkdownDocumentReaderTests.cs` (novo).
- `docs/definicao-tecnica.md`: registrar as decisões desta etapa, mantendo o restante em aberto.

## Passos

1. Testes do chunker (vistos falhando), domínio e chunker.
2. Testes do reader e reader.
3. Opções, DI e `appsettings.json`.
4. Atualização da documentação.
5. Build e testes, `scope-check` e `dotnet-code-review` do diff.

## Validação

- Chunker: texto curto gera um chunk; parágrafos agrupados até o limite; parágrafo longo quebrado no último espaço; índices sequenciais; nenhum chunk excede `MaxChunkSize`; texto vazio ou só com espaços gera zero chunks; `DocumentPath` preservado em todos os chunks.
- Reader, em pasta temporária real e isolada por teste: lê apenas `.md`, inclusive em subdiretórios; `DocumentPath` relativo com `/`; conteúdo lido; pasta inexistente gera erro.
- Teste existente de `/health` continua passando.
- Comando: `dotnet test Fonte.sln`.

## Riscos

- A validação na inicialização impede a API de subir com configuração inválida (comportamento desejado). Os padrões são válidos e a pasta não é lida na inicialização, então não precisa existir para a API subir.

## Continua em aberto

- Estratégia de ID dos pontos no Qdrant.
- Embedding associado ao chunk.
- Comportamento da reindexação.
- Criação e versionamento da pasta `documents/`.
- Formato do prompt, critério de contexto insuficiente e contrato de `POST /questions`.

## Resultado da implementação

Implementado conforme o plano, nos arquivos listados. `dotnet build` sem avisos e `dotnet test Fonte.sln` com 18 testes passando (17 novos e o de `/health`). Com `Documents:MaxChunkSize = 0`, a API não sobe e informa o erro de validação.

### Desvios e detalhes não previstos no plano

- **Par substituto (UTF-16)**: quando o corte cai no próprio limite, o chunker não separa um par substituto (ex.: emoji); o corte recua um caractere. Coberto por teste.
- **Extensão sem diferenciar maiúsculas**: `.md` e `.MD` são lidos em qualquer sistema operacional.
- **Arquivos e pastas ocultos ou de sistema** são ignorados, e pastas sem permissão de acesso são puladas (padrão de `EnumerationOptions` do .NET).
- **Testes adicionais**: quebra de linha `\r\n` separa parágrafos e `Path` absoluto é aceito.
