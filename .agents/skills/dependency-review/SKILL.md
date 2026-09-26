---
name: dependency-review
description: Avalia a adição, atualização ou remoção de dependências de terceiros, considerando necessidade, manutenção, licença, vulnerabilidades, impacto na árvore de dependências e breaking changes. Use quando o usuário pedir para adicionar, atualizar ou remover uma biblioteca ou pacote, ou quando um diff alterar manifestos ou lockfiles de dependências.
---

# Dependency Review

Avalia e recomenda. Só aplica a mudança quando o usuário a pediu e a recomendação for prosseguir. Nos demais casos, e sempre que houver ponto marcado para decisão humana, apresentar a recomendação e aguardar. Itens não verificados não impedem a aplicação, mas precisam ser informados.

## Regra de verificação

Manutenção, licença, versões e vulnerabilidades devem vir de uma fonte consultada: registro de pacotes, repositório do projeto, arquivo de licença ou ferramenta de auditoria do ecossistema (ex.: `npm audit`, `pip-audit`, `osv-scanner`). Não afirmar nada disso de memória. Se não for possível consultar, marcar o item como **não verificado**.

## Adição

1. **Necessidade**: a biblioteca padrão, o framework ou uma dependência já presente resolve? Um código próprio curto e de baixo risco é preferível a uma dependência nova. Exceção: domínios com armadilhas conhecidas, como criptografia, parsing de formatos complexos, datas e fusos horários. Neles, preferir uma biblioteca consolidada.
2. **Manutenção**: releases recentes, issues respondidas, compatibilidade com a versão da plataforma usada no projeto.
3. **Licença**: compatível com a licença e o modelo de distribuição do projeto. Em caso de dúvida (ex.: copyleft em software distribuído), sinalizar para decisão humana.
4. **Vulnerabilidades** conhecidas na versão escolhida.
5. **Impacto**: dependências transitivas que entram junto e tamanho do artefato, quando isso importa (ex.: frontend, mobile).

## Atualização

1. Ler o changelog ou as release notes entre a versão atual e a versão alvo, identificando breaking changes e depreciações.
2. Verificar licença e vulnerabilidades da versão alvo. A licença pode mudar entre versões.
3. Em salto de versão major, localizar no código os usos afetados antes de atualizar. Se a atualização exigir adaptações de código significativas, apresentá-las antes de aplicar.
4. Revisar o diff do lockfile: dependências transitivas adicionadas, removidas ou com mudança de major.
5. Depois de atualizar, rodar build e testes relevantes e confirmar que não há falhas novas em relação ao estado anterior.

## Remoção

1. Procurar usos restantes em código, configuração, scripts e documentação, incluindo usos indiretos (plugins carregados por configuração, CLIs chamadas em scripts). Se houver usos, listá-los e só removê-los se isso fizer parte do pedido.
2. Remover pelo gerenciador de pacotes do ecossistema, que atualiza o lockfile. Não editar o lockfile à mão.
3. Rodar build e testes relevantes e confirmar que não há falhas novas.

## Formato da saída

- **Recomendação**: prosseguir, não prosseguir ou usar uma alternativa (indicar qual).
- **Justificativa** por critério, em poucas linhas, com os itens não verificados marcados.
- **Ações necessárias**: ajustes de código, migração, pontos de atenção.

## Concluído quando

A recomendação foi entregue. Se a mudança foi aplicada, manifesto e lockfile estão consistentes e a validação não tem falhas novas.
