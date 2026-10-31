# Enviar gravações ao Google Drive — passo a passo

Faça uma vez, com a conta Google da empresa que tem acesso à pasta. Cada etapa tem um **teste** ("✅") para você saber se deu certo antes de seguir.

> **Dica:** no TelaRec, o botão **?** da seção Google Drive abre um assistente com estes mesmos passos e botões que fazem as ações por você (gerar chave, copiar código, abrir script.new, testar conexão). Este arquivo é a versão por escrito.

## Antes de começar: o que é a "chave"?
A **chave é uma senha que o próprio TelaRec gera para você**. Ela:
- **não vem do Google**;
- **não é o ID da implantação** (aquele texto grande que o Google mostra depois de publicar);
- fica em **dois lugares**: dentro do script (no Google) e no campo "Chave secreta" do TelaRec. Se as duas forem iguais, o envio é liberado.

Você **não precisa digitar nem editar a chave no script**: o botão "Copiar código do script" já coloca a chave dentro do código, na linha que começa com `const CHAVE` (o programa informa o número da linha). Essa é a **única** linha do código que contém a chave; se um dia precisar trocá-la à mão, altere somente o texto entre as aspas dessa linha.

## Etapa 1 — No TelaRec
1. Marque **Enviar as gravações para o Google Drive da empresa**.
2. Cole o **link da pasta** do Drive.
3. Clique em **Gerar nova chave** (o campo "Chave secreta" é preenchido).
4. Clique em **Copiar código do script**.

## Etapa 2 — No script.new
1. Abra https://script.new
2. Clique dentro do código, aperte **Ctrl+A**, **Delete**, depois **Ctrl+V** (cola o código copiado) e **Ctrl+S** (salvar).

✅ Teste: perto do começo do código, a linha `const CHAVE = '...'` deve mostrar uma sequência de letras e números (a sua chave), **não** a frase "TROQUE-POR-UMA-CHAVE-LONGA".

## Etapa 3 — Autorizar
1. No menu no topo do editor (mostra o nome de uma função, ex.: `doGet`), escolha **autorizar**.
2. Clique em **Executar**.
3. Em "Autorização necessária": **Revisar permissões** → sua conta → **Avançado** → **Acessar projeto (não seguro)** → **Permitir**. (O aviso é normal: o script é seu, não do Google.)

✅ Teste: no "Registro de execução" aparece "Execução concluída".

Se você já tinha autorizado uma versão anterior do código, **autorize de novo**: a versão atual também pede permissão para criar arquivos no Drive (sem ela o envio falha com "insufficient authentication scopes").

## Etapa 4 — Publicar
1. **Implantar** → **Nova implantação**.
2. Na engrenagem ao lado de "Selecione o tipo", escolha **App da Web**.
3. **Executar como: Eu** — **Quem pode acessar: Qualquer pessoa** (não use "Qualquer pessoa em [empresa]").
4. **Implantar** e copie o **URL do app da Web** (termina em `/exec`).

✅ Teste (importante): abra uma **janela anônima** (Ctrl+Shift+N), cole o URL e aperte Enter. Deve aparecer algo com `"ok":true`, `"versao":3` e `"chaveConfigurada":true`.

| O que apareceu | O que fazer |
|---|---|
| `"ok":true` e `"chaveConfigurada":true` | Tudo certo, vá para a Etapa 5 |
| `"chaveConfigurada":false` | A publicação foi feita **antes** de a chave estar no código → faça a **Etapa 6** |
| Tela de login do Google | O acesso não está em "Qualquer pessoa" → **Etapa 6**, e confira o campo "Quem pode acessar" |
| "Função de script não encontrada" | A publicação tem código antigo/vazio → confira a Etapa 2 e faça a **Etapa 6** |
| Pedido de autorização | Refaça a Etapa 3 e depois a **Etapa 6** |
| Erro "insufficient authentication scopes" | O script só tem permissão de leitura → refaça a Etapa 3 (função `autorizar`) e a **Etapa 6** |

## Etapa 5 — Testar no TelaRec
Cole o **URL do app da Web** no campo "URL do App da Web" e clique em **Testar conexão**.
✅ Deve ficar verde: *Conectado! Pasta: "..." (conta: ...)*.

## Etapa 5b — Testar o envio de verdade
Clique em **Testar envio**: o programa manda um arquivo de texto pequeno (`TelaRec-teste-….txt`) para a pasta e o remove em seguida.
✅ Deve ficar verde: *Envio de teste OK!*

Pronto! A partir daí, ao finalizar cada gravação o TelaRec pergunta se quer enviar ao Drive e mostra o link do vídeo.

## Etapa 6 — Atualizar uma publicação que já existe
O Google guarda a publicação como uma "foto" do código. Se você mudou o código ou a chave **depois** de publicar, o Google continua usando a foto antiga (por isso aparecia "Troque a CHAVE no script"). Para tirar uma foto nova:
1. **Implantar** → **Gerenciar implantações**.
2. Clique no ícone do **lápis** (Editar).
3. Em **Versão**, escolha **Nova versão**.
4. Clique em **Implantar**. O URL continua o mesmo.

## Escolher a pasta no Drive (a cada gravação)
Ao finalizar a gravação e responder **Sim** à pergunta do Drive, abre o navegador de pastas, sempre na **pasta raiz** que você configurou. Dê dois cliques para abrir uma pasta, use **Voltar** (desativado na raiz) e **Nova pasta** para criar uma. Clique em **Salvar aqui** para salvar na pasta mostrada. Depois escolha o nome do arquivo.

**Trocou a pasta do Drive nas configurações?** O código copiado já libera a pasta que estava configurada na hora da cópia. Se trocar a pasta: **Copiar código do script** → colar no script.new → **Etapa 6** (Nova versão). Sem isso o teste mostra "não é permitida pelo script".

## Dúvidas comuns
**"Criei a implantação antes de colocar a chave. Errei?"** Não. É só fazer a Etapa 6 para publicar a versão com a chave.

**"Posso usar o ID da implantação como chave?"** Não. Ele aparece no URL, então não funciona como senha. Use a chave gerada pelo TelaRec.

**"A mesma chave tem que estar nos dois lugares?"** Sim. Se você clicar em **Gerar nova chave** depois de publicar, copie o código de novo, cole no script.new e faça a Etapa 6.

## Segurança
- Quem souber o URL **e** a chave só consegue *enviar* arquivos para a pasta (não lê nem apaga).
- Não publique prints com o URL e a chave. Se vazaram: **Gerar nova chave** → **Copiar código do script** → colar no script.new → **Etapa 6**.
- Se o TelaRec mostrar "Detalhe técnico: ...", copie esse texto: ele diz exatamente o que o Google respondeu.
