---
name: pr-creation
description: Prepara e cria um pull request (ou merge request) fiel às alterações reais da branch, conferindo origem e destino, commits, diff e PRs existentes, com título e descrição baseados apenas no que mudou. Use quando o usuário pedir para abrir, criar ou preparar um PR ou MR. Para criar commits, use commit; para revisar um PR, use pr-review. Não faz merge.
---

# PR Creation

Cria o pull request na plataforma do repositório. Não altera código, não cria commits e não faz merge.

## Regras

- Não fazer push sem que ele faça parte do pedido ou tenha sido autorizado.
- Não alterar código, commits ou histórico para "melhorar" o PR. Problemas percebidos são informados ao usuário.
- Título e descrição vêm dos commits e do diff. Motivação, contexto, issues relacionadas e decisões só entram se vierem do usuário ou de fontes do projeto (issue, plano, documentação). Não inventar.
- Citar apenas validações efetivamente executadas ou com resultado disponível, com o resultado real. O que não foi verificado é declarado como não verificado.
- Seguir o template de PR e as instruções de contribuição do repositório, quando existirem.
- Não incluir, por iniciativa própria, atribuição, assinatura, crédito ou identificação do agente, nem indicação de que o conteúdo foi gerado por IA. Só incluir se o usuário pedir ou uma regra explícita do projeto exigir.
- Não fazer merge nem habilitar merge automático sem pedido explícito.

## Procedimento

1. **Estado local**: verificar a branch atual e o working tree. Alterações não commitadas não entram no PR: informar o usuário e perguntar se devem ser commitadas antes (skill `commit`, se disponível). Não commitá-las por conta própria.
2. **Origem e destino**: o destino é a branch indicada pelo usuário ou, na falta dela, a branch padrão do repositório. Se a branch atual for o próprio destino ou outra branch principal ou protegida, não criar o PR a partir dela: propor uma branch de trabalho e aguardar.
3. **Conteúdo do PR**: comparar com o destino remoto atualizado (ex.: `git fetch`, `git log <destino>..HEAD`, `git diff <destino>...HEAD`).
   - Sem commits de diferença: parar e informar. Não criar PR vazio.
   - Commits locais que não estão no remoto: fazer push só se autorizado; caso contrário, parar e perguntar.
   - Commits ou arquivos que parecem não pertencer ao pedido: informar antes de criar. Se a skill `scope-check` estiver disponível, aplicá-la apenas para relatar, sem desfazer alterações.
4. **PR existente**: procurar PRs com a mesma branch de origem.
   - Aberto: não criar outro. Informar a URL e perguntar se o título ou a descrição devem ser atualizados.
   - Já integrado ou fechado: informar antes de criar um novo, porque a branch pode estar sendo reutilizada depois do merge e carregar commits já integrados.
5. **Título e descrição**: o título resume a mudança principal e segue a convenção do repositório, se houver. Sem template, usar o formato abaixo.
6. **Criar o PR** com a ferramenta disponível no ambiente.
   - Ferramenta ausente ou sem autenticação: não tentar contornar. Entregar título e descrição prontos e informar o que falta.
   - Falha na criação: relatar o erro. Antes de tentar de novo, verificar se o PR chegou a ser criado, para não duplicá-lo.
7. **Conferir**: consultar o PR criado e confirmar origem, destino e título.

## Formato da descrição

```markdown
## Resumo
<o que muda, em 1 a 3 frases>

## Alterações
- <mudança, agrupada por tema, com os arquivos ou componentes afetados>

## Validação
- Verificado: <comando ou check>: <resultado>
- Não verificado: <o que não foi executado e por quê>

## Contexto
<motivação, issue ou decisões, somente se fornecidas pelo usuário ou pelo projeto>
```

Omitir seções vazias.

## Concluído quando

O PR foi criado e conferido, e o usuário recebeu origem, destino, título e URL. Ou o processo parou, com o motivo informado e, quando for útil, título e descrição prontos para uso.
