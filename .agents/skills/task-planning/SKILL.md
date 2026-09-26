---
name: task-planning
description: Elabora, com base na inspeção do código, um plano curto antes da implementação, cobrindo objetivo, decisões, escopo, arquivos afetados, passos, riscos, dúvidas e forma de validação. Em tarefas não triviais, salva o plano aprovado no repositório para que o trabalho não dependa do histórico da conversa. Use quando o usuário pedir um plano ou abordagem, ou antes de tarefas com várias etapas, vários arquivos ou risco de quebra. Não implementa por conta própria.
---

# Task Planning

Planeja; não altera código. O único arquivo que pode criar ou atualizar é o do próprio plano, quando ele for salvo (ver "Plano salvo no repositório"). A implementação só começa depois da aprovação do usuário, ou quando ele já pediu a implementação e o plano não tem dúvidas bloqueantes.

## Regras

- Inspecionar o código antes de listar arquivos. Não inventar caminhos nem componentes; marcar com "(novo)" os arquivos a criar.
- Planejar apenas o que foi pedido. Melhorias percebidas vão como observação separada.
- Tamanho proporcional à tarefa: tarefa pequena, plano de poucas linhas.

## Formato

```markdown
### Plano: <tarefa>

**Objetivo**: <1 a 2 frases: o que muda e por quê>

**Decisões**
- Escolhas técnicas aprovadas e o motivo, quando não for óbvio.

**Escopo**
- Inclui: ...
- Não inclui: ...

**Arquivos**
- `caminho/arquivo`: o que muda
- `caminho/outro-arquivo` (novo): propósito

**Passos**
1. ...

**Riscos**
- Efeitos em outras partes do sistema, breaking changes em contratos ou dados, migrações.

**Em aberto**
- Dúvidas, premissas e decisões ainda não aprovadas, com a premissa adotada ou a pergunta ao usuário.

**Validação**
- Testes a criar ou executar e verificações manuais, com os comandos do projeto.
```

Omitir seções que não se aplicam.

## Plano salvo no repositório

- **Quando salvar**: só em implementações não triviais, quando o plano tiver decisões, várias etapas, riscos ou contexto que precise sobreviver à sessão. Planos pequenos ficam só na conversa.
- **Onde**: seguir a convenção do projeto para local e nome dos planos. Sem convenção, propor um local (ex.: `docs/plans/<tarefa>.md`). Informar o caminho ao apresentar o plano, para que a aprovação do plano cubra também o arquivo.
- **Quando gravar**: depois da aprovação e antes de começar a implementação.
- **Conteúdo**: só o necessário para continuar o trabalho sem o histórico da conversa, no formato acima. O que ainda não foi aprovado fica em **Em aberto**, nunca em **Decisões**.
- **Durante a implementação**: se surgir algo que mude o plano aprovado de forma relevante (abordagem, escopo, arquivos, riscos), parar, propor a atualização do plano e pedir a decisão do usuário. Ajustes de detalhe que não mudam o que foi aprovado podem ser registrados diretamente no plano.
- **Ao concluir**: opcionalmente, registrar no plano o resultado e os desvios relevantes em relação ao que foi aprovado.
- **Limite**: o plano salvo não autoriza ampliar o escopo. O que não estiver em **Decisões** nem na lista "Inclui" do **Escopo** continua precisando de aprovação.

## Concluído quando

O plano foi entregue, com as dúvidas bloqueantes explícitas para o usuário. Se o plano precisar ser salvo, o arquivo foi gravado depois da aprovação.
