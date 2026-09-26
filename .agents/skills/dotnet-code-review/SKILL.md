---
name: dotnet-code-review
description: Revisa código C#/.NET com foco em defeitos e riscos da plataforma, como async/await, ciclo de vida na injeção de dependência, IDisposable, nullability, exceções, LINQ e EF Core. Use quando o usuário pedir revisão de código C#, .NET ou ASP.NET Core, ou quando o diff em revisão for principalmente C#. Para o parecer geral de um PR, combine com pr-review.
---

# .NET Code Review

Apenas relata. Respeitar as convenções do repositório (estilo, arquitetura, versões da linguagem e do runtime) e não apontar o que os analisadores ou o `.editorconfig` já cobrem.

## O que verificar

### Async
- `async void` fora de event handlers.
- Bloqueio síncrono sobre código assíncrono (`.Result`, `.Wait()`, `GetAwaiter().GetResult()`): risco de deadlock e de esgotamento do thread pool.
- `CancellationToken` recebido e não repassado às chamadas de I/O.
- Tarefa iniciada sem `await` e sem tratamento: as exceções dela se perdem.
- `Task` retornada diretamente, sem `async`/`await`, em método com `using`, `try`/`catch` ou lógica após a chamada. O recurso é descartado cedo demais e as exceções mudam de comportamento. Retornar a `Task` direto só é seguro em repasses simples.

### Injeção de dependência
- Captive dependency: serviço `Scoped` (ex.: `DbContext`) injetado em `Singleton`. `Transient` em `Singleton` só é problema se tiver estado não thread-safe ou dependências `Scoped`.
- `Singleton` com estado mutável compartilhado sem sincronização.
- Serviço novo sem registro ou com tempo de vida inadequado.

### Recursos
- Objeto `IDisposable`/`IAsyncDisposable` criado pelo próprio código e não descartado (`using`/`await using`).
- Instância obtida via DI sendo descartada manualmente; o contêiner é quem gerencia.
- `new HttpClient()` por requisição, que esgota sockets. Usar `IHttpClientFactory` ou uma instância de longa duração.

### Nullability e exceções
- Com nullable reference types habilitado: `!` sem justificativa ou avisos de nulabilidade suprimidos.
- `catch` vazio ou genérico que engole exceções.
- `throw ex;` em vez de `throw;`, o que perde a stack trace original.

### LINQ e EF Core
- `IEnumerable` com execução adiada enumerado mais de uma vez.
- Consultas dentro de laços (N+1), ou `ToList()`/`AsEnumerable()` antes do filtro, trazendo dados demais para a memória.
- Concatenação de strings em laços extensos (usar `StringBuilder`).

## Severidade

- 🛑 **Bloqueante**: defeito confirmado, deadlock, vazamento de recurso ou captive dependency com estado.
- ⚠️ **Importante**: risco operacional ou de desempenho real.
- 💡 **Sugestão**: melhoria opcional.

## Formato da saída

Para cada achado: severidade, `arquivo:linha`, problema, impacto e correção recomendada. Omitir itens que não se aplicam. Sem achados, dizer isso explicitamente.

## Concluído quando

Todo o código C# no escopo foi verificado e o relatório foi entregue.
