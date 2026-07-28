# MediaFetch API

Учебный локальный API на C# и .NET 10. Он получает метаданные публичной
медиассылки, ставит загрузку в последовательную очередь и возвращает готовый
видео- или аудиофайл.

Проект рассчитан на контент, который вам принадлежит или для скачивания которого
у вас есть разрешение. Он не обходит DRM, авторизацию, paywall и другие способы
защиты.

## Что используется

- ASP.NET Core Controllers и встроенный dependency injection;
- EF Core 10 и SQLite с миграциями;
- `BackgroundService` и bounded `Channel<Guid>`;
- безопасный запуск `yt-dlp` через `ProcessStartInfo.ArgumentList`;
- `async/await`, `CancellationToken` и потоковая выдача файлов;
- Swagger UI и xUnit-интеграционные тесты.

YouTube является основным проверяемым источником. Другие публичные сайты,
поддерживаемые `yt-dlp`, работают в режиме best effort и могут переставать
работать после изменений на стороне платформы.

## Требования

- [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0);
- [yt-dlp](https://github.com/yt-dlp/yt-dlp#installation);
- [ffmpeg](https://ffmpeg.org/download.html).

Пример установки на Windows:

```powershell
winget install yt-dlp.yt-dlp
winget install Gyan.FFmpeg
yt-dlp --version
ffmpeg -version
```

Если бинарники не находятся через `PATH`, укажите полные пути в
`appsettings.json`:

```json
{
  "MediaFetch": {
    "YtDlpPath": "C:\\tools\\yt-dlp.exe",
    "FfmpegPath": "C:\\tools\\ffmpeg.exe"
  }
}
```

## Запуск

```powershell
dotnet tool restore
dotnet restore
dotnet run
```

Приложение слушает только `http://127.0.0.1:5080`. В development-режиме
Swagger UI доступен по адресу
[http://127.0.0.1:5080/swagger](http://127.0.0.1:5080/swagger).

При первом запуске автоматически создаются `.mediafetch-data/mediafetch.db`, каталог
`downloads` и применяется миграция EF Core.

Готовые запросы находятся в [MediaFetch.http](MediaFetch.http). Минимальный
пример создания задачи:

```http
POST http://127.0.0.1:5080/api/downloads
Content-Type: application/json

{
  "url": "https://www.youtube.com/watch?v=...",
  "mode": "Video",
  "maxHeight": 720
}
```

Ответ имеет статус `202 Accepted`. Поле `id` используется для проверки:

```http
GET /api/downloads/{id}
GET /api/downloads/{id}/file
DELETE /api/downloads/{id}
```

Для аудио передайте `"mode": "Audio"` и `"audioFormat": "Mp3"` либо `"M4a"`.
Для видео поддерживаются 360, 720 и 1080; значение по умолчанию — 720.
Максимальный размер результата по умолчанию — 1 GiB.

## Тесты и миграции

```powershell
dotnet test .\MediaFetch.Tests\MediaFetch.Tests.csproj
dotnet tool run dotnet-ef migrations list
```

Тесты не обращаются к YouTube и не запускают `yt-dlp`: внешний процесс заменён
fake-реализацией. Интеграционные тесты используют настоящие ASP.NET test-host,
EF Core и shared in-memory SQLite.

Чтобы создать следующую миграцию:

```powershell
dotnet tool run dotnet-ef migrations add MigrationName --output-dir Data\Migrations
```

## Ограничения безопасности

- принимаются только абсолютные HTTP/HTTPS URL без встроенных credentials;
- DNS-имена, ведущие на loopback, private, link-local или multicast адреса,
  блокируются;
- URL передаётся отдельным аргументом процесса, а не склеивается в shell-строку;
- имя выходного файла формируется из ID задачи;
- выдаются только файлы внутри настроенного каталога `downloads`;
- API намеренно привязан к loopback и не имеет пользовательской авторизации.

DNS-проверка снижает риск SSRF, но не является достаточной защитой для публичного
развёртывания из-за DNS rebinding. Перед публикацией API потребуется сетевой
egress-фильтр, авторизация, rate limit, квоты и изоляция процесса.
