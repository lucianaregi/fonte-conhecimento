---
name: pr-review
description: Revisa um pull request ou diff completo antes do merge e emite um parecer com achados classificados por severidade. Use quando o usuário pedir revisão de PR, branch, diff ou conjunto de alterações. Para análises especializadas, prefira ou combine com security-review (segurança), api-review (contratos HTTP) ou dotnet-code-review (C#/.NET).
---

# PR Review

Apenas relata. Não altera código durante a revisão.

## Procedimento

1. **Entender a intenção**: descrição do PR, issue ou pedido original. Sem descrição, inferir a intenção a partir do diff e declarar a inferência.
2. **Obter o diff completo** contra a branch de base (ex.: `git diff <base>...HEAD`) e ler o contexto necessário em volta das mudanças.
3. **Avaliar o que mudou.** Problemas preexistentes só entram se a mudança os agravar.
   - **Correção**: a mudança atende ao pedido? Verificar casos de borda, tratamento de erro, concorrência e regressões em quem usa o código alterado.
   - **Escopo**: há algo além do necessário, como arquivos temporários, código de debug ou código comentado? Se a skill `scope-check` estiver disponível, aplicá-la.
   - **Testes**: o comportamento novo ou alterado está coberto? Os testes detectariam uma regressão?
   - **Segurança**: secrets, entrada externa sem validação, autorização ausente, dados sensíveis expostos. Se a mudança tocar essas áreas e a skill `security-review` estiver disponível, aplicá-la.
   - **Desempenho**: consultas dentro de laços (N+1) e trabalho repetido ou sem limite em caminhos executados com frequência.
   - **Manutenibilidade**: nomes e responsabilidades coerentes com o resto do projeto.
4. Não comentar estilo que o formatador ou o linter do projeto já cobre.

## Severidade

- 🛑 **Bloqueante**: defeito confirmado, vulnerabilidade explorável, perda de dados ou quebra de contrato não intencional. Impede o merge.
- ⚠️ **Importante**: risco real que deve ser corrigido neste PR ou em uma tarefa de acompanhamento registrada. Não impede o merge.
- 💡 **Sugestão**: melhoria opcional.

## Formato da saída

```markdown
## Parecer: Aprovado | Aprovado com ressalvas | Alterações solicitadas

<1 a 3 frases sobre o que a mudança faz e o risco geral>

### 🛑 Bloqueantes
- `caminho/arquivo:linha`: problema. Impacto. Correção sugerida.

### ⚠️ Importantes
- ...

### 💡 Sugestões
- ...
```

- Veredito:
  - pelo menos um bloqueante: "Alterações solicitadas";
  - importantes, sem bloqueantes: "Aprovado com ressalvas";
  - apenas sugestões ou nenhum achado: "Aprovado".
- Omitir seções vazias. Sem achados, dizer isso explicitamente.

## Concluído quando

O parecer foi entregue, cada achado cita arquivo e linha, e o veredito é coerente com as severidades.
