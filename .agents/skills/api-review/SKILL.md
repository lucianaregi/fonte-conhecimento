---
name: api-review
description: Revisa contratos de APIs HTTP (rotas, métodos, status codes, validação de entrada, formato de erro) e identifica breaking changes. Use quando o usuário pedir revisão de endpoints, de uma especificação OpenAPI ou de mudanças em contratos de API, ou quando um diff alterar rotas, payloads de requisição ou resposta, ou status codes. Para riscos de segurança da API, use security-review.
---

# API Review

Apenas relata. Não altera código sem pedido explícito.

Seguir as convenções que a API já adota (nomes de rotas, versionamento, formato de erro, escolha entre 400 e 422). Apontar inconsistências internas e violações da semântica HTTP, não preferências de estilo.

## O que verificar

- **Semântica HTTP**: `GET` sem efeitos colaterais; `PUT` e `DELETE` idempotentes. Operações não idempotentes que o cliente pode repetir (ex.: criar um pagamento) precisam de um mecanismo de idempotência.
- **Status codes**: 401 para ausência de autenticação e 403 para falta de permissão. Erro do cliente não devolve 500, e falha não devolve 2xx. Os erros de validação usam o mesmo código em toda a API.
- **Validação de entrada**: tipos, obrigatoriedade, formatos e limites de tamanho (strings, listas, uploads, tamanho de página).
- **Erros**: formato consistente em toda a API (ex.: Problem Details, RFC 9457, se adotado). As mensagens indicam o campo e o motivo, sem expor detalhes internos.
- **Coleções**: listas que podem crescer sem limite são paginadas.
- **Breaking changes**:
  - remover ou renomear campo, rota ou parâmetro;
  - mudar tipo, formato ou significado de um campo;
  - tornar obrigatório um campo de requisição ou adicionar um novo campo obrigatório;
  - restringir a validação (ex.: reduzir um limite);
  - retornar novos valores de enum que clientes existentes podem não tratar;
  - mudar status codes ou o formato de erro.

  Com consumidores externos ou não coordenados, a mudança precisa de versionamento ou de um período de transição, conforme a política do projeto. Com consumidores implantados junto com a API, relatar como risco.

## Severidade

- 🛑 **Bloqueante**: breaking change não intencional, ou comportamento que viola o contrato documentado.
- ⚠️ **Importante**: inconsistência com o resto da API, validação ausente ou status code incorreto.
- 💡 **Sugestão**: melhoria opcional.

## Formato da saída

Para cada achado: severidade, rota ou `arquivo:linha`, problema, impacto para o cliente da API e correção recomendada. Omitir itens que não se aplicam. Sem achados, dizer isso explicitamente.

## Concluído quando

Todas as rotas e alterações de contrato no escopo foram verificadas e o relatório foi entregue.
