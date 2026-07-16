# MangaReader

App pessoal para **ler e arquivar mangás** obtidos via API da MangaDex.
Usuário único. Este arquivo é o contexto base — o detalhe técnico está em `ARQUITETURA.md` e `schema.sql`.

---

## Regras invioláveis

Estas decisões definem o projeto. Não as contrarie sem me perguntar antes:

1. **As imagens são do dono.** A cópia-mestre fica em hardware próprio (o notebook). **Nada de nuvem de terceiros** como armazenamento (Supabase, S3 etc.) — o risco de takedown é justamente o que estamos evitando.
2. **O celular NUNCA fala com a MangaDex.** Só o backend .NET toca a API (inclusive por causa da falta de CORS). O app só conversa com o backend.
3. **Offline-first.** Depois de baixado, ler um capítulo não pode exigir internet nem servidor no ar.
4. **Sem servidor 24/7.** O notebook só liga para baixar mangá novo ou sincronizar. Ler o que já está no celular independe disso.
5. **Download é assíncrono.** Rotas de download enfileiram e retornam 202; um `BackgroundService` processa respeitando os rate limits. Nunca segurar a requisição HTTP esperando o download terminar.
6. **Respeitar a MangaDex.** Rate limits (5 req/s global; 40/min no at-home), report da rede @Home, e crédito à MangaDex + grupo de scanlation (AUP). `User-Agent` real.

---

## Arquitetura em uma olhada

```
MangaDex API ──(só o backend)──> [ .NET + SQLite + .cbz ]  <──USB/Wi-Fi──>  [ App Capacitor + SD ]
                                        NOTEBOOK                                    CELULAR
```

- **Notebook:** backend .NET (Web API + BackgroundService), SQLite, arquivos `.cbz` (1 por capítulo) em disco. Único a falar com a MangaDex.
- **Celular:** app JS (React/Svelte) + **Capacitor** (APK instalável, acesso real ao cartão SD). Guarda `.cbz` no SD, lê offline.
- **Conexão:** cabo USB-C com tethering **ou** mesma rede Wi-Fi.

---

## Stack

- **Backend:** .NET (Web API + `BackgroundService` para a fila de download).
- **Banco:** SQLite. Ligar `PRAGMA foreign_keys = ON` e `journal_mode = WAL` **a cada conexão**. Sem `BOOL` (usar `0/1`). Datas em texto ISO 8601 (UTC).
- **Imagens:** `.cbz` (zip de imagens). Serve para agrupar, não para encolher — economia real de espaço vem de baixar em `data-saver`.
- **Frontend:** JavaScript + Capacitor. **Não usar Blazor** (preferência do dono).

---

## Onde está o resto

- `ARQUITETURA.md` — decisões técnicas completas, integração MangaDex, API, serviço de download, fluxos.
- `schema.sql` — DDL do SQLite (aplicar na inicialização do backend). Fonte da verdade do modelo de dados; não reinterpretar a partir de prosa.

---

## Convenções

- API sob `/api`, JSON em tudo. Sem auth por enquanto (será adicionada depois sem mudar as rotas — não inventar auth agora).
- `chapter.chapter` é **texto** (existe "10.5"); ordenar por `chapter.sort_number`.
- Diferenciar `DELETE /api/chapters/{id}/file` (libera espaço, mantém na biblioteca) de `DELETE /api/manga/{id}` (apaga tudo).
- Pressuposto de plataforma: **Android** (cartão SD, USB tethering).

---

## Status

Projeto começando do zero. Ordem sugerida de implementação está na seção 9 do `ARQUITETURA.md`. Comece pelo item 1 (projeto .NET + SQLite + aplicar o schema).