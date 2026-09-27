# RAG: geração aumentada por recuperação

RAG, sigla em inglês para geração aumentada por recuperação, é uma técnica que combina um modelo de linguagem com uma base de conhecimento externa. Em vez de responder apenas com o que aprendeu durante o treinamento, o modelo recebe trechos relevantes de documentos junto com a pergunta e usa esse material como contexto para formular a resposta. Isso permite trabalhar com informações privadas ou mais recentes do que o treinamento do modelo, sem precisar treiná-lo novamente.

## Por que usar RAG

Modelos de linguagem podem produzir respostas plausíveis, mas incorretas, quando não conhecem o assunto. Ao fornecer trechos concretos dos documentos, o RAG ancora a resposta em fontes verificáveis. Uma vantagem adicional é a transparência: o sistema pode informar quais documentos foram usados, e quem lê a resposta consegue conferir a origem de cada afirmação.

## Indexação

Antes de responder perguntas, os documentos precisam ser preparados. Na etapa de indexação, cada documento é dividido em trechos menores, chamados chunks. Trechos pequenos são mais específicos e cabem com folga no limite de entrada dos modelos; trechos grandes preservam mais contexto, mas tendem a misturar assuntos. Uma estratégia simples é dividir o texto por parágrafos e agrupar parágrafos vizinhos até um tamanho máximo.

Cada chunk é então convertido em um embedding e armazenado em um banco de dados vetorial, junto com metadados que identificam sua origem, como o documento e a posição do trecho. Esses metadados são o que permite, mais tarde, citar as fontes da resposta.

## Embeddings e busca semântica

Um embedding é um vetor de números que representa o significado de um texto. Modelos de embedding são treinados para que textos com sentido parecido produzam vetores próximos entre si, mesmo quando usam palavras diferentes. Por isso embeddings são usados na busca semântica: uma pergunta sobre "reduzir o tempo de resposta de uma API" pode encontrar um trecho que fala em "latência", algo que uma busca apenas por palavras-chave não faria.

A proximidade entre vetores costuma ser medida pela similaridade de cosseno, que compara a direção dos vetores e ignora sua magnitude. O resultado varia de -1 a 1, e quanto mais perto de 1, mais parecido é o significado dos dois textos. A pergunta e os documentos precisam ser convertidos pelo mesmo modelo de embedding, porque vetores de modelos diferentes não são comparáveis.

## Recuperação

A recuperação é a etapa em que o sistema encontra os trechos mais úteis para responder a uma pergunta. Primeiro, a pergunta é convertida em um embedding. Em seguida, esse vetor é comparado com os vetores armazenados no banco vetorial, que devolve os trechos mais próximos, geralmente os primeiros k resultados ordenados pela similaridade. O valor de k é um ajuste importante: poucos trechos podem deixar de fora informação necessária, e muitos trechos trazem ruído e aumentam o custo da geração.

Bancos vetoriais usam índices especializados para fazer essa busca rapidamente mesmo com milhões de vetores, aceitando uma pequena imprecisão em troca de velocidade. Também é comum combinar a busca por similaridade com filtros sobre os metadados, por exemplo para restringir a busca a um conjunto de documentos.

## Geração

Na geração, os trechos recuperados são inseridos no prompt enviado ao modelo de linguagem, junto com a pergunta e com instruções sobre como responder. Uma instrução essencial é pedir que o modelo use apenas o contexto fornecido e diga explicitamente quando esse contexto não é suficiente. Sem essa orientação, o modelo tende a preencher lacunas com conhecimento geral, e a resposta deixa de estar fundamentada nos documentos.

Ao final, o sistema devolve a resposta acompanhada das fontes utilizadas, normalmente com o nome do documento, a posição do trecho e a pontuação de similaridade de cada um.

## Limitações

A qualidade de um sistema RAG depende diretamente da recuperação. Se os trechos certos não forem encontrados, nem o melhor modelo de linguagem conseguirá produzir uma boa resposta. Por isso decisões aparentemente simples, como o tamanho dos chunks, o modelo de embedding e o valor de k, têm grande impacto no resultado e costumam ser ajustadas a partir de perguntas reais.
