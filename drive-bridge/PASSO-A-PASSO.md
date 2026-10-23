# Configurar o envio ao Google Drive — passo a passo

Faça uma vez, no computador de quem tem acesso à pasta da empresa. Cada etapa tem um **teste** para você saber se deu certo antes de seguir.

## Etapa 1 — Colocar o código no script.new
1. Abra https://script.new (com a conta Google da empresa).
2. No TelaRec, clique em **Copiar código do script** (ou abra `drive-bridge/Code.gs`).
3. No editor do Apps Script, clique dentro do código, aperte **Ctrl+A** (seleciona tudo), **Delete**, depois **Ctrl+V**.
4. Ache a linha `const CHAVE = 'TROQUE-POR-UMA-CHAVE-LONGA';` e troque **só o texto entre as aspas** por uma chave sua.
   - Use uma chave longa e **diferente do ID que aparece no URL**. Exemplo: `az2Kw3gAcH728AF9LOZ-nMfCI_th7159`
   - Anote a chave: você vai colar a mesma no TelaRec.
5. Salve com **Ctrl+S**.

✅ Teste: a linha da chave não deve mais conter "TROQUE".

## Etapa 2 — Autorizar o script
1. No topo do editor há um menu com o nome de uma função (ex.: `doGet`). Clique nele e escolha **autorizar**.
2. Clique em **Executar**.
3. Aparece "Autorização necessária" → **Revisar permissões** → escolha sua conta → **Avançado** → **Acessar projeto sem título (não seguro)** → **Permitir**. (O aviso aparece porque o script é seu, não do Google; é normal.)

✅ Teste: no "Registro de execução" aparece "Execução concluída" e "Autorização concluída".

## Etapa 3 — Publicar (Implantar)
1. Botão azul **Implantar** → **Nova implantação**.
2. Clique na engrenagem ao lado de "Selecione o tipo" → **App da Web**.
3. **Executar como: Eu** e **Quem pode acessar: Qualquer pessoa** (a opção "Qualquer pessoa em [empresa]" **não** funciona com o TelaRec).
4. **Implantar** e copie o **URL do app da Web** (termina em `/exec`).

✅ Teste (o mais importante): abra uma **janela anônima** do navegador (Ctrl+Shift+N), cole o URL e aperte Enter. Deve aparecer algo como:
`{"ok":true,"servico":"TelaRec","msg":"Ponte no ar...","chaveConfigurada":true}`

| O que apareceu | O que significa | O que fazer |
|---|---|---|
| Texto com `"ok":true` e `"chaveConfigurada":true` | Tudo certo | Vá para a Etapa 4 |
| `"chaveConfigurada":false` | Você publicou antes de trocar a chave | Faça **Etapa 5** (nova versão) |
| Tela de login do Google | Acesso não está em "Qualquer pessoa" | **Etapa 5** e confira o campo "Quem pode acessar" |
| "Função de script não encontrada" | A publicação tem código antigo/vazio | Confira se colou o código e faça **Etapa 5** |
| "Autorização necessária" | Faltou a Etapa 2 | Refaça a Etapa 2 e depois a **Etapa 5** |

## Etapa 4 — Configurar o TelaRec
Na seção **Google Drive**: cole o **link da pasta**, o **URL do App da Web** e a **chave** (a mesma da Etapa 1) e clique em **Testar conexão**.
✅ Deve ficar verde: *Conectado! Pasta: "..." (conta: ...)*.

## Etapa 5 — Atualizar uma publicação existente (sempre que mudar o código ou a chave)
O Google guarda a publicação como uma "foto" do código. Se você alterar o código depois, precisa tirar uma foto nova:
1. **Implantar** → **Gerenciar implantações**.
2. Ícone do **lápis** (Editar) na implantação ativa.
3. Em **Versão**, escolha **Nova versão**.
4. **Implantar**. O URL continua o mesmo.

## Dicas de segurança
- Quem souber o URL **e** a chave só consegue *enviar* arquivos para a pasta (não lê nem apaga).
- Não publique prints com o URL e a chave. Se vazaram: troque a chave no script, faça a Etapa 5 e atualize o TelaRec.
- Se o TelaRec mostrar "Detalhe técnico: ...", envie esse texto ao suporte: ele diz exatamente o que o Google respondeu.
