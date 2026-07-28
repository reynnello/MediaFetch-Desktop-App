# Маршрут изучения C# через MediaFetch

Порядок рассчитан примерно на 60–90 минут в день. После каждого блока полезно
изменить небольшую часть кода самостоятельно и только потом сверяться с готовой
реализацией.

## День 1 — модель предметной области

Изучить `Domain/DownloadJob.cs` и сравнить с Java:

- C#-properties вместо JavaBean getters/setters;
- `record` для неизменяемых DTO и обычный `class` для сущности с поведением;
- nullable reference types (`string?`) — подсказки компилятора, а не `Optional`;
- pattern matching в проверке переходов состояний;
- `DateTimeOffset` вместо неоднозначного локального `DateTime`.

Задание: добавить метод повтора `Failed`-задачи, не разрешая повтор canceled job.

## День 2 — LINQ и EF Core

Изучить `Data/MediaFetchDbContext.cs`, миграцию и recovery-код worker:

- LINQ похож на Stream API, но является частью языка и может переводиться в SQL;
- `DbContext` примерно соответствует unit of work поверх JPA/Hibernate;
- `DbSet<T>` — вход в запросы к сущности;
- не каждый .NET-тип переводится SQLite-провайдером: поэтому сортировка
  `DateTimeOffset` восстановленной очереди выполняется после загрузки в память.

Задание: добавить `GET /api/downloads?status=Completed` с сортировкой по созданию.

## День 3 — ASP.NET Core

Изучить `Program.cs` и контроллеры:

- `builder.Services` играет роль Spring application context;
- constructor injection работает без `@Autowired`;
- `[ApiController]`, `[HttpPost]` и `[Route]` близки к Spring MVC annotations;
- DTO отделены от EF-сущности;
- `ActionResult<T>` явно описывает варианты HTTP-ответа.

Задание: добавить endpoint для последних десяти задач.

## День 4 — async/await и отмена

Изучить `IYtDlpRunner`, контроллеры и `DownloadCancellationRegistry`:

- `Task<T>` близок к `CompletableFuture<T>`, но `await` сохраняет линейный код;
- `CancellationToken` передаётся явно по всей цепочке;
- `using`/`await using` детерминированно освобождает ресурсы;
- отмена не равна ошибке и имеет собственное состояние.

Задание: написать unit-тест отмены активной задачи.

## День 5 — внешние процессы

Изучить `YtDlpRunner.cs` и `YtDlpOutputParser.cs`:

- аргументы добавляются через `ArgumentList`, поэтому shell не интерпретирует URL;
- stdout и stderr читаются параллельно во избежание deadlock;
- JSON разбирается через `System.Text.Json`;
- процесс и его дочерние процессы завершаются при отмене.

Задание: добавить тесты испорченного JSON и неизвестной строки прогресса.

## День 6 — очередь и BackgroundService

Изучить `DownloadQueue.cs` и `DownloadWorker.cs`:

- `Channel<T>` — асинхронная producer/consumer очередь;
- bounded capacity создаёт backpressure;
- один consumer гарантирует только одну одновременную загрузку;
- scoped `DbContext` создаётся внутри singleton background worker.

Задание: добавить конфигурируемое число параллельных worker-ов.

## День 7 — ошибки и безопасность

Изучить `MediaUrlValidator`, `ApiExceptionHandler` и выдачу файла:

- Problem Details унифицирует ошибки API;
- DNS-проверка блокирует очевидные SSRF-адреса;
- `Path.GetRelativePath` не позволяет выдать файл вне output-каталога;
- размер и формат результата ограничиваются конфигурацией.

Задание: добавить middleware rate limiting для локальных клиентов.

## День 8 — тесты

Изучить `MediaFetch.Tests`:

- unit-тесты проверяют чистую доменную логику;
- `WebApplicationFactory<Program>` поднимает настоящий HTTP pipeline;
- внешние инструменты заменяются fake-реализацией через DI;
- shared in-memory SQLite сохраняет реальное поведение relational provider.

Задание: добавить интеграционный тест `DELETE`, который дожидается состояния
`Canceled`.

## Java → C# коротко

| Java | C#/.NET |
|---|---|
| package | namespace |
| Maven/Gradle dependency | NuGet `PackageReference` |
| getter/setter | property |
| Stream API | LINQ |
| `CompletableFuture` | `Task` + `async/await` |
| `Optional<T>` | чаще nullable `T?` |
| Spring DI | встроенный `IServiceCollection` |
| `@RestController` | `[ApiController]` |
| JPA/Hibernate | EF Core |
| `ExecutorService`/queue | `BackgroundService` + `Channel<T>` |
| try-with-resources | `using` / `await using` |
