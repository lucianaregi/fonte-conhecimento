---
name: scope-check
description: Confronta as alterações feitas ou propostas com o pedido original e contém o que estiver fora do escopo (scope creep). Use antes de concluir uma implementação, antes de commitar ou ao revisar um diff, sempre que for preciso confirmar que apenas o necessário foi alterado. Para a revisão de qualidade do código, use pr-review.
---

# Scope Check

Verifica se cada alteração é necessária para cumprir o pedido. Em revisão, apenas relata. Durante a própria implementação, o agente desfaz somente o que ele mesmo introduziu fora do escopo.

## O que pertence ao escopo

| Alteração | No escopo? |
| :--- | :--- |
| Código necessário para cumprir o pedido | Sim |
| Testes do comportamento criado ou alterado | Sim |
| Ajustes obrigatórios em chamadores, configuração ou documentação afetados diretamente pela mudança | Sim |
| Correção de bug sem a qual a tarefa não funciona | Sim |
| Bug encontrado por acaso, sem relação com a tarefa | Não: relatar |
| Refatoração, renomeação ou reorganização de código que já funciona | Não: sugerir separadamente |
| Formatação, ordem de imports ou estilo em trechos que a tarefa não tocou | Não |
| Dependência nova ou atualizada que a tarefa não exige | Não |
| Funcionalidade ou opção extra não pedida | Não |

## Procedimento

1. Reler o pedido original. Se ele for ambíguo, registrar a interpretação adotada.
2. Listar as alterações (ex.: `git status`, `git diff`, `git diff --staged` ou o diff do PR).
3. Para cada trecho alterado, perguntar: "sem isto, o pedido deixa de ser atendido?". Se não, o trecho está fora do escopo.
4. Tratar o que estiver fora do escopo:
   - **Trechos introduzidos pelo agente**: desfazê-los editando o próprio trecho. Não usar comandos que descartam ou movem as alterações do arquivo inteiro (`git checkout -- <arquivo>`, `git restore`, `git reset --hard`, `git stash`), porque eles atingem também o trabalho do usuário e os trechos dentro do escopo.
   - **Alterações que o agente não fez** (do usuário ou preexistentes): nunca desfazer. Listá-las e perguntar ao usuário.
   - **Em revisão**: apontar o trecho e recomendar que ele vá para uma tarefa ou PR separado.
5. Registrar como sugestões as melhorias percebidas fora do escopo, sem implementá-las.

## Concluído quando

Todo trecho do diff foi classificado. O que estava fora do escopo foi desfeito (se introduzido pelo agente) ou relatado, e as sugestões separadas foram listadas.
