# MangaReader — Documento Técnico

> App pessoal para ler e arquivar mangás obtidos via API da MangaDex.
> Este documento reúne **todas as decisões técnicas** e serve de contexto para implementação.
> O DDL do banco está no arquivo separado `schema.sql`.

---

## 1. Objetivo e restrições que guiam o projeto

- **Usuário único.** Sem autenticação por enquanto (será adicionada depois, sem quebrar as rotas).
- **As imagens são do dono.** Não podem correr o risco de serem apagadas por terceiros. → Sem nuvem de terceiros como armazenamento-mestre; a cópia-mestre fica em hardware do próprio usuário.
- **Leitura offline.** Depois de baixado, ler um capítulo não exige internet nem servidor no ar.
- **Sem servidor 24/7.** O notebook só precisa estar ligado na hora de **baixar mangá novo** ou **sincronizar** com o celular. Ler o que já está no aparelho independe disso.
- **Leve, fácil de manter e versionar** (git), instalável no celular.

**Por que não Supabase / nuvem:** contradiz a restrição principal. O provedor é um terceiro que pode suspender a conta (o conteúdo é scanlation), o free tier de storage é minúsculo (~1 GB) e o pago vira conta mensal que cresce com o acervo. Nenhum serviço é grátis + ilimitado + à prova de takedown ao mesmo tempo.

---

## 2. Arquitetura geral

Dois componentes, com papéis bem separados:

**Notebook (backend + biblioteca-mestre)**
- Backend **.NET** (Web API + serviço de background).
- Banco **SQLite**.
- Arquivos-mestre: um `.cbz` por capítulo, em disco.
- **É o único componente que fala com a MangaDex.**

**Celular (leitor)**
- App em **JavaScript** (React ou Svelte) empacotado com **Capacitor** → gera APK instalável e tem acesso real ao sistema de arquivos / cartão SD.
- Guarda os `.cbz` baixados no **cartão SD**.
- **Nunca fala com a MangaDex** — só conversa com o notebook.

**Conexão entre os dois**
- Cabo USB-C com *USB tethering* (rede pelo cabo), **ou** os dois na mesma rede Wi-Fi.
- O celular pede os arquivos ao servidor .NET; baixa; depois lê offline.

```
MangaDex API ──(só o backend)──> [ .NET + SQLite + .cbz ]  <──USB/Wi-Fi──>  [ App Capacitor + SD ]
                                        NOTEBOOK                                    CELULAR
```

---

## 3. Stack e decisões

| Camada | Escolha | Por quê |
|---|---|---|
| Backend | .NET (Web API) | Preferência do dono; obrigatório porque a API da MangaDex **não tem CORS** (navegador não pode chamar direto). |
| Fila de download | `BackgroundService` do .NET | Download é lento e limitado por rate limit → precisa ser assíncrono e enfileirado. |
| Banco | SQLite | Usuário único, manutenção mínima, backup = copiar 1 arquivo. Migrável para Postgres se crescer. |
| Concorrência no banco | Modo **WAL** | Permite ler a biblioteca enquanto o downloader escreve. |
| Armazenamento de imagem | `.cbz` (zip de imagens) | Formato padrão de quadrinho, 1 arquivo por capítulo, portátil, abre em outros apps. |
| Frontend | JS (React/Svelte) + **Capacitor** | Preferência por JS (Blazor descartado). Capacitor dá APK instalável **e** storage real no SD (um PWA puro pode ter o cache despejado pelo navegador). |

**Sobre "compactar":** imagens de mangá já vêm comprimidas (JPEG/WebP). O `.cbz` serve para **agrupar**, não para encolher — ganho de espaço de zipar ≈ 0%. A alavanca real de espaço é baixar em **data-saver** (resolução menor, ~metade do tamanho), decidido no momento do download. Reconverter para WebP/AVIF reduz mais, mas dá trabalho de processar cada imagem (fica como melhoria futura).

**Pressuposto:** Android (por causa do cartão SD e do USB tethering). No iOS essa conexão via USB é bem mais travada — a confirmar.

---

## 4. Integração com a MangaDex

Base: `https://api.mangadex.org`. **Todas** as chamadas partem do backend .NET.

**Endpoints usados**
- `GET /manga?title=...` — busca.
- `GET /manga/{id}/feed` — lista de capítulos. Pagine: `size` até 500 no feed; requisições onde `offset + size > 10.000` são rejeitadas.
- `GET /at-home/server/{chapterId}` — retorna `baseUrl` + `hash` + duas listas de arquivos: `data` (resolução cheia) e `dataSaver` (comprimida).
- Capa: `uploads.mangadex.org/covers/{mangaId}/{filename}` — CDN direto, **sem rate limit**.

**Montagem da URL da página**
```
{baseUrl}/data/{hash}/{filename}          # resolução cheia
{baseUrl}/data-saver/{hash}/{filename}    # data-saver
```

**Regras críticas**
- **O `baseUrl` expira em ~15 min.** Se tomar `403` no meio do download, chame `/at-home/server` de novo.
- **Rate limits:** global de **5 req/s** em `api.mangadex.org`; o `/at-home/server` é **40 req/min**. → O download precisa de uma fila que respeite os dois.
- **Report de rede:** para cada imagem baixada de uma base que **não** é `mangadex.org` (nó @Home), notificar via `POST https://api.mangadex.network/report`. É o combinado da rede @Home.
- **AUP:** creditar a MangaDex e o grupo de scanlation (por isso `scanlation_group` no schema); proibido rodar anúncios ou serviço pago sobre a API. Uso pessoal está ok.
- **Boa cidadania:** enviar um `User-Agent` real e respeitar os limites. Existe enforcement recente contra abuso de API; ter uma conta/token pode ajudar a evitar bloqueios.

**Atalho:** o NuGet **MangaDexSharp** já cuida de paginação e rate limit de vários endpoints. Vale avaliar antes de escrever tudo do zero.

---

## 5. Modelo de dados

DDL completo em **`schema.sql`**. Resumo:

**Notebook (fonte da verdade)**
- `manga` — metadados + `mangadex_id` (UUID único) + `cover_filename`.
- `chapter` — pertence a `manga`; `chapter` é **texto** (existe "10.5") e há `sort_number REAL` só para ordenar; guarda `download_status` (`none`/`downloading`/`done`/`error`), `local_path` do `.cbz`, `scanlation_group`.
- `page` — **opcional**; com `.cbz` normalmente dispensável.
- `tag` + `manga_tag` — **opcionais** (gênero/tema).
- `reading_progress` — 1 linha por capítulo (`last_page`, `is_read`); backup do que sobe do celular.
- View `v_continue_reading` — "continuar de onde parou".

**Celular (store local, opcional em SQLite via Capacitor)**
- `library_item` — o que está fisicamente no SD (`local_cbz_path`).
- `reading_progress` — nasce aqui (é onde se lê) e sobe no sync.

**Lembretes de SQLite**
- `PRAGMA foreign_keys = ON` e `journal_mode = WAL` **a cada conexão** (no .NET, via connection string ou logo após abrir).
- Não há `BOOL`: `is_read` é `0/1`.
- Datas em texto ISO 8601 (`datetime('now')` = UTC).

---

## 6. API do backend (.NET)

Convenção: base `/api`, JSON em tudo, sem auth por enquanto.

**Descoberta (proxy da MangaDex)**
- `GET /api/search?title={q}&lang=pt-br&limit=20` → candidatos `[{ mangadexId, title, year, status, coverUrl }]`. Não persiste.

**Biblioteca**
- `GET /api/manga` → lista `[{ id, title, coverUrl, totalChapters, downloadedChapters }]`.
- `POST /api/manga` — body `{ mangadexId, language }`; busca metadados + capa + **todo o feed** e persiste (capítulos com status `none`).
- `GET /api/manga/{id}` → detalhe + capítulos com `downloadStatus`.
- `POST /api/manga/{id}/refresh` → insere só capítulos novos do feed.
- `DELETE /api/manga/{id}` → apaga metadados **e** os `.cbz`.

**Download**
- `POST /api/chapters/{id}/download` — body opcional `{ quality: "data" | "data-saver" }`. Retorna **202** (enfileira).
- `POST /api/manga/{id}/download` — lote; body `{ onlyMissing: true, quality }`.
- `GET /api/chapters/{id}/status` → `{ status, progress }`.
- `GET /api/downloads` → fila inteira (baixando/na fila/erros).
- `DELETE /api/chapters/{id}/file` → apaga só o `.cbz`, status volta a `none` (o "arquivar": some do disco, continua na lista).

**Arquivo (o que o celular mais usa)**
- `GET /api/chapters/{id}/file` → **stream do `.cbz`**. `Content-Type: application/vnd.comicbook+zip`. Suporta `Range` (retomar download interrompido).

**Sync**
- `GET /api/sync/manifest` → índice `[{ mangaId, chapters: [{ chapterId, mangadexId, downloadStatus, hasFile, fileSize }] }]`.
- `POST /api/sync/progress` → celular envia `[{ chapterMangadexId, lastPage, isRead, updatedAt }]`; servidor faz **merge por `updatedAt`** (last-write-wins).
- `GET /api/sync/progress` → devolve o progresso salvo.

**Saúde**
- `GET /api/health` → ping (o app checa antes de sincronizar).

**Princípios embutidos**
- **Download assíncrono:** `POST /download` só enfileira; um `BackgroundService` processa respeitando os rate limits. Nunca segurar a requisição HTTP esperando o download.
- **O celular nunca toca a MangaDex:** nenhuma rota expõe URL da MangaDex ao app.
- **Refinamento futuro:** trocar o polling de `/status` por **SSE** em `GET /api/downloads/stream` para progresso em tempo real.

---

## 7. Serviço de download (background)

Comportamento esperado do `BackgroundService`:

1. Consome uma **fila** de capítulos a baixar.
2. Respeita os limites: **≤ 5 req/s** global e **≤ 40/min** no `/at-home/server` (throttle/espera entre itens).
3. Para cada capítulo: pega o `/at-home/server`, monta as URLs, baixa as páginas **em ordem**, monta o `.cbz`, grava em `local_path`, atualiza `download_status` e `file_size_bytes`.
4. Se o `baseUrl` expirar (403) no meio, re-busca o `/at-home/server`.
5. Em `429`, faz **backoff** e re-tenta.
6. Envia o **report** de rede @Home para cada imagem.
7. Atualiza o progresso consultável via `GET /api/chapters/{id}/status`.

---

## 8. Fluxo de sync (notebook ↔ celular)

1. App checa `GET /api/health`.
2. Baixa `GET /api/sync/manifest` e compara com o que tem no SD.
3. Para cada capítulo desejado: `GET /api/chapters/{id}/file` → grava o `.cbz` no SD → registra em `library_item`.
4. Envia `POST /api/sync/progress` com o progresso local → servidor faz merge no `reading_progress` (backup).

---

## 9. Ordem sugerida de implementação

1. Projeto .NET + SQLite + aplicar `schema.sql` na inicialização.
2. Cliente da MangaDex (busca, feed, at-home) com throttle de rate limit.
3. Rotas de biblioteca (`/api/search`, `POST /api/manga`, `GET /api/manga`).
4. `BackgroundService` de download + rotas de download + geração de `.cbz`.
5. Rota de stream do `.cbz` + rotas de sync.
6. App Capacitor: lista da biblioteca → baixar `.cbz` no SD → leitor de `.cbz` → envio de progresso.
7. (Depois) SSE, auth, re-encode WebP/AVIF.

---

## 10. Decisões adiadas / a confirmar

- **Auth** e multiusuário (adicionar `user_id` em `reading_progress`).
- **Acesso remoto** (ex.: Tailscale) — só se um dia quiser **adicionar** mangá longe do notebook. Ler não precisa.
- **Plataforma:** Android assumido; iOS a confirmar (USB mais restrito).
- **Idioma padrão** dos capítulos (`pt-br` vs `en`) e **qualidade padrão** (`data` vs `data-saver`).