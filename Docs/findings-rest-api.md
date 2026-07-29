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

### 5.1 Session accounting — REST session žije nad TCP a nikdy se neodhlásí (✅ 7.23.2, 2026-07-28/29)

**Je to potvrzený bug RouterOS, ne náš.** Hlášený na fóru pro 7.16 až 7.24rc1 (mj. 7.22, 7.22.1,
7.23beta2, 7.23rc1, 7.23.1, **7.23.2** = náš router, 7.24rc1), čtyři support tickety
(SUP-214490, SUP-218559, SUP-219610, SUP-219529), bez řešení a bez workaroundu; jednou byl označen za
opravený v 7.16 a opravený není. Viz
[forum thread](https://forum.mikrotik.com/t/users-logged-in-via-rest-api-shown-in-active-users-do-not-disappear/269432).
Oficiální stránka [REST API](https://help.mikrotik.com/docs/spaces/ROS/pages/47579162/REST+API)
o životním cyklu session **neříká vůbec nic** — jediný timeout, který zmiňuje, je 60 s na běh příkazu.

**Model chování (naměřeno):** router si drží session pro dvojici (user, source-address) a další requesty
ji **recyklují** — nový login se nezaloguje a nový řádek nevznikne. Za posledních ~50 requestů
(sériově, paralelně, s `Connection: close` i bez) router nezalogoval **ani jeden** rest-api login.
Občas ale novou session přesto založí, a ta stará zůstane viset navždy. Přesně to popisuje i fórum
("it seems to reuse the session occasionally"). Proto řádky nepřibývají po requestech, ale po dnech.

Každý takový login založí **dva** řádky: `via=rest-api` a `via=api` se stejným časem. Ten `api` řádek
nedělá klient — je to interní www→api backend routeru (v logu se pozná tím, že nemá adresu:
`user admin logged in via api` bez `from …`).

#### Co session NEUKONČÍ (všechno ověřeno, všechno tři vyvrácené)

| Údajný mechanismus | Výsledek |
|---|---|
| HTTP hlavička `Connection: close` | ❌ **Nic.** 20 requestů s `Connection: close` → 0 nových řádků a 0 nových loginů, tzn. jelo se po recyklované session; žádný řádek to taky neuvolnilo. |
| Zavření socketu / zánik klienta (`Dispose`, konec procesu) | ❌ **Nic.** Pár po jednom `curl` byl v tabulce i 90 s po skončení curlu a `Get-NetTCPConnection` na hostu neukazoval **žádné** spojení na 80/443. Session žije nad TCP vrstvou. |
| Inactivity timeout na routeru | ❌ **Neexistuje.** Nejstarší řádek žil **~24 hodin** a nezmizel. Za 33 min nepřetržitého sledování 12 řádků zmizel přesně jeden — `api` polovina páru po ~10 min — zatímco její `rest-api` polovina tam byla i po 25 min. Poloviny páru nesdílí ani stejné pravidlo; skutečné pravidlo **zjištěno nebylo** a nehádat ho. |

`/user/active/remove` je odmítne (`action failed (6)` — fórum hlásí stejnou chybu). Spolehlivě je smaže
až reboot.

#### Ex-post identifikace — jde, ale ne přes ID

Klient **žádné session ID nedostane**: odpověď nese jen `Cache-Control / Connection / Content-Length /
Content-Type / Date / Expires / X-Frame-Options` — **žádnou cookie, žádnou session hlavičku** (ověřeno
`curl -D -`). `/user/active` má jen `.id, when, name, address, via, group, radius`, takže jediný
korelátor je IP klienta a čas — session lze přiřadit hostu, ne procesu ani spojení.

Zato to prozradí **router sám ve svém logu**, topic `account` (na default konfiguraci ho chytá pravidlo
`info`, takže se nemusí nic zapínat):

```
/log print where message~"rest-api"
    user admin logged in from 192.168.4.31 via rest-api
```

Napočítáno na živém logu: **`rest-api`: 4× logged in, 0× logged out** — proti `api` 81/74 a `winbox`
317/318, které sedí. Odhlášení se u REST **nezaloguje nikdy**, a to je ten ex-post signál: rozdíl mezi
počtem loginů a logoutů per `via`.

#### Praktický důsledek

**Počet řádků v `/user/active` neměří nic o klientovi.** Naměřených 164 řádků (109 `api` + 55 `rest-api`)
v P2.35 sedí přesně na ten poměr ~2:1 a je to účetnictví routeru — close path je v tik4netu čistý na
všech transportech (viz `UserActiveSessionProbeTest`). V `RestConnection.Close()` není co opravit;
cokoli, co by tam kdo přidal, by na tohle nemělo vliv.

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
