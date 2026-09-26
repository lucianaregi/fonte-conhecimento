---
name: observability-review
description: Revisa logs, métricas e traces no código, cobrindo níveis de log, logging estruturado, contexto de correlação, cardinalidade de métricas e vazamento de dados sensíveis na telemetria. Use quando o usuário pedir revisão de logging ou observabilidade, ou quando um diff adicionar ou alterar logs, métricas ou traces. Para outros riscos de segurança, use security-review.
---

# Observability Review

Apenas relata. Avaliar com base nas bibliotecas e na infraestrutura de telemetria que o projeto já usa; não recomendar ferramentas novas sem pedido.

## O que verificar

- **Níveis de log** (os nomes variam por biblioteca):
  - erro: falha que impede a operação e exige ação;
  - aviso: anomalia contornada (retry, fallback);
  - informação: evento relevante do ciclo de vida ou do negócio;
  - debug/trace: detalhe de diagnóstico, desligado por padrão em produção.

  Problemas comuns: erro esperado (ex.: validação do usuário) registrado como erro; log dentro de laço frequente; a mesma exceção registrada em várias camadas.
- **Logging estruturado**: se a biblioteca suportar, os valores vão em campos nomeados e não interpolados no texto da mensagem, para que possam ser filtrados e agregados.
- **Contexto**: logs de erro identificam o recurso afetado e registram a exceção com stack trace, não só a mensagem. Com chamadas entre serviços ou processamento assíncrono, um identificador de correlação (ex.: trace ID do OpenTelemetry) é propagado.
- **Dados sensíveis**: senhas, tokens, dados de cartão, documentos pessoais (ex.: CPF) e dados de saúde nunca aparecem em logs, atributos de métricas ou spans. Atenção a objetos inteiros serializados no log.
- **Cardinalidade** (se o projeto coleta métricas): labels não usam valores únicos por requisição, como IDs de usuário, GUIDs, timestamps ou URLs com parâmetros.
- **Cobertura de métricas** (se o projeto coleta métricas): operações críticas novas expõem taxa de erro, latência e volume, seguindo o padrão existente.

## Severidade

- 🛑 **Bloqueante**: dado sensível em telemetria ou cardinalidade sem limite em métricas.
- ⚠️ **Importante**: falha sem log ou sem contexto suficiente para diagnóstico, ou nível incorreto que gera ruído ou esconde erros.
- 💡 **Sugestão**: melhoria opcional.

## Formato da saída

Para cada achado: severidade, `arquivo:linha`, problema, impacto no diagnóstico ou na operação e correção recomendada. Omitir itens que não se aplicam. Sem achados, dizer isso explicitamente.

## Concluído quando

Todos os pontos de telemetria no escopo foram verificados e o relatório foi entregue.
