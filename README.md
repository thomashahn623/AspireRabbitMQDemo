# Aspire RabbitMQ + MassTransit Outbox Demo

Dieses Repository demonstriert eine verteilte .NET 9 Anwendung mit [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/), RabbitMQ als Message Broker, PostgreSQL als Persistenz für eine MassTransit EF Core Outbox sowie getrennten Diensten für Publishing und Consuming.

## Architekturüberblick
Komponenten:
1. AppHost (`AspireRabbitMQDemo`) – Orchestriert Ressourcen (RabbitMQ, PostgreSQL, Projekte).
2. `MessagePublisher` – Generiert periodisch Events (`SomethingHappened`) und nutzt die MassTransit EF Core Outbox (Bus Outbox) gegen PostgreSQL.
3. `MessageConsumer` – Konsumiert die Events aus einer benannten Queue (`something-happened-queue`). Aktuell mit In-Memory Outbox (für idempotente internen Publish) – optional erweiterbar um Inbox/Persistenz.
4. `Contracts` – Enthält die gemeinsam genutzten Message Contracts (Record `SomethingHappened`).

## Wichtige Features
- RabbitMQ Container (inkl. Management Plugin) wird durch Aspire gemanagt.
- PostgreSQL Datenbank (`outboxdb`) für persistente MassTransit Outbox.
- MassTransit 8.x mit Bus Outbox (garantiert: Publish erst nach DB-Commit → größere Zuverlässigkeit bei Abstürzen).
- Automatisches Warten auf abhängige Ressourcen (`WaitFor`).
- Erweiterbar um Inbox, Sagas, OpenTelemetry, Health Checks.

## Voraussetzungen
- [.NET SDK 9.0](https://dotnet.microsoft.com/download)
- Docker Desktop (oder kompatibler Container Runtime)
- (Optional) VS Code + C# Dev Kit oder Visual Studio 2022 17.10+

## Start (Schnell)
```bash
dotnet restore
dotnet run --project AspireRabbitMQDemo/AspireRabbitMQDemo.AppHost.csproj
```
Die Aspire-Konsole zeigt URLs (Dashboard + Ports). Beende mit `Ctrl+C`.

## RabbitMQ Zugriff
- AMQP: Standard-Port 5672 (falls nicht von Aspire neu gemappt; Ausgabe prüfen)
- Management UI: `http://localhost:15672` (Benutzer/Pass siehe Parameter oder Secrets)

## Konfigurierbare Parameter (AppHost)
| Parameter | Default | Zweck |
|-----------|---------|-------|
| `RabbitMQUser` | guest | Broker User |
| `RabbitMQPassword` | guest | Broker Passwort |

### Anpassen via User-Secrets
```bash
cd AspireRabbitMQDemo
dotnet user-secrets set RabbitMQUser myuser
dotnet user-secrets set RabbitMQPassword mypass
```

### Oder per Umgebungsvariablen
```bash
export RabbitMQUser=myuser
export RabbitMQPassword=mypass
dotnet run --project AspireRabbitMQDemo/AspireRabbitMQDemo.AppHost.csproj
```

## Projektstruktur (Auszug)
- `AspireRabbitMQDemo/Program.cs` – Definition von RabbitMQ + PostgreSQL Ressourcen und Projektreferenzen.
- `MessagePublisher/Program.cs` – Konfiguration MassTransit (RabbitMQ) + EF Core Outbox (`UsePostgres`), Worker der Events erzeugt.
- `MessagePublisher/OutboxDbContext.cs` – DbContext inkl. Outbox-/Inbox-Entity-Mappings + Heartbeat-Demo-Tabelle.
- `MessageConsumer/Program.cs` – Consumer mit definierter ReceiveEndpoint Queue und In-Memory Outbox.
- `Contracts/SomethingHappened.cs` – Event-Contract.

## Outbox Funktionsweise
1. Worker erstellt Event.
2. `publish.Publish(...)` wird durch den EF Outbox Interceptor abgefangen.
3. Beim `SaveChanges()` landen Outbox-Nachrichten + Heartbeat in PostgreSQL.
4. Der Outbox Dispatcher pollt (`QueryDelay`) und veröffentlicht Messages an RabbitMQ.
5. Consumer verarbeitet Events aus Queue `something-happened-queue`.

### Warum Heartbeat-Tabelle?
In dieser Demo gibt es keine echte Domain-Aggregatänderung. Um pro Schleife einen Commit zu erzwingen, wird ein Heartbeat-Datensatz eingefügt. In realen Anwendungen ersetzt durch echte Domain Writes.

## Outbox vs. Inbox – Konzepte kurz erklärt

### Outbox Pattern
Das Outbox Pattern löst das „Dual Write Problem“: Du möchtest innerhalb einer lokalen Transaktion (z.B. DB Update) zusätzlich eine Nachricht an RabbitMQ senden – beides muss konsistent sein. Statt direkt an den Broker zu senden, wird die Nachricht zuerst zuverlässig im selben Datenbankspeicher (Outbox-Tabelle) festgehalten. Ein separater Dispatcher publiziert sie anschließend asynchron.

Typischer Ablauf:
1. Start einer DB-Transaktion.
2. Domain-Änderungen (INSERT/UPDATE) + Schreiben der Outbox-Message-Row.
3. Commit – jetzt sind Domainzustand und ausstehende Nachricht gemeinsam durable.
4. Hintergrundprozess liest Outbox-Tabellenzeilen (Polling) und publiziert an RabbitMQ.
5. Nach erfolgreichem Publish wird die Row als verarbeitet markiert / gelöscht.

Vorteile:
- Verhindert „verlorene“ Messages bei Crash zwischen Domain-Commit und Publish.
- Keine 2-Phase-Commit Infrastruktur nötig.
- Ermöglicht Replay/Recovery falls der Broker temporär nicht erreichbar ist.

Varianten in MassTransit:
- Bus Outbox (hier benutzt): Abfangen von `IPublish/ISend` innerhalb eines EF-DbContext-Scopes.
- In-Memory Outbox: Nur im Speicher (volatile), nützlich für idempotente Publish-Aufrufe innerhalb einer Consumer-Verarbeitung.
- Persistente EF Outbox: Haltbar, transaktionssicher.

### Inbox Pattern
Die Inbox adressiert das Gegenstück beim Konsumieren: „Was, wenn dieselbe Nachricht (redelivery / duplicate) mehrmals zugestellt wird?“ Ohne Schutz könnten Nebenwirkungen (z.B. Geld abbuchen, Mail verschicken) doppelt passieren.

Prinzip:
1. Beim Eintreffen einer Message prüft der Consumer (innerhalb einer DB-Transaktion) eine Inbox-Tabelle, ob die MessageId schon verarbeitet wurde.
2. Falls nein: Geschäftslogik ausführen, MessageId + Status in Inbox speichern, committen.
3. Falls ja: Verarbeitung überspringen (idempotent).

Ergebnis: „At-least-once“ vom Broker + Inbox = „effektiv exactly-once“ für deine Domänensemantik (sofern die Operation selbst idempotent oder durch die Inbox geschützt ist).

### Zusammenspiel Outbox + Inbox
| Schritt | Outbox | Inbox |
|--------|--------|-------|
| Producer speichert Event | JA (OutboxRow) | – |
| Producer stürzt vor Publish | Event bleibt erhalten | – |
| Dispatcher publiziert | JA | – |
| Broker liefert evtl. doppelt | – | JA (Dedup) |
| Consumer führt Seiteneffekt aus | – | Einmalig |

Mit beiden Patterns erreichst du Ende-zu-Ende Zuverlässigkeit ohne Distributed Transactions.

### Wann welches Pattern?
- Nur Outbox: Du willst zuverlässiges Publish (kein Verlust), Empfängerseite toleriert Duplikate oder hat eigene Idempotenz.
- Nur Inbox: Du konsumierst von einem System, dem du vertraust (selten Verluste), willst aber Duplikate sicher vermeiden.
- Beide: Kritische Workflows (Zahlung, Inventar, Buchhaltung) mit strenger Konsistenzanforderung.

### Wichtige Hinweise
- Outbox garantiert nicht automatisch Reihenfolge über alle Aggregat-Typen hinweg – nur innerhalb des commit-/dispatch-Flows.
- Inbox-Tabellen müssen regelmäßig „aufgeräumt“ werden (Retention / TTL), sonst wachsen sie unendlich.
- Idempotenz-Schlüssel: Standard ist MessageId. Bei zusammengesetzten Keys (Partition + Id) ggf. zusätzliche Spalten modellieren.

## Häufige Probleme & Lösungen
| Problem | Ursache | Lösung |
|---------|---------|--------|
| Keine Events beim Consumer | Outbox nicht committet | Sicherstellen, dass `SaveChangesAsync()` ausgeführt wird (hier: Heartbeat). |
| Alte Sqlite-Datei / Readonly | Wechsel auf Postgres durchgeführt | (Erledigt) |
| RabbitMQ Queue fehlt | Falscher Queue-Name | Der Consumer deklariert `something-happened-queue`. |
| Duplicate Events | Kein Outbox/Inbox Schutz | Outbox aktiv, Inbox optional ergänzen. |

## Migrations (Empfehlung)
Aktuell verwendet der Publisher `EnsureCreated()` für Demo-Zwecke. Für Produktion:
```bash
dotnet ef migrations add InitialOutbox -p MessagePublisher/MessagePublisher.csproj -s MessagePublisher/MessagePublisher.csproj
dotnet ef database update -p MessagePublisher/MessagePublisher.csproj -s MessagePublisher/MessagePublisher.csproj
```
(Falls das EF Tool fehlt: `dotnet tool install --global dotnet-ef`.)

## Nächste Erweiterungen (Ideen)
- Inbox beim Consumer für persistente Deduplikation.
- OpenTelemetry Tracing + Metrics (RabbitMQ / EF / Outbox-Lag).
- Retry Policies & Circuit Breaker (MassTransit Middleware konfigurieren).
- Sagas oder State Machines (z.B. für Prozesskoordination).
- Health Checks (z.B. Prüfen auf Outbox-Lag > Threshold).

## Cleanup
Stoppe die Anwendung mit `Ctrl+C`; Container und Volumes bleiben standardmäßig erhalten (Postgres Daten bleiben erhalten). Zum Entfernen der Docker Artefakte (optional): Docker Dashboard oder `docker compose` Ressourcen, die Aspire erzeugt hat, bereinigen.

## Lizenz
Demo-Code zu Lern- und Experimentierzwecken. Keine Garantie.
