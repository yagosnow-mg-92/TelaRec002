/**
 * TelaRec - Ponte de envio ao Google Drive (Google Apps Script)  -  versão 4
 *
 * O QUE É A "CHAVE"?  É uma SENHA que o TelaRec gera para você. Ela NÃO vem do Google e NÃO é o ID da implantação.
 * A mesma senha fica em dois lugares: aqui no script (linha "const CHAVE") e no campo "Chave secreta" do TelaRec.
 * Se você copiou este código pelo botão "Copiar código do script" do TelaRec, a chave JÁ ESTÁ aqui dentro.
 *
 * PASSO A PASSO (uma vez só) - o botão "?" do TelaRec mostra tudo isto com botões de ação:
 *  1. Neste editor: Ctrl+A, Delete, Ctrl+V (cola este código inteiro) e Ctrl+S (salvar).
 *  2. No menu no topo do editor, escolha a função "autorizar", clique em Executar e aceite as permissões
 *     (Avançado > Acessar projeto > Permitir). Se já tinha feito antes, FAÇA DE NOVO: esta versão pede
 *     também a permissão de criar arquivos no Drive.
 *  3. Implantar > Nova implantação > tipo "App da Web":
 *       Executar como: Eu     |     Quem pode acessar: Qualquer pessoa
 *     Clique em Implantar e copie o "URL do app da Web".
 *  4. No TelaRec: cole o URL e clique em "Testar conexão" e depois em "Testar envio".
 *
 * SEMPRE que mudar qualquer coisa neste código (inclusive a chave):
 *   Implantar > Gerenciar implantações > ícone do lápis > Versão: "Nova versão" > Implantar.
 *
 * A ÚNICA LINHA QUE PODE PRECISAR DE ALTERAÇÃO é a que começa com "const CHAVE" (use Ctrl+F e procure por CHAVE).
 *   Altere SOMENTE o texto entre as aspas dessa linha. Nenhuma outra linha deve ser editada.
 *
 * SEGURANÇA: quem tiver o URL + a CHAVE consegue ENVIAR arquivos para a pasta e apagar apenas os arquivos de
 * TESTE do TelaRec (nome começando com "TelaRec-teste-"). Não consegue ler nem apagar mais nada.
 */
const VERSAO = 4; // usada pelo TelaRec para saber se a publicação está atualizada (não altere)

const CHAVE = 'TROQUE-POR-UMA-CHAVE-LONGA';

// Opcional: limite as pastas aceitas. Ex.: ['1AbC...IdDaPasta']. Vazio = qualquer pasta que você possa editar.
const PASTAS_PERMITIDAS = [];

const ESCOPO_DRIVE = 'https://www.googleapis.com/auth/drive';

function doPost(e) {
  try {
    const req = JSON.parse(e.postData.contents);
    if (!chaveTrocada_()) {
      return resposta_({ ok: false, error: 'Troque a CHAVE no script e implante uma nova versão.' });
    }
    if (!req.key || req.key !== CHAVE) return resposta_({ ok: false, error: 'Chave inválida.' });

    // Apaga SOMENTE arquivos de teste do TelaRec (nome "TelaRec-teste-..." e menores que 1 KB).
    if (req.action === 'delete') {
      let arq;
      try { arq = DriveApp.getFileById(String(req.fileId)); }
      catch (err) { return resposta_({ ok: false, error: 'Arquivo não encontrado.' }); }
      if (arq.getName().indexOf('TelaRec-teste-') !== 0 || arq.getSize() > 1024) {
        return resposta_({ ok: false, error: 'Só é permitido apagar arquivos de teste do TelaRec.' });
      }
      arq.setTrashed(true);
      return resposta_({ ok: true });
    }

    if (!req.folderId) return resposta_({ ok: false, error: 'Pasta não informada.' });
    if (PASTAS_PERMITIDAS.length && PASTAS_PERMITIDAS.indexOf(req.folderId) < 0) {
      return resposta_({ ok: false, error: 'Esta pasta não é permitida pelo script.' });
    }

    if (req.action === 'check') {
      let pasta;
      try { pasta = DriveApp.getFolderById(req.folderId); }
      catch (err) { return resposta_({ ok: false, error: 'Pasta não encontrada ou sem permissão para a conta dona do script.' }); }
      const esc = escopos_();
      return resposta_({
        ok: true,
        versao: VERSAO,
        folderName: pasta.getName(),
        owner: Session.getEffectiveUser().getEmail(),
        podeEscrever: esc.length ? esc.indexOf(ESCOPO_DRIVE) >= 0 : null
      });
    }

    if (req.action === 'start') {
      const tamanho = Number(req.size);
      if (!(tamanho > 0)) return resposta_({ ok: false, error: 'Tamanho do arquivo inválido.' });
      const mime = req.mime === 'text/plain' ? 'text/plain' : 'video/mp4';
      const nome = String(req.name || 'video.mp4').replace(/[\\/:*?"<>|]/g, '_').slice(0, 200);
      const url = 'https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&supportsAllDrives=true&fields=' +
        encodeURIComponent('id,name,size,webViewLink');
      const r = UrlFetchApp.fetch(url, {
        method: 'post',
        contentType: 'application/json; charset=UTF-8',
        headers: {
          Authorization: 'Bearer ' + ScriptApp.getOAuthToken(),
          'X-Upload-Content-Type': mime,
          'X-Upload-Content-Length': String(tamanho)
        },
        payload: JSON.stringify({ name: nome, parents: [req.folderId], mimeType: mime }),
        muteHttpExceptions: true
      });
      if (r.getResponseCode() !== 200) {
        const txt = r.getContentText();
        let dica = '';
        if (r.getResponseCode() === 403 && txt.indexOf('insufficient authentication scopes') >= 0) {
          dica = ' >> O script não tem permissão para CRIAR arquivos. No editor do script: escolha a função "autorizar", ' +
                 'clique em Executar e aceite as novas permissões; depois Implantar > Gerenciar implantações > lápis > Nova versão > Implantar.';
        }
        return resposta_({ ok: false, error: 'Drive recusou (' + r.getResponseCode() + '): ' + txt.slice(0, 200) + dica });
      }
      const h = r.getAllHeaders();
      const local = h['Location'] || h['location'];
      if (!local) return resposta_({ ok: false, error: 'O Drive não devolveu o endereço de envio.' });
      return resposta_({ ok: true, sessionUri: local });
    }

    return resposta_({ ok: false, error: 'Ação desconhecida.' });
  } catch (err) {
    return resposta_({ ok: false, error: String(err) });
  }
}

function resposta_(obj) {
  return ContentService.createTextOutput(JSON.stringify(obj)).setMimeType(ContentService.MimeType.JSON);
}

// Verdadeiro quando a CHAVE já não é o texto-modelo.
function chaveTrocada_() {
  return CHAVE.indexOf('TROQUE-POR') !== 0;
}

// Lista as permissões (escopos) que este script realmente possui neste momento.
function escopos_() {
  try {
    const r = UrlFetchApp.fetch('https://oauth2.googleapis.com/tokeninfo?access_token=' + encodeURIComponent(ScriptApp.getOAuthToken()),
      { muteHttpExceptions: true });
    return String(JSON.parse(r.getContentText()).scope || '').split(' ').filter(String);
  } catch (err) {
    return [];
  }
}

// Teste pelo navegador: abrir o URL do App da Web numa janela anônima deve mostrar este JSON.
function doGet() {
  return resposta_({
    ok: true,
    servico: 'TelaRec',
    versao: VERSAO,
    msg: 'Ponte no ar. Se você vê esta mensagem, o App da Web está publicado corretamente.',
    chaveConfigurada: chaveTrocada_()
  });
}

// Execute esta função no editor (menu no topo > autorizar > Executar) para conceder as permissões.
// Ela cria e apaga um arquivo de teste na raiz do seu Drive: é isso que faz o Google pedir a permissão de escrita.
function autorizar() {
  const f = DriveApp.createFile('TelaRec-teste-autorizacao.txt', 'ok');
  f.setTrashed(true);
  UrlFetchApp.fetch('https://www.googleapis.com/discovery/v1/apis/drive/v3/rest', { muteHttpExceptions: true });
  Logger.log('Autorização concluída. Agora faça a implantação (Implantar > Nova implantação) ou crie uma Nova versão.');
}
