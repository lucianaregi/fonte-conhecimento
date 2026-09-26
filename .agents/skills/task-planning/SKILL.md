---
name: task-planning
description: Elabora, com base na inspeção do código, um plano curto antes da implementação, cobrindo objetivo, arquivos afetados, passos, riscos, dúvidas e forma de validação. Use quando o usuário pedir um plano ou abordagem, ou antes de tarefas com várias etapas, vários arquivos ou risco de quebra. Não implementa por conta própria.
---

# Task Planning

Apenas planeja; não altera arquivos. A implementação só começa depois da aprovação do usuário, ou quando ele já pediu a implementação e o plano não tem dúvidas bloqueantes.

## Regras

- Inspecionar o código antes de listar arquivos. Não inventar caminhos nem componentes; marcar com "(novo)" os arquivos a criar.
- Planejar apenas o que foi pedido. Melhorias percebidas vão como observação separada.
- Tamanho proporcional à tarefa: tarefa pequena, plano de poucas linhas.

## Formato

```markdown
### Plano: <tarefa>

**Objetivo**: <1 a 2 frases: o que muda e por quê>

**Arquivos**
- `caminho/arquivo`: o que muda
- `caminho/outro-arquivo` (novo): propósito

**Passos**
1. ...

**Riscos**
- Efeitos em outras partes do sistema, breaking changes em contratos ou dados, migrações.

**Dúvidas e premissas**
- Pontos ambíguos do pedido, com a premissa adotada ou a pergunta ao usuário.

**Validação**
- Testes a criar ou executar e verificações manuais, com os comandos do projeto.
```

Omitir seções que não se aplicam.

## Concluído quando

O plano foi entregue, com as dúvidas bloqueantes explícitas para o usuário.
