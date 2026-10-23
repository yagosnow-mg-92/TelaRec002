# TelaRec — Gravador de tela portátil para Windows 10/11

Gravador de tela gratuito, leve e sem limite artificial de duração. Grava **um monitor por vez** (qualquer um dos detectados pelo Windows) e gera vídeos **MP4 (H.264)**. Sem instalação, sem anúncios, sem telemetria, funciona offline.

> **Estado honesto do projeto (v1.0.0):** todo o código foi escrito sem acesso a um Windows nem a um compilador .NET. **Nada foi compilado nem executado ainda.** O primeiro passo é rodar o GitHub Actions; se aparecer erro de compilação, envie o log para correção. A validação com monitores reais está em [`TESTES.md`](TESTES.md).

## Como obter o executável (sem instalar nada no seu PC)

1. Crie um repositório no GitHub e envie **todo o conteúdo** desta pasta (incluindo a pasta oculta `.github`).
2. Abra a aba **Actions**. O workflow *Compilar TelaRec (Windows)* roda sozinho a cada push na branch `main` (ou clique em **Run workflow** para iniciar manualmente).
3. Ao terminar (alguns minutos), baixe `TelaRec.exe` em duas opções:
   - **Actions → execução → Artifacts → `TelaRec-win-x64`**; ou
   - **Releases** (cada execução cria uma; marcadas como *pre-release* quando vêm de `main`).
4. Para gerar uma versão numerada: crie uma tag, por exemplo `v1.0.0` (GitHub → Releases → *Draft a new release* → nova tag). O workflow publica uma Release normal.

O executável tem ~60–80 MB porque embute o .NET e o FFmpeg. Na primeira execução ele extrai o FFmpeg para `%LOCALAPPDATA%\TelaRec\bin` (sem administrador).

## Como usar

1. Execute `TelaRec.exe`.
2. Clique no monitor desejado no mapa de telas (mesma disposição das configurações de vídeo do Windows, com miniatura).
3. Escolha pasta, nome (opcional), FPS e qualidade.
4. **Iniciar gravação** → **Pausar/Retomar** → **Finalizar**. O tempo em pausa não entra no vídeo.

## Áudio (opcional)

Em **Áudio**, duas opções independentes (ambas vêm desligadas):

- **Gravar o som do computador** — captura a saída de áudio (tudo que toca nas caixas/fones: vídeos, reuniões…) via WASAPI loopback, com controle de volume.
- **Gravar o microfone** — ao marcar, o programa **detecta todos os microfones** e mostra uma lista para escolher (botão *Detectar microfones* atualiza). Há **teste com barra de nível ao vivo**, controle de **captação (0–300%)** e **Reduzir ruído** (Leve/Média/Forte).

As duas fontes são mixadas em uma só faixa AAC (160 kbps, 48 kHz estéreo). Com o som do computador ligado, o som do clique do "efeito de clique" também entra no vídeo.

**Redução de ruído:** é um filtro de graves + *porta de ruído adaptativa* — mede o ruído de fundo e o atenua quando você não está falando. Não remove ruído *enquanto* você fala (isso exigiria um modelo de IA, fora do escopo da v1).

**Sincronia:** o áudio chega ao FFmpeg por um pipe, que não carrega relógio próprio. Por isso o programa (1) reduz a análise inicial da entrada de áudio (sem isso o FFmpeg demorava ~1 s para iniciar o vídeo e o som saía atrasado) e (2) só começa a enviar o áudio real quando o **primeiro quadro de vídeo** aparece (detectado pelo filtro `showinfo`). Assim o som já nasce alinhado com a imagem, sem depender de uma estimativa. Há ainda um **Ajuste fino da sincronia (ms)** (padrão 0; positivo atrasa o som, negativo adianta) para eventuais diferenças do seu equipamento. A sincronia em hardware real ainda precisa ser confirmada.

**Permissão do microfone:** se a barra de teste não mexer, confira *Configurações do Windows → Privacidade e segurança → Microfone* e permita que **aplicativos de desktop** usem o microfone.

## Envio ao Google Drive (opcional)

Ao finalizar uma gravação, o programa pode **enviar o MP4 (sem compactar) para uma pasta do Google Drive da empresa** e mostrar uma janela com o **link do vídeo** e o botão **Copiar link**.

**Como funciona (sem Google Cloud):** você cria uma vez um pequeno *Google Apps Script* na conta da empresa que tem acesso à pasta. Esse script roda com a permissão do dono e apenas **abre uma sessão de envio retomável** no Drive; o arquivo vai direto do computador para o Google, em partes de 8 MB, com janela de progresso, retomada automática se a internet cair e conferência do tamanho no final. O vídeo herda as permissões da pasta (quem está no grupo da empresa consegue abrir o link).

**Configuração (uma vez):** veja o guia ilustrado por etapas com testes em [`drive-bridge/PASSO-A-PASSO.md`](drive-bridge/PASSO-A-PASSO.md). Resumo: seção **Google Drive** do programa → botão **?** mostra o passo a passo; o botão **Copiar código do script** copia o código (também disponível em `drive-bridge/Code.gs`). Resumo:
1. Abra https://script.new com a conta da empresa e cole o código; troque `TROQUE-POR-UMA-CHAVE-LONGA` por uma chave sua.
2. *Implantar → Nova implantação → App da Web* — **Executar como: Eu**; **Quem pode acessar: Qualquer pessoa**. Implante e autorize.
3. No TelaRec, cole o **link da pasta**, o **URL do App da Web** e a **chave**; clique em **Testar conexão**.

**Importante:** não use "Qualquer pessoa em [empresa]". Essa opção exige login Google na chamada, e o programa não consegue fazê-lo sem um projeto no Google Cloud (nesse caso o programa mostra um aviso explicando).

**Segurança:** quem souber o URL **e** a chave consegue apenas *enviar* arquivos para a pasta (não lê nem apaga nada). A chave fica protegida no computador pelo Windows (DPAPI) e não vai para o repositório. Para trocar a chave: altere-a no script, crie uma *nova versão* da implantação e atualize o programa. O script aceita uma lista opcional de pastas permitidas (`PASTAS_PERMITIDAS`). Os vídeos ficam em nome da conta dona do script e usam a cota de armazenamento dela/da pasta. O administrador do Google Workspace pode bloquear Apps Script ou apps sem login; nesse caso este caminho não funciona (alternativa oficial: criar um ID de cliente OAuth no Google Cloud).

**Limitação:** esta é uma técnica conhecida da comunidade, não o método oficial do Google; ainda precisa ser validada na sua conta.

## Efeito de clique (opcional)

Em **Configurações → Efeito de clique do mouse** (botão **?** explica as cores):

| Botão | Efeito no vídeo | Som |
|---|---|---|
| Esquerdo | círculo **azul** que cresce no ponto exato do clique | clique agudo |
| Direito | círculo **laranja** que cresce no ponto exato do clique | clique mais grave |

Funciona só no monitor gravado e não aparece durante a pausa. O círculo é desenhado numa janela transparente e "atravessável" sobre o monitor, por isso entra no vídeo. O **som é só aviso para você** (a v1 não grava áudio). O hook de mouse só fica ativo durante a gravação e apenas lê os cliques; nada é armazenado.

## Escolhas técnicas

| Item | Escolha | Motivo |
|---|---|---|
| Linguagem/UI | C# .NET 8 + WPF | Interface moderna nativa do Windows; publica em arquivo único autocontido (o usuário não instala runtime). |
| Captura | FFmpeg `ddagrab` (DXGI Desktop Duplication) | Captura **diretamente** a saída DXGI do monitor escolhido (nada de gravar tudo e recortar), eficiente e correta em qualquer DPI. |
| Mapeamento de monitor | Win32 `EnumDisplayMonitors` + DXGI `EnumAdapters1/EnumOutputs` | Liga o "Monitor N" do Windows ao par (adaptador, saída) usado pelo capturador. |
| Plano B | `gdigrab` (GDI) por coordenadas | Usado só se o DXGI falhar **e** todos os monitores estiverem em escala 100% (senão o recorte poderia errar). |
| Codificação | H.264: NVENC / Quick Sync / AMF (testados na inicialização) com fallback x264 | Compatibilidade máxima; hardware quando confiável. |
| Áudio | NAudio (WASAPI) → mixer próprio → PCM no stdin do FFmpeg → AAC | Loopback + microfone em tempo real, com silêncio injetado quando nada toca (o loopback não envia dados em silêncio). |
| Envio ao Drive | Apps Script (sessão retomável) + HttpClient em partes de 8 MB | Funciona sem projeto no Google Cloud; arquivo vai direto ao Google, com progresso e retomada. |
| Container | MP4 fragmentado por trecho → união sem recodificar + `faststart` | Arquivo sobrevive a quedas; pausa = novo trecho; sem manter vídeo em RAM. |
| Cor | BT.709, faixa limitada, sem escalonar | Cores corretas e texto nítido; só corta 1 px se a dimensão for ímpar (exigência do H.264). |
| Distribuição | `dotnet publish` single-file + FFmpeg embutido (gzip) | Um único `.exe`. |

Metas de tamanho (10–20 MB / 5 min) são referência; o padrão é x264 `veryfast` CRF 26 (hardware: CQ 28), 30 FPS. Os valores reais **ainda precisam ser medidos**.

## Estrutura

```
src/TelaRec.Core   lógica pura (argumentos do FFmpeg, nomes de arquivo, configurações) — testada
src/TelaRec.App    interface WPF, detecção de monitores, controle do FFmpeg
tests/TelaRec.Tests  testes unitários (xunit)
.github/workflows/build.yml   compilação, testes, executável, artefato e Release
```

## Comportamentos de segurança

- Revalida o monitor imediatamente antes de gravar; se o monitor sumir ou mudar de geometria durante a gravação, finaliza com segurança e avisa.
- Nunca sobrescreve arquivos (acrescenta ` (2)`, ` (3)`…).
- Verifica espaço livre: avisa abaixo de 3 GB e finaliza abaixo de 1 GB.
- Falha do codificador/captura: salva o que foi gravado e mostra mensagem. Se a união final falhar, os trechos ficam numa pasta oculta `.telarec-*` ao lado do destino e o caminho é informado.
- Sucesso só é informado depois que o FFmpeg fecha o MP4 final com código 0.
- A pasta temporária fica no mesmo disco do destino; a união final pode precisar temporariamente de espaço ≈ tamanho do vídeo.

## Limitações conhecidas (transparência)

- **Nunca testado em Windows real** (ver aviso no topo). O mapeamento monitor→DXGI, a opção "esconder esta janela da gravação" (`WDA_EXCLUDEFROMCAPTURE`) e telas giradas precisam de validação manual.
- Monitores girados (retrato) com `ddagrab`: comportamento não verificado.
- Sem webcam, edição, atalhos globais nem ícone de bandeja.
- Áudio: só a saída **padrão** do Windows é capturada (se você trocar o dispositivo de saída durante a gravação, o novo não é seguido); a sincronia áudio/vídeo é estimada (ver acima).
- Requer GPU/driver com Desktop Duplication (praticamente todo PC físico). Em máquinas virtuais/Área de Trabalho Remota pode cair no modo GDI.
- Windows 10 2004+ recomendado (para ocultar a janela do app da gravação).

## Licenças

O código do TelaRec é distribuído sob **GPL-3.0-or-later** (necessário por embutir o FFmpeg em build GPL). Adicione o texto completo da licença ao repositório (GitHub → *Add file → Create new file* → nome `LICENSE` → *Choose a license template* → GPL-3.0). Detalhes das dependências em [`THIRD_PARTY_LICENSES.md`](THIRD_PARTY_LICENSES.md).
