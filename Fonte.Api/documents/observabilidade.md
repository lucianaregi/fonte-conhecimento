# Observabilidade

Observabilidade é a capacidade de entender o que acontece dentro de um sistema a partir dos dados que ele emite enquanto funciona. Em vez de depender apenas de depuração ou de reproduzir problemas localmente, uma aplicação observável produz sinais suficientes para responder perguntas que não foram previstas quando o código foi escrito, como por que uma requisição específica ficou lenta ou qual dependência externa começou a falhar.

## Os três sinais: traces, métricas e logs

A observabilidade costuma se apoiar em três tipos de sinal, que se complementam.

Um trace descreve o caminho de uma operação pelo sistema. Ele é formado por spans, e cada span representa uma unidade de trabalho com início, fim, status e atributos. Os spans se organizam em uma árvore: o span de uma requisição HTTP pode ter como filhos os spans de uma consulta ao banco e de uma chamada a outro serviço. Com essa estrutura é possível ver onde o tempo foi gasto e em qual etapa uma falha aconteceu.

Uma métrica é uma medida numérica agregada ao longo do tempo, como a quantidade de requisições por minuto, a taxa de erros ou a distribuição de latência. Métricas são baratas de armazenar e consultar, por isso servem bem para painéis e alertas. Em troca, elas perdem o detalhe de cada ocorrência individual.

Um log é um registro de um evento que aconteceu em um momento específico. Logs estruturados guardam os dados em campos nomeados, e não apenas em uma frase de texto livre, o que permite filtrar e agrupar eventos com precisão. Quando um log é emitido dentro de um span ativo, ele pode carregar os identificadores do trace, e isso permite ir de uma mensagem de erro direto para a requisição que a originou.

## OpenTelemetry

O OpenTelemetry é um projeto aberto que padroniza a forma de gerar, coletar e exportar traces, métricas e logs. Ele define APIs, convenções semânticas para nomes e atributos e um protocolo de transporte chamado OTLP. Como o formato é neutro, a mesma instrumentação pode enviar dados para diferentes ferramentas de análise sem que o código da aplicação precise mudar.

As convenções semânticas são importantes porque dão nomes consistentes a conceitos comuns. Um atributo como `http.request.method` ou `error.type` tem o mesmo significado em qualquer aplicação que siga o padrão, o que facilita a construção de consultas e painéis reutilizáveis.

## Instrumentação em .NET

No .NET, a instrumentação usa APIs que já fazem parte da plataforma, e o OpenTelemetry atua como coletor e exportador desses dados.

O `ActivitySource` é o ponto de partida para criar traces próprios em uma aplicação .NET. A aplicação cria uma instância com um nome único, normalmente guardada em um campo estático, e chama `StartActivity` para iniciar cada operação que deseja observar. Cada `Activity` retornada corresponde a um span. Quando ninguém está escutando aquela fonte, `StartActivity` devolve nulo e quase não há custo, o que permite manter a instrumentação no código de produção sem prejuízo de desempenho.

Para métricas, o .NET oferece a classe `Meter`, que cria instrumentos como contadores e histogramas. Um contador acumula valores que só crescem, como o número de documentos processados. Um histograma registra a distribuição de valores, como a duração de uma operação, e permite calcular percentis. Os logs são emitidos com a interface `ILogger`, que suporta mensagens estruturadas com parâmetros nomeados.

## Exportação com OTLP

O OTLP é o protocolo do OpenTelemetry para enviar telemetria a um coletor ou diretamente a um serviço de observabilidade. Ele funciona sobre gRPC ou sobre HTTP com mensagens protobuf. A configuração do exportador costuma ser feita por variáveis de ambiente padronizadas, como `OTEL_EXPORTER_OTLP_ENDPOINT`, que indica o endereço de destino, e `OTEL_EXPORTER_OTLP_HEADERS`, que carrega cabeçalhos de autenticação. Assim, o mesmo binário pode enviar dados para destinos diferentes em cada ambiente.

## Propagação de contexto

Para que um trace atravesse vários serviços, o contexto do span atual precisa viajar junto com as chamadas de rede. O padrão W3C Trace Context define o cabeçalho HTTP `traceparent`, que carrega o identificador do trace e o do span de origem. O serviço que recebe a requisição lê esse cabeçalho e cria seus próprios spans como filhos, formando um único trace de ponta a ponta.

## Cuidados com a telemetria

Atributos de métricas precisam ter baixa cardinalidade, ou seja, poucos valores distintos. Usar como atributo um identificador de usuário ou o nome de cada arquivo cria uma série temporal nova para cada valor, o que aumenta o custo e pode tornar as consultas lentas. Valores com muitas variações pertencem aos traces ou aos logs, e não às métricas.

Também é preciso cuidar da privacidade. Telemetria costuma ser armazenada por bastante tempo e acessada por muitas pessoas, por isso não deve conter segredos, chaves de API nem o conteúdo de dados sensíveis. Uma boa prática é registrar quantidades, durações e tipos de erro, e deixar de fora o conteúdo que está sendo processado.
