-- ============================================================
--  MangaReader — Schema SQLite
--  Banco: biblioteca-mestre (roda no notebook, junto do backend .NET)
--  SQLite 3.x
-- ============================================================

-- ------------------------------------------------------------
--  PRAGMAs — precisam ser aplicados A CADA CONEXÃO
--  (não são persistidos no arquivo, exceto journal_mode)
-- ------------------------------------------------------------
PRAGMA foreign_keys = ON;     -- faz as FKs (ON DELETE CASCADE etc.) realmente valerem
PRAGMA journal_mode = WAL;    -- deixa você LER enquanto o downloader ESCREVE (persiste no arquivo)
PRAGMA synchronous = NORMAL;  -- bom equilíbrio segurança/velocidade no modo WAL


-- ============================================================
--  MANGA
-- ============================================================
CREATE TABLE manga (
    id              INTEGER PRIMARY KEY,          -- rowid local
    mangadex_id     TEXT    NOT NULL UNIQUE,       -- UUID da MangaDex
    title           TEXT    NOT NULL,
    description     TEXT,
    author          TEXT,
    artist          TEXT,
    status          TEXT    CHECK (status IN ('ongoing','completed','hiatus','cancelled')),
    year            INTEGER,
    content_rating  TEXT    CHECK (content_rating IN ('safe','suggestive','erotica','pornographic')),
    cover_filename  TEXT,                          -- nome do arquivo de capa na MangaDex
    created_at      TEXT    NOT NULL DEFAULT (datetime('now')),  -- UTC (ISO 8601)
    updated_at      TEXT    NOT NULL DEFAULT (datetime('now'))
);


-- ============================================================
--  CHAPTER
-- ============================================================
CREATE TABLE chapter (
    id                INTEGER PRIMARY KEY,
    manga_id          INTEGER NOT NULL REFERENCES manga(id) ON DELETE CASCADE,
    mangadex_id       TEXT    NOT NULL UNIQUE,      -- UUID da MangaDex
    volume            TEXT,                         -- nullable (nem todo capítulo tem volume)
    chapter           TEXT,                         -- TEXTO de propósito: existe "10.5"
    sort_number       REAL,                         -- número normalizado só p/ ordenar (ex: 10.5)
    title             TEXT,
    language          TEXT    NOT NULL,             -- ex: 'pt-br', 'en'
    scanlation_group  TEXT,                         -- crédito obrigatório (regra da MangaDex)
    page_count        INTEGER NOT NULL DEFAULT 0,
    published_at      TEXT,
    download_status   TEXT    NOT NULL DEFAULT 'none'
                              CHECK (download_status IN ('none','downloading','done','error')),
    local_path        TEXT,                         -- caminho do .cbz no notebook
    file_size_bytes   INTEGER,
    downloaded_at     TEXT,
    created_at        TEXT    NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX idx_chapter_manga     ON chapter(manga_id);
CREATE INDEX idx_chapter_download  ON chapter(download_status);
CREATE INDEX idx_chapter_sort      ON chapter(manga_id, sort_number);


-- ============================================================
--  PAGE  (OPCIONAL)
--  Com CBZ as páginas já vivem ordenadas dentro do .cbz, então
--  normalmente esta tabela é dispensável. Mantenha só se quiser
--  rastrear página a página fora do arquivo.
-- ============================================================
CREATE TABLE page (
    id           INTEGER PRIMARY KEY,
    chapter_id   INTEGER NOT NULL REFERENCES chapter(id) ON DELETE CASCADE,
    page_number  INTEGER NOT NULL,
    filename     TEXT    NOT NULL,                  -- nome original na MangaDex (ex: x1.jpg)
    UNIQUE (chapter_id, page_number)
);

CREATE INDEX idx_page_chapter ON page(chapter_id);


-- ============================================================
--  TAGS  (OPCIONAL — gênero/tema)
--  Se preferir começar simples, ignore estas duas tabelas e
--  guarde os gêneros como JSON num campo extra da manga.
-- ============================================================
CREATE TABLE tag (
    id           INTEGER PRIMARY KEY,
    mangadex_id  TEXT UNIQUE,
    name         TEXT NOT NULL UNIQUE
);

CREATE TABLE manga_tag (
    manga_id  INTEGER NOT NULL REFERENCES manga(id) ON DELETE CASCADE,
    tag_id    INTEGER NOT NULL REFERENCES tag(id)   ON DELETE CASCADE,
    PRIMARY KEY (manga_id, tag_id)
);

CREATE INDEX idx_manga_tag_tag ON manga_tag(tag_id);


-- ============================================================
--  READING PROGRESS
--  Backup do progresso que sobe do celular no sync.
--  1 linha por capítulo (single user).
-- ============================================================
CREATE TABLE reading_progress (
    id           INTEGER PRIMARY KEY,
    chapter_id   INTEGER NOT NULL UNIQUE REFERENCES chapter(id) ON DELETE CASCADE,
    last_page    INTEGER NOT NULL DEFAULT 0,
    is_read      INTEGER NOT NULL DEFAULT 0 CHECK (is_read IN (0,1)),  -- SQLite não tem BOOL
    updated_at   TEXT    NOT NULL DEFAULT (datetime('now'))
);


-- ============================================================
--  TRIGGERS — mantêm updated_at fresco automaticamente
-- ============================================================
CREATE TRIGGER trg_manga_updated
AFTER UPDATE ON manga
FOR EACH ROW
BEGIN
    UPDATE manga SET updated_at = datetime('now') WHERE id = NEW.id;
END;

CREATE TRIGGER trg_progress_updated
AFTER UPDATE ON reading_progress
FOR EACH ROW
BEGIN
    UPDATE reading_progress SET updated_at = datetime('now') WHERE id = NEW.id;
END;


-- ============================================================
--  VIEW útil: "continuar lendo"
--  Último capítulo tocado por mangá.
-- ============================================================
CREATE VIEW v_continue_reading AS
SELECT m.id             AS manga_id,
       m.title          AS manga_title,
       c.id             AS chapter_id,
       c.chapter        AS chapter_label,
       rp.last_page     AS last_page,
       rp.updated_at    AS last_read_at
FROM reading_progress rp
JOIN chapter c ON c.id = rp.chapter_id
JOIN manga   m ON m.id = c.manga_id
WHERE rp.is_read = 0
ORDER BY rp.updated_at DESC;


-- ============================================================
--  APÊNDICE — STORE DO CELULAR (opcional)
--  Use SÓ se o app (Capacitor) também usar SQLite no aparelho.
--  Fica num arquivo .db separado, no próprio celular.
-- ============================================================
--
-- CREATE TABLE library_item (
--     chapter_mangadex_id  TEXT PRIMARY KEY,   -- casa com chapter.mangadex_id do notebook
--     manga_mangadex_id    TEXT NOT NULL,
--     local_cbz_path       TEXT NOT NULL,      -- caminho do .cbz no cartão SD
--     downloaded_at        TEXT NOT NULL DEFAULT (datetime('now'))
-- );
--
-- CREATE TABLE reading_progress (
--     chapter_mangadex_id  TEXT PRIMARY KEY,
--     last_page            INTEGER NOT NULL DEFAULT 0,
--     is_read              INTEGER NOT NULL DEFAULT 0 CHECK (is_read IN (0,1)),
--     updated_at           TEXT    NOT NULL DEFAULT (datetime('now'))
-- );
--
-- No sync: o celular manda estas duas tabelas pro notebook,
-- que atualiza a reading_progress dele (casando pelo mangadex_id).