/**
 * TelaRec - Ponte de envio ao Google Drive (Google Apps Script)  -  versão 3
 *
 * O QUE É A "CHAVE"?  É uma SENHA que VOCÊ INVENTA (ou que o TelaRec gera para você).
 * Ela NÃO é gerada pelo Google e NÃO é o ID da implantação. A mesma senha fica em dois lugares:
 * aqui no script (linha "const CHAVE") e no campo "Chave secreta" do TelaRec.
 * Se você copiou este código pelo botão "Copiar código do script" do TelaRec, a chave JÁ ESTÁ aqui dentro.
 *
 * PASSO A PASSO (uma vez só):
 *  1. Neste editor: Ctrl+A, Delete, Ctrl+V (cola este código inteiro) e Ctrl+S (salvar).
 *  2. No menu no topo do editor, escolha a função "autorizar", clique em Executar e aceite as permissões
 *     (Avançado > Acessar projeto > Permitir).
 *  3. Implantar > Nova implantação > tipo "App da Web":
 *       Executar como: Eu     |     Quem pode acessar: Qualquer pessoa
 *     Clique em Implantar e copie o "URL do app da Web".
 *  4. TESTE: abra esse URL numa JANELA ANÔNIMA. Deve aparecer {"ok":true,"servico":"TelaRec",...}.
 *  5. No TelaRec: cole o URL e clique em "Testar conexão".
 *
 * SEMPRE que mudar qualquer coisa neste código (inclusive a chave):
 *   Implantar > Gerenciar implantações > ícone do lápis > Versão: "Nova versão" > Implantar.
 *
 * A ÚNICA LINHA QUE PODE PRECISAR DE ALTERAÇÃO é a que começa com "const CHAVE" (use Ctrl+F e procure por CHAVE).
 *   Altere SOMENTE o texto entre as aspas dessa linha. Nenhuma outra linha deve ser editada.
 *
 * SEGURANÇA: quem tiver o URL + a CHAVE consegue ENVIAR arquivos para a pasta, mas não ler nem apagar.
 */
const VERSAO = 3; // usada pelo TelaRec para saber se a publicação está atualizada (não altere)

const CHAVE = 'TROQUE-POR-UMA-CHAVE-LONGA';

// Opcional: limite as pastas aceitas. Ex.: ['1AbC...IdDaPasta']. Vazio = qualquer pasta que você possa editar.
const PASTAS_PERMITIDAS = [];

function doPost(e) {
  try {
    const req = JSON.parse(e.postData.contents);
    if (!chaveTrocada_()) {
      return resposta_({ ok: false, error: 'Troque a CHAVE no script e implante uma nova versão.' });
    }
    if (!req.key || req.key !== CHAVE) return resposta_({ ok: false, error: 'Chave inválida.' });
    if (!req.folderId) return resposta_({ ok: false, error: 'Pasta não informada.' });
    if (PASTAS_PERMITIDAS.length && PASTAS_PERMITIDAS.indexOf(req.folderId) < 0) {
      return resposta_({ ok: false, error: 'Esta pasta não é permitida pelo script.' });
    }

    if (req.action === 'check') {
      let pasta;
      try { pasta = DriveApp.getFolderById(req.folderId); }
      catch (err) { return resposta_({ ok: false, error: 'Pasta não encontrada ou sem permissão para a conta dona do script.' }); }
      return resposta_({ ok: true, versao: VERSAO, folderName: pasta.getName(), owner: Session.getEffectiveUser().getEmail() });
    }

    if (req.action === 'start') {
      const tamanho = Number(req.size);
      if (!(tamanho > 0)) return resposta_({ ok: false, error: 'Tamanho do arquivo inválido.' });
      const nome = String(req.name || 'video.mp4').replace(/[\\/:*?"<>|]/g, '_').slice(0, 200);
      const url = 'https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&supportsAllDrives=true&fields=' +
        encodeURIComponent('id,name,size,webViewLink');
      const r = UrlFetchApp.fetch(url, {
        method: 'post',
        contentType: 'application/json; charset=UTF-8',
        headers: {
          Authorization: 'Bearer ' + ScriptApp.getOAuthToken(),
          'X-Upload-Content-Type': 'video/mp4',
          'X-Upload-Content-Length': String(tamanho)
        },
        payload: JSON.stringify({ name: nome, parents: [req.folderId], mimeType: 'video/mp4' }),
        muteHttpExceptions: true
      });
      if (r.getResponseCode() !== 200) {
        return resposta_({ ok: false, error: 'Drive recusou (' + r.getResponseCode() + '): ' + r.getContentText().slice(0, 200) });
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

// Execute esta função UMA vez no editor (menu no topo > autorizar > Executar) para conceder as permissões.
function autorizar() {
  DriveApp.getRootFolder();
  UrlFetchApp.fetch('https://www.googleapis.com/discovery/v1/apis/drive/v3/rest', { muteHttpExceptions: true });
  Logger.log('Autorização concluída. Agora faça a implantação (Implantar > Nova implantação).');
}

// Verdadeiro quando a CHAVE já não é o texto-modelo.
function chaveTrocada_() {
  return CHAVE.indexOf('TROQUE-POR') !== 0;
}
