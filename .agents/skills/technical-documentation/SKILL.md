---
name: technical-documentation
description: Produz documentação técnica fiel ao código, como visão de arquitetura, fluxos entre componentes, registros de decisão (ADRs) e guias de integração ou de desenvolvimento. Use quando o usuário pedir para documentar a arquitetura, um componente, um fluxo, uma integração ou uma decisão técnica. Para o README do projeto, use readme.
---

# Technical Documentation

Cria ou altera arquivos de documentação. Não altera código.

## Regras

- **Verificável no repositório**: toda afirmação sobre fluxo, contrato, persistência ou arquitetura é confirmada no código ou na configuração versionada. Citar caminhos de arquivos e nomes de símbolos em vez de copiar trechos longos de código.
- **Premissas declaradas**: o que depende de algo fora do repositório (infraestrutura, serviços externos, configuração de ambiente) é marcado explicitamente. Exemplo:

  > ⚠️ **Não verificado no código**: o retry em produção depende da configuração do gateway, que não está versionada neste repositório.
- **Motivos não se inferem do código**: a razão de uma decisão vem do usuário ou de fontes como issues, PRs e documentos existentes. Não inventá-la.
- **Autoria**: não incluir, por iniciativa própria, atribuição, assinatura, crédito ou identificação do agente, nem indicação de que o conteúdo foi gerado por IA. Só incluir se o usuário pedir ou uma regra explícita do projeto exigir.
- **Local e formato existentes**: salvar onde o projeto já mantém documentação (ex.: `docs/`, `docs/adr/`) e seguir os modelos que já existem.
- **Concisão**: preferir listas e tabelas curtas. Usar diagramas só quando esclarecem um fluxo; Mermaid apenas se o destino o renderiza.

## Documento técnico

1. **Contexto**: o problema que o componente ou a arquitetura resolve.
2. **Componentes e responsabilidades.**
3. **Fluxos principais.**
4. **Premissas não verificadas.**

## ADR

Sem modelo no projeto, usar:

- **Título**: `ADR-NNN: <decisão>`
- **Status**: proposta, aceita ou substituída (indicar por qual ADR)
- **Data**
- **Contexto**: problema e alternativas consideradas
- **Decisão**
- **Consequências**: ganhos e custos aceitos

## Concluído quando

O documento foi salvo, toda afirmação está verificada ou marcada como não verificada, e o usuário recebeu a lista das premissas pendentes de confirmação.
