---
name: commit
description: Inspeciona as alterações, faz a validação proporcional à mudança e cria commits Git seguindo a convenção do repositório (sem convenção identificável, Conventional Commits com descrição em pt-BR). Use quando o usuário pedir para commitar ou registrar alterações no Git. Não faz push.
---

# Commit

Cria commits locais. Não altera código: se algo impedir o commit, o agente para e informa.

## Regras

- Não fazer push, amend, rebase nem force sem pedido explícito.
- Não usar `--no-verify`. Se um hook falhar, relatar a falha em vez de contorná-la.
- Não commitar secrets (senhas, tokens, chaves privadas, `.env` com valores reais).
- Não corrigir código para conseguir commitar. Falha de build, teste ou hook interrompe o processo.
- Não incluir, por iniciativa própria, atribuição, assinatura, crédito ou identificação do agente, nem indicação de que o conteúdo foi gerado por IA. Só incluir se o usuário pedir ou uma regra explícita do projeto exigir.
- Preservar a identidade Git configurada. Não deduzir atribuição ou coautoria a partir de commits existentes: exemplos no histórico não são regra do projeto.

## Procedimento

1. **Inspecionar**: `git status`, `git diff` e `git diff --staged`. Separar o que pertence à mudança pedida. Alterações que o usuário não mencionou só entram com confirmação.
2. **Verificar os arquivos**: nada de secrets, arquivos temporários, artefatos de build ou configuração local.
3. **Validar de forma proporcional**:
   - mudança de código: rodar o build e os testes relevantes com os comandos do próprio projeto;
   - mudança apenas em documentação ou texto: dispensar build e testes.

   Se algo falhar, parar e informar o usuário, indicando se a falha parece preexistente ou causada pela alteração.
4. **Identificar a convenção**: consultar `git log --oneline -n 20` e, se existirem, `CONTRIBUTING.md` e a configuração de lint de commits. Seguir o formato e o idioma encontrados. Sem convenção identificável, usar Conventional Commits com a descrição em pt-BR:

   ```text
   <tipo>(<escopo opcional>): <descrição no infinitivo>
   ```

   - Tipos: `feat`, `fix`, `docs`, `test`, `refactor`, `perf`, `build`, `ci`, `chore`, `style`.
   - Mudança incompatível: `!` após o tipo ou escopo e rodapé `BREAKING CHANGE: <descrição>`.
   - Exemplo: `fix(checkout): corrigir cálculo do desconto no fechamento do pedido`.
5. **Commitar**: adicionar os arquivos pelo nome (`git add <arquivos>`), sem `git add .` ou `-A`. Um commit por mudança lógica; se o diff misturar mudanças independentes, propor a divisão.

## Concluído quando

O commit foi criado e confirmado com `git log -1`, e o hash e a mensagem foram informados. Ou o processo parou, com o motivo informado ao usuário.
