# Findings — MikroTik RouterOS REST API

**Zdroj:** https://help.mikrotik.com/docs/spaces/ROS/pages/47579162/REST+API
**Datum:** 2026-05-31
**Retrieval status:** Oficiální stránka zpracována research agentem (Confluence); **klíčové write-path
chování (PUT/POST/PATCH/DELETE/unset/move) navíc živě ověřeno** proti testovacímu routeru
testovací CHR (RouterOS **7.21.4 long-term**) z hlavní session. Řádky označené ✅ = ověřeno živě,
📄 = z dokumentace (živě neověřeno).

> Účel: referenční podklad pro `RestConnection : ITikConnection` v `tik4net` core (kapitola A).
> Viz [A-rest-implementation-plan.md](A-rest-implementation-plan.md).

---

## 1. Základ

- ✅ Base URL: `http(s)://<host>/rest/<menu-path>`. CLI cesta `/ip/firewall/address-list`
  → URL `/rest/ip/firewall/address-list` (lomítka 1:1, bez úvodního `/` v API path se přidá).
- ✅ Dostupné od **RouterOS 7.1+**. Služba `www` (HTTP, port 80) nebo `www-ssl` (HTTPS, port 443) v `/ip/service`.
- ✅ **Všechny hodnoty v JSON jsou stringy** — bool jako `"true"`/`"false"`, `.id` jako `"*1"`,
  čísla jako `"1500"`. Nikdy nečekat nativní JSON bool/number. (Shodné s binárním API → existující
  konverze v `tik4net.entities` mapperu fungují beze změny.)
- ✅ `.id` má stejný formát jako u API (`*1`, `*6`, …) → `Id` property mapperu funguje beze změny.
- 📄 **Content-Type musí být přesně `application/json`** (bez `; charset=utf-8`) — starší ROS dělaly
  striktní string match a vracely **HTTP 415**; opraveno ve v7.2RC5, ale v C# `HttpClient` posílat
  `application/json` bez charsetu pro jistotu. (Na 7.21.4 fungovalo i tak, ale držet se bezpečné varianty.)

---

## 2. HTTP verby — mapování (✅ živě ověřeno na 7.21.4)

| Operace | HTTP | URL | Tělo | Poznámka |
|---|---|---|---|---|
| **print / list** | `GET` | `/rest/<path>` | — | vrací JSON **pole** objektů (vč. `.id`) |
| **print + proplist** | `GET` | `/rest/<path>?.proplist=a,b` | — | omezí vrácená pole |
| **print + filtr/proplist** | `POST` | `/rest/<path>/print` | `{".query":["name=x"],".proplist":["a","b"]}` | filtr **musí** přes `.query` (ne `?`-klíče → 400) |
| **add** | `PUT` | `/rest/<path>` | `{"field":"val",...}` | ✅ vrací **celý vytvořený objekt vč. `.id`** (žádné zvláštní `ret`) |
| **set** | `PATCH` | `/rest/<path>/{id}` | `{"field":"val",...}` | `{id}` bez `*`? — funguje s `*X` v cestě; vrací celý objekt |
| **set (alt)** | `POST` | `/rest/<path>/set` | `{".id":"*X","field":"val"}` | `.id` v těle — ekvivalent PATCH |
| **remove** | `DELETE` | `/rest/<path>/{id}` | — | ✅ |
| **unset (clear pole)** | `PATCH` | `/rest/<path>/{id}` | `{"field":null}` nebo `{"field":""}` | ✅ vyprázdní pole; **`POST /unset` vrací 400** (viz §4) |
| **move (ordered)** | `POST` | `/rest/<path>/move` | `{".id":"*X","destination":"*Y"}` nebo `{"numbers":"*X","destination":"*Y"}` | ✅ obě formy fungují; `destination` = `.id` prvku, **před který** se vkládá |
| **libovolný příkaz** | `POST` | `/rest/<path>/<command>` | `{...}` | např. `/print`, `/set`, `/move`, `/monitor` |

> ⚠️ **POST na kořen kolekce (`/rest/<path>`) bez příkazu → HTTP 400.** POST je jen pro
> `/rest/<path>/<command>` endpointy. **Add se dělá `PUT`em na kořen kolekce**, ne POSTem.
> (Toto je oprava oproti původnímu předpokladu v plánu, kde byl add = POST.)

### Ověřené příklady (zkrácené odpovědi)

```
PUT  /rest/ip/firewall/address-list  {"list":"x","address":"192.0.2.50"}
 → 200 {".id":"*1","address":"192.0.2.50","list":"x","disabled":"false","dynamic":"false",...}

PATCH /rest/ip/firewall/address-list/*1  {"comment":"hello"}
 → 200 {".id":"*1",...,"comment":"hello",...}

PATCH /rest/ip/firewall/address-list/*1  {"comment":null}   → comment se vyprázdní na ""

POST /rest/ip/firewall/filter/move  {".id":"*7","destination":"*6"}  → přesun *7 před *6

DELETE /rest/ip/firewall/address-list/*1  → 200
```

---

## 3. Dotazování (`print`)

- ✅ **Jednoduchý list:** `GET /rest/<path>` → pole všech prvků.
- ✅ **proplist (omezení polí):** `GET /rest/<path>?.proplist=name,type` (query string, comma-separated).
- ✅ **Filtr + proplist (doporučeno jednotně):** `POST /rest/<path>/print` s tělem:
  ```json
  { ".query": ["name=ether1", "type=ether"], ".proplist": ["name","type",".id"] }
  ```
  `.query` je **pole podmínek**; `.proplist` je pole (nebo comma-string) názvů polí.
- 📄 **Operátory v `.query`:** `=`, `<`, `>`, `~` (regex/substring); logické spojky jako samostatné
  prvky pole: `"#|"` (OR), `"#&"` (AND), `"#!"` (NOT). Příklad:
  `[".query":["type=ether","type=vlan","#|"]]` = type==ether OR type==vlan.
- 📄 `.detail` — vyžádá detailní pole (analogie `print detail`).
- ✅ Filtry přes `?name=value` v těle POST/print **nefungují** (→ 400). Pouze přes `.query`.

**Doporučení pro `RestRequestBuilder`:** bez filtru i proplist → `GET /rest/<path>`. Jakmile je
přítomen filtr nebo proplist → `POST /rest/<path>/print` (zvládne obojí jednotně).

---

## 4. Unset — pozor (✅ ověřeno)

- **Neexistuje funkční `POST /rest/<path>/unset`** na 7.21.4 — všechny varianty
  (`{".id","value-name"}`, `{"numbers","value-name"}`) vrací **HTTP 400**.
  (Research agent uváděl `POST /unset {".id","value-name"}` — na 7.21.4 to **neplatí**.)
- **Funkční náhrada:** `PATCH /rest/<path>/{id}` s `{"field":null}` nebo `{"field":""}` → pole se vyprázdní.
- ⚠️ Sémantický rozdíl: tik4net mapper používá `/unset` k **resetu pole na default**. REST `PATCH null`
  nastaví **prázdnou hodnotu**, což u některých polí nemusí být totéž jako „revert na default".
  Pro většinu textových polí (comment apod.) je to ekvivalent. **Dokumentovat jako known limitation.**

---

## 5. Autentizace

- ✅ **HTTP Basic auth** (`Authorization: Basic base64(user:pass)`), ověřeno s prázdným heslem.
- 📄 Žádný token/cookie mechanismus v základu — Basic auth na každý request.
- HTTPS: certifikát na routeru **povinný** (.NET `SslStream` nepodporuje anonymous-DH); self-signed
  akceptovat přes `ServerCertificateCustomValidationCallback`. Viz
  [A-rest-implementation-plan.md §0.1](A-rest-implementation-plan.md).

---

## 6. Chyby

- 📄 Chybová odpověď je JSON: `{ "error": <http-status-int>, "message": "<text>", "detail": "<text>" }`.
  HTTP status odpovídá `error` (400/404/415/500…).
- ✅ Špatně formovaný požadavek (např. filtr přes `?`-klíč, POST na kořen kolekce, neznámé tělo) → **400**.
- Mapování na existující výjimky tik4net (v `RestCommand`): text `message`/`detail` obsahuje analogie
  binárního API trapu — `"no such command"`, `"no such item"`, `"already have such item"` →
  `TikNoSuchCommandException` / `TikNoSuchItemException` / `TikAlreadyHaveSuchItemException`;
  ostatní 4xx/5xx → `TikCommandTrapException`. HTTP 401 → `TikConnectionLoginException`.
  (Přesné texty REST chyb **ověřit během implementace** a doplnit mapování.)

---

## 7. Capability gaps (📄 + logika)

- **Žádný `/listen` / push** — REST je request-response.
- **Žádný streaming/follow.** Monitor příkazy (`/interface/monitor`, `/tool/ping` apod.) volat s
  `{"once":""}` (resp. `once`), jinak by request „visel". Průběžné výsledky (Torch, kontinuální ping)
  nejsou možné → v `RestConnection` `NotSupportedException`.
- 📄 **~60s hard timeout** na request na straně routeru → dlouhé operace nelze držet otevřené.
- → V capability matici: `Crud` ano; `Listen`, `Streaming`, `RawSentences`, `Tagging` ne.

---

## 8. Open questions / k ověření během implementace

1. Přesné texty REST chybových `message`/`detail` pro „no such command/item", „already have" → doladit mapování §6.
2. `{id}` v PATCH/DELETE cestě — ověřeno s `*X` (s hvězdičkou). Ověřit URL-encoding `*` (funguje raw `*1`).
3. Multi-value pole (např. seznamy oddělené čárkou) — jak REST přijímá/vrací (string s čárkami? pole?).
4. `.detail` a `.proplist` interakce; chování `?.proplist` vs `.query` proplist u GET.
5. `once` přesný formát pro monitor endpointy (`{"once":""}` vs query `?once`).
6. Chování `PATCH null` vs `""` na číselných/enum polích (default vs prázdno) — §4 limitation.

---

## 9. Dopady na plán

- **Oprava mapování:** add = **PUT** (ne POST); POST jen pro `/<command>` endpointy. Promítnuto do
  [A-rest-implementation-plan.md §5.1](A-rest-implementation-plan.md).
- `ExecuteScalar` u `Save` (čtení nového `.id`): PUT vrací celý objekt → `.id` číst z těla odpovědi.
- `/unset` mapperu → `PATCH {field:null}` (s known-limitation poznámkou).
- `/move` → `POST /<path>/move {".id"|"numbers", "destination"}`.
