---
name: testing
description: Cria e revisa testes automatizados focados em comportamento observável, determinísticos e pouco frágeis. Use quando o usuário pedir para escrever, melhorar, corrigir ou revisar testes, aumentar a cobertura ou investigar testes instáveis (flaky). Para reproduzir um bug com teste antes de corrigi-lo, use bug-fix.
---

# Testing

Cria ou altera testes. Não altera código de produção sem pedido; se o código não for testável como está, explicar o motivo e perguntar antes.

## Princípios

- **Comportamento, não implementação**: verificar entradas, saídas e efeitos colaterais do contrato. Uma mudança interna que mantém a regra não deve quebrar o teste.
- **Dublês só onde necessário**: substituir (mocks, stubs, fakes) o que é externo, lento ou não determinístico, como rede, serviços de terceiros, relógio e e-mail. Para lógica de domínio e objetos de valor, usar instâncias reais. Em testes de integração, seguir a prática do projeto (banco real, em contêiner ou em memória).
- **Determinismo**: não depender da ordem de execução, do estado deixado por outro teste nem de relógio, fuso ou aleatoriedade sem controle.
- **Asserções específicas**: verificar valores, não apenas "não é nulo" ou "não lançou exceção".

## Estrutura

- Separar preparação, ação e verificação (Arrange-Act-Assert), com uma ação principal por teste.
- Seguir o framework, a organização e a convenção de nomes já usados no projeto. Sem convenção, o nome deve indicar o cenário e o resultado esperado.
- Para vários casos da mesma regra, preferir testes parametrizados a laços ou condicionais dentro do teste.

## Evitar

- Testar métodos privados diretamente ou tornar algo público só para testar.
- Verificar a sequência de chamadas internas que não fazem parte do contrato.
- Criar mocks de DTOs, estruturas de dados simples ou do próprio objeto sob teste.
- Resolver testes instáveis com retry ou espera fixa. Identificar a causa antes (tempo, ordem, estado compartilhado, concorrência).

## Concluído quando

- Os testes novos ou alterados executam e passam.
- Cada asserção falharia se o comportamento protegido estivesse errado: um teste que passa com qualquer implementação não protege nada. Se o teste foi escrito antes da implementação, ele foi visto falhando primeiro.
- A suíte relevante foi executada. Falhas fora dos testes criados ou alterados são relatadas, não corrigidas.
