---
name: refactoring
description: Reestrutura código existente sem alterar o comportamento observável, em passos pequenos validados por testes. Use quando o usuário pedir para refatorar, simplificar, extrair, renomear, reorganizar ou eliminar duplicação. Para corrigir bugs, use bug-fix; refatoração não adiciona funcionalidade.
---

# Refactoring

Altera código. O comportamento externo deve ficar idêntico: saídas, efeitos colaterais, contratos públicos e formatos persistidos ou serializados.

## Regras

- Refatorar apenas a área pedida. Oportunidades fora dela viram sugestões.
- Não alterar contratos públicos (APIs, assinaturas exportadas, schemas, formatos serializados) sem pedido explícito.
- Bug encontrado durante a refatoração é relatado, não corrigido: corrigir mudaria o comportamento.
- Não misturar refatoração com funcionalidade nova.

## Procedimento

1. Definir o objetivo concreto (ex.: extrair uma função, eliminar uma duplicação, simplificar uma condicional) e a área afetada.
2. Rodar os testes que cobrem a área e registrar o baseline, incluindo falhas preexistentes.
   - Se a cobertura for insuficiente, propor testes de caracterização antes de alterar o código. Se o esforço for significativo, perguntar ao usuário antes de escrevê-los.
   - Se seguir sem testes, declarar como o comportamento foi preservado (ex.: verificação do compilador, execução manual) e o risco que resta.
3. Alterar em passos pequenos, rodando os testes após cada passo.
4. Validar: nenhuma falha nova em relação ao baseline.

## Concluído quando

O objetivo foi atingido, os testes não têm falhas novas em relação ao baseline e o usuário recebeu um resumo das mudanças, com as limitações da validação.
