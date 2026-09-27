# .NET e ASP.NET Core

O .NET é uma plataforma de desenvolvimento gratuita e de código aberto mantida pela Microsoft. Ela reúne um runtime, um conjunto amplo de bibliotecas e ferramentas de linha de comando que permitem criar aplicações para servidores, desktop, dispositivos móveis e nuvem. A linguagem mais usada na plataforma é o C#, uma linguagem orientada a objetos com tipagem estática, recursos funcionais e suporte nativo à programação assíncrona com `async` e `await`.

## Runtime e compilação

O código C# é compilado para uma linguagem intermediária, que o runtime transforma em código de máquina durante a execução por meio do compilador JIT. O runtime também gerencia a memória com um coletor de lixo, o que libera o desenvolvedor de alocar e liberar memória manualmente na maior parte dos casos. Versões com suporte de longo prazo, as chamadas LTS, recebem correções por três anos, enquanto as demais versões têm um ciclo de suporte mais curto.

## ASP.NET Core

O ASP.NET Core é o framework do .NET para construir aplicações web e APIs HTTP. Toda aplicação ASP.NET Core é hospedada por um host que inicializa a configuração, os logs e a injeção de dependência antes de começar a receber requisições. O servidor web padrão é o Kestrel, um servidor multiplataforma e de alto desempenho que roda dentro do próprio processo da aplicação. Em produção, ele pode atender requisições diretamente ou ficar atrás de um proxy reverso.

As requisições atravessam um pipeline de middlewares, componentes encadeados que podem inspecionar, modificar ou encerrar o processamento. Autenticação, roteamento e tratamento de exceções são exemplos de responsabilidades implementadas como middleware. A ordem em que eles são registrados importa, porque cada middleware decide se chama o próximo.

### Minimal APIs

As Minimal APIs permitem declarar endpoints HTTP com pouco código, associando uma rota diretamente a uma função. Os parâmetros dessa função são preenchidos automaticamente a partir da rota, da query string, do corpo da requisição ou do contêiner de injeção de dependência. Esse estilo é adequado para serviços pequenos e focados, em que a estrutura de controllers seria excessiva.

## Injeção de dependência

O ASP.NET Core traz um contêiner de injeção de dependência integrado. Os serviços são registrados com um de três tempos de vida. Um serviço singleton tem uma única instância durante toda a vida da aplicação. Um serviço scoped tem uma instância por requisição HTTP. Um serviço transient é criado novamente a cada vez que é solicitado. Escolher o tempo de vida correto evita problemas como compartilhar estado mutável entre requisições ou manter um objeto de escopo curto preso dentro de um singleton.

## Configuração

A configuração de uma aplicação ASP.NET Core é montada a partir de várias fontes em sequência: o arquivo `appsettings.json`, um arquivo específico do ambiente, como `appsettings.Development.json`, as variáveis de ambiente e os argumentos de linha de comando. Quando a mesma chave aparece em mais de uma fonte, vale a que foi carregada por último, e por isso variáveis de ambiente conseguem sobrescrever valores definidos em arquivos. Seções da configuração podem ser associadas a classes tipadas com o padrão Options, e validadas já na inicialização da aplicação.

## O papel do ASP.NET Core no Fonte

No Fonte, o ASP.NET Core é a base da API HTTP. Ele hospeda os endpoints de verificação de saúde, de indexação de documentos e de perguntas, resolve os serviços do pipeline por injeção de dependência e carrega as configurações de modelos, banco vetorial e telemetria. É também o ASP.NET Core que cria o span de cada requisição HTTP, ao qual se ligam as etapas internas da indexação e da resposta.
