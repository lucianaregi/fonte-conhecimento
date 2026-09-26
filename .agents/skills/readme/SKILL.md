---
name: readme
description: Cria ou atualiza o README de um projeto com informações verificadas no próprio repositório, como propósito, pré-requisitos, instalação, execução, testes e uso. Use quando o usuário pedir para escrever, revisar ou atualizar um README. Para documentação de arquitetura, ADRs ou guias técnicos detalhados, use technical-documentation.
---

# README

Altera apenas o README.

## Regras

- **Só o que existe**: extrair comandos e pré-requisitos de fontes do projeto, como manifestos, scripts, `Makefile`, Dockerfile, pipelines de CI e arquivos de versão do runtime. Não supor versões, ferramentas ou serviços.
- **Comandos verificados**: executar os comandos quando for viável e informar ao usuário quais não foram executados.
- **Atualização conservadora**: manter o idioma, o tom e as seções úteis do README existente. Alterar apenas o que está desatualizado ou foi pedido.
- **Tom direto**: sem adjetivos de marketing nem promessas de funcionalidades inexistentes.
- **Sem dados sensíveis**: nenhuma credencial ou valor real em exemplos de configuração.
- **Sem seções vazias ou especulativas**, como um roadmap não pedido.

## Estrutura base

Adaptar ao tipo de projeto: aplicação (como executar), biblioteca (como instalar e usar) ou ferramenta de linha de comando (exemplos de uso).

````markdown
# Nome do projeto

Uma ou duas frases sobre o que o projeto faz e para quem.

## Pré-requisitos

- Runtime ou SDK, com a versão exigida
- Serviços externos necessários (banco de dados, fila), se houver

## Como executar

```bash
<comando real do projeto>
```

## Como testar

```bash
<comando real do projeto>
```

## Estrutura (opcional)

Visão curta dos diretórios principais.
````

## Concluído quando

Todos os comandos e pré-requisitos do README têm origem identificada no repositório, e o usuário sabe quais comandos foram executados e quais não foram.
