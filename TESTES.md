# Testes e validação

## Automatizados (rodam no GitHub Actions, etapa "Testes unitários")
Cobrem apenas lógica pura: argumentos do FFmpeg (índices DXGI, coordenadas negativas, sem escalonamento, MP4 fragmentado), nomes de arquivo sem sobrescrita, formatação de tempo, configurações e comparação de geometria de monitor. A etapa é *informativa* (não bloqueia o executável). **Compilar com sucesso não prova que a gravação funciona.**

## Manuais (nenhum foi executado ainda)
Marque conforme for validando. Para cada item anote Windows, GPU e resultado.

| # | Teste | Resultado |
|---|---|---|
| 1 | Abrir o .exe em Windows 10 e em Windows 11 limpos (sem ferramentas de desenvolvimento) | ☐ |
| 2 | 1 monitor: aparece "Monitor 1" com resolução correta | ☐ |
| 3 | 2 monitores: ambos aparecem, disposição igual às configurações do Windows | ☐ |
| 4 | 3+ monitores: todos listados | ☐ |
| 5 | Gravar o Monitor 2 com 3 telas: vídeo contém **só** o Monitor 2 | ☐ |
| 6 | Monitores com resoluções diferentes | ☐ |
| 7 | Monitor à esquerda/acima do principal (coordenadas negativas) | ☐ |
| 8 | Escalas de DPI diferentes (100%/125%/150%) em monitores distintos | ☐ |
| 9 | Monitor girado (retrato) | ☐ |
| 10 | Iniciar → pausar → retomar → finalizar; duração do vídeo ≈ tempo ativo (sem o tempo pausado) | ☐ |
| 11 | MP4 abre em Windows Media Player/Filmes e TV, VLC e navegador; permite avançar/retroceder | ☐ |
| 12 | Resolução e proporção do vídeo = do monitor (ver propriedades; ímpar perde no máx. 1 px) | ☐ |
| 13 | Texto pequeno legível nas 3 qualidades; medir MB em 5 e 10 min (meta 10–20 / 20–40 MB) | ☐ |
| 14 | Gravação ≥ 2 h: memória estável, CPU baixa, arquivo íntegro | ☐ |
| 15 | Disco quase cheio: aviso aos 3 GB, finalização aos 1 GB, arquivo reproduzível | ☐ |
| 16 | Pasta sem permissão / pasta inexistente: mensagem clara | ☐ |
| 17 | Desconectar o monitor gravado durante a gravação: finaliza com segurança e avisa | ☐ |
| 18 | Alterar resolução do monitor gravado durante a gravação | ☐ |
| 19 | Matar o ffmpeg.exe pelo Gerenciador de Tarefas durante a gravação: mensagem e trecho salvo | ☐ |
| 20 | Fechar a janela durante a gravação: pergunta e salva | ☐ |
| 21 | Janela do TelaRec **não** aparece no vídeo (opção ativada) | ☐ |
| 22 | NVIDIA / Intel / AMD: encoder de hardware detectado e vídeo válido; PC sem GPU: cai para x264 | ☐ |
| 23 | Sem internet: tudo funciona; nenhum tráfego de rede do app | ☐ |
| 24 | Após fechar, nenhum `ffmpeg.exe` ou `TelaRec.exe` fica no Gerenciador de Tarefas | ☐ |
| 25 | Nome de arquivo repetido não sobrescreve | ☐ |
| 26 | Efeito de clique: círculo AZUL no clique esquerdo e LARANJA no direito, no ponto exato, visíveis no vídeo gravado | ☐ |
| 27 | Efeito de clique em monitor secundário, com escala de DPI ≠ 100% e com coordenadas negativas: círculo no lugar certo | ☐ |
| 28 | Sons diferentes para esquerdo/direito; sem efeito durante pausa; clique em outro monitor não gera efeito | ☐ |
| 29 | Efeito de clique desligado: nenhum hook/janela extra; mouse sem atraso perceptível com o efeito ligado | ☐ |
| 30 | Som do computador: gravar vídeo do YouTube; o MP4 tem áudio audível e sem estalos | ☐ |
| 31 | Som do computador em silêncio por um tempo e depois voltando: áudio continua sincronizado até o fim (vídeo de 10+ min) | ☐ |
| 32 | Microfone: lista mostra todos os microfones; troca de microfone funciona; "Detectar microfones" vê um USB recém-conectado | ☐ |
| 33 | Teste do microfone: barra reage à voz; captação 0–300% muda o nível; aviso aparece se sem sinal | ☐ |
| 34 | Gravar som do PC + microfone juntos: as duas vozes aparecem no mesmo vídeo, sem eco/duplicação | ☐ |
| 35 | Redução de ruído (Leve/Média/Forte): chiado cai nas pausas e a voz não é cortada | ☐ |
| 36 | Sincronia áudio/imagem (clicar num vídeo/YouTube e ver quando o som começa; ou bater palma): diferença < ~100 ms com o ajuste em 0; se não, usar "Ajuste fino da sincronia" | ☐ |
| 37 | Pausar/retomar com áudio: sem áudio durante a pausa, sincronia mantida após retomar | ☐ |
| 38 | Microfone bloqueado nas permissões do Windows: aviso claro e gravação segue sem o microfone | ☐ |
| 39 | Trocar dispositivo de saída/fones durante a gravação (comportamento documentado) | ☐ |
| 40 | Finalizar com áudio: MP4 fecha corretamente (exit 0), abre em VLC, Windows Media Player e navegador | ☐ |
| 41 | Google Drive: criar o script, implantar como "Qualquer pessoa" e clicar em "Testar conexão": mostra o nome da pasta e a conta | ☐ |
| 42 | Testar com a chave errada e com URL de implantação restrita à empresa: mensagens claras | ☐ |
| 43 | Finalizar gravação: aparece a pergunta "Enviar para o Drive?"; "Não" mantém o vídeo local; botão "Enviar ao Drive" permite enviar depois | ☐ |
| 44 | Envio de vídeo pequeno (10 MB) e grande (>1 GB): barra de progresso, velocidade e tempo restante; arquivo no Drive abre e tem o mesmo tamanho | ☐ |
| 45 | Janela final: link correto, "Copiar link" funciona (colar no DevOps), "Abrir no navegador" abre o vídeo | ☐ |
| 46 | Outra pessoa do grupo da empresa consegue abrir o link; pessoa fora do grupo não | ☐ |
| 47 | Derrubar a internet no meio do envio: retoma sozinho (ou oferece "tentar novamente") sem recomeçar do zero | ☐ |
| 48 | Cancelar o envio: nenhum arquivo parcial fica no Drive | ☐ |
| 49 | Pasta em Drive compartilhado (Shared Drive) e em "Meu Drive" | ☐ |
| 50 | "Enviar automaticamente" ligado: envia sem perguntar; chave não aparece em texto puro no settings.json | ☐ |
| 51 | "Testar conexão" com script só-leitura (sem rodar "autorizar" de novo): avisa que falta permissão de criar arquivos | ☐ |
| 52 | "Testar envio": arquivo TelaRec-teste-….txt chega à pasta e é removido; resultado verde | ☐ |
| 53 | Depois do teste verde, enviar um vídeo real: sobe, abre pelo link e o tamanho confere | ☐ |
| 54 | Gravar 3 s com som do computador, depois com microfone, depois com os dois: o MP4 sai com vídeo do tamanho certo em todos | ☐ |
| 55 | Provocar falha de áudio (desabilitar o dispositivo de saída/mic no Windows): aparece aviso, o vídeo é salvo (com ou sem áudio) e "Abrir relatório" abre o log | ☐ |
| 56 | Clicar em "Iniciar gravação": o botão muda na hora para "Iniciando…" e o cronômetro começa em 00:00:00 e conta sem "pular" | ☐ |
| 57 | Pausar: cronômetro congela e a tela muda na hora; Retomar: mostra "Retomando…" e volta a contar a partir do valor anterior | ☐ |
| 58 | Pausar e retomar várias vezes rápido, e finalizar logo depois de pausar: o vídeo final tem todos os trechos e abre normalmente | ☐ |
| 59 | Microfone "Gravar 5 s e ouvir": falar uma frase; o resultado mostra sinal contínuo e a reprodução soa inteira; repetir com cada microfone da lista | ☐ |
| 60 | Gravar com microfone (e com som do PC) falando frases longas: o áudio do MP4 sai inteiro, sem cortes | ☐ |
| 61 | Em caso de áudio picado, "Abrir relatório" mostra o resumo do áudio (buffer do microfone, bloqueio de escrita) | ☐ |
| 62 | Finalizar: aparece a janela de nome com o padrão preenchido e ".mp4" fixo; só OK mantém o nome padrão | ☐ |
| 63 | Alterar o nome e clicar OK: o arquivo é renomeado, a mensagem e os botões (Abrir pasta/Reproduzir) usam o novo nome | ☐ |
| 64 | Digitar caracteres inválidos (\ / : * ? " < > \|), nome vazio, "CON", nome com ".mp4": tratados sem erro | ☐ |
| 65 | Escolher um nome que já existe na pasta: não sobrescreve, acrescenta (2) e avisa | ☐ |
| 66 | Depois da janela de nome, a pergunta do Drive aparece e o arquivo enviado tem o nome final | ☐ |
| 67 | Atualizar o script para a v5 (colar o código novo e criar Nova versão): "Testar conexão" mostra "script v5" | ☐ |
| 68 | Gravar → Sim ao Drive → o navegador de pastas abre na raiz e lista as subpastas em ordem alfabética | ☐ |
| 69 | Abrir subpastas com duplo clique, Voltar até a raiz: na raiz o botão Voltar fica desativado | ☐ |
| 70 | Nova pasta "505": é criada, o programa entra nela, "Salvar aqui" + nome do arquivo + envio: o vídeo cai em RAIZ/505 | ☐ |
| 71 | Criar uma pasta com nome que já existe: entra na existente, sem duplicar | ☐ |
| 72 | Cancelar envio na escolha de pasta: vídeo salvo só no computador, e a janela de nome ainda aparece | ☐ |
| 73 | Modo "enviar automaticamente": vai para a raiz sem abrir o navegador de pastas | ☐ |
| 74 | Trocar a pasta nas configurações sem recopiar o script: erro claro "não é permitida pelo script" com instrução | ☐ |
| 75 | Botão "Enviar ao Drive" (depois da gravação) também abre o navegador de pastas | ☐ |
| 76 | Atualizar o script para a v6: "Testar conexão" mostra "script v6" | ☐ |
| 77 | Janela de pastas abre larga o bastante: ao clicar em "Nova pasta" os botões Criar e Cancelar aparecem inteiros | ☐ |
| 78 | Redimensionar a janela de pastas, fechar e abrir de novo: abre no tamanho escolhido (também após fechar o programa) | ☐ |
| 79 | Ordenar por Nome / Mais recentes / Mais antigas: a lista reordena na hora e a escolha é lembrada | ☐ |
| 80 | Pesquisar "505": o trecho aparece destacado em cor, a pasta é selecionada/rolada na lista, Enter vai para a próxima, Esc limpa; a lista NÃO é filtrada e nada é aberto | ☐ |
| 81 | Pasta inexistente na pesquisa: mensagem "Nenhuma pasta encontrada…"; testar com acentos e maiúsculas/minúsculas | ☐ |
