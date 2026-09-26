---
name: security-review
description: Analisa código e configuração em busca de riscos de segurança concretos e exploráveis, como secrets expostos, injeção, falhas de autenticação e autorização, exposição de dados e configurações inseguras. Use quando o usuário pedir revisão ou auditoria de segurança, ou quando uma mudança tocar autenticação, autorização, entrada externa, secrets ou dados sensíveis. Para vulnerabilidades em pacotes de terceiros, use dependency-review.
---

# Security Review

Apenas relata. Não corrige sem pedido explícito.

## Princípios

- Reportar riscos com caminho de exploração plausível no código real: de onde vem a entrada, por onde passa e onde é usada.
- Não reportar riscos teóricos sem ligação com o código. Se a exploração depender de algo que não é visível no repositório (proxy, gateway, configuração de ambiente), declarar a premissa.

## O que verificar

- **Secrets**: credenciais, chaves privadas, tokens ou connection strings em código, em configuração versionada ou no histórico do Git. Um secret commitado continua no histórico mesmo depois de removido do arquivo, então a recomendação é **rotacionar a credencial**, não apenas apagá-la.
- **Injeção**: consultas montadas por concatenação em vez de parâmetros; entrada externa chegando a comandos de shell, caminhos de arquivo (path traversal), templates, desserialização ou URLs requisitadas pelo servidor (SSRF).
- **XSS**: saída sem escape adequado ao contexto (HTML, atributo, JavaScript, URL). A defesa principal é o escape automático do mecanismo de templates; sanitização só é necessária quando HTML fornecido pelo usuário é permitido.
- **Autenticação e autorização**: toda operação protegida exige autenticação. O acesso por ID verifica se o recurso pertence ao usuário (IDOR). Papéis e escopos são verificados no servidor, não apenas na interface.
- **Exposição de dados**: dados sensíveis em logs, respostas ou mensagens de erro; stack traces ou detalhes internos devolvidos ao cliente em produção.
- **CORS**: origem da requisição refletida sem lista de permissões junto com `Access-Control-Allow-Credentials: true`. (`*` com credenciais já é bloqueado pelos navegadores.)
- **Transporte e sessão**: TLS na comunicação remota. Cookies de sessão com `Secure`, `HttpOnly` e `SameSite` adequados. Proteção contra CSRF quando a autenticação usa cookies.

## Severidade

- 🛑 **Bloqueante**: vulnerabilidade explorável ou secret exposto. Impede o merge.
- ⚠️ **Importante**: risco real que exige condições adicionais para exploração, ou defesa em profundidade ausente em área sensível.
- 💡 **Sugestão**: endurecimento opcional.

## Formato da saída

Para cada achado:

1. Severidade e `arquivo:linha`.
2. Vulnerabilidade (ex.: SQL Injection, secret exposto).
3. Cenário de exploração: o que um atacante faz e o que obtém.
4. Correção recomendada.

Omitir itens que não se aplicam. Sem achados, dizer isso explicitamente e indicar o que foi verificado.

## Concluído quando

Todas as áreas aplicáveis foram verificadas e o relatório foi entregue no formato acima.
