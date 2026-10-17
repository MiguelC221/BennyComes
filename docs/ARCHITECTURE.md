# BennyComes - Arquitectura y Principios de Diseño

BennyComes es un reproductor de música de alto rendimiento desarrollado con **.NET 8**, **WPF** y **WPF-UI (Fluent Design / Windows 11)**, concebido bajo los principios de **Clean Architecture**, **SOLID** y el patrón **MVVM (Model-View-ViewModel)** con CommunityToolkit.Mvvm.

---

## 1. Estructura de Capas (Layered Architecture)

```
BenyComes/
│
├── Presentation (Vistas y XAML)
│   ├── MainWindow.xaml (.cs)      # Shell principal, barra de título personalizada y MiniPlayer
│   └── Views/
│       ├── MyMusicPage.xaml       # Colección musical (Canciones, Artistas, Álbumes, vistas de detalle)
│       ├── NowPlayingView.xaml    # Modo inmersivo con visualizadores y Ken Burns
│       ├── PlaylistsPage.xaml     # Gestión y detalle de listas de reproducción
│       ├── SearchPage.xaml        # Búsqueda instantánea en catálogo
│       └── SettingsPage.xaml      # Preferencias, carpetas, audio y licencias
│
├── Presentation Logic (ViewModels)
│   ├── MainViewModel.cs           # Orquestador global, navegación, reproducción activa
│   ├── MyMusicViewModel.cs        # Lógica de pestañas, ordenación, JumpList, detalle de artista y álbum
│   ├── NowPlayingViewModel.cs     # Estado cinemático, cola expandible, fondos de artista
│   ├── PlaylistsViewModel.cs      # Lógica de listas de reproducción y sugerencias
│   ├── SettingsViewModel.cs       # Configuración del sistema, audio e indexación
│   └── ThemeViewModel.cs          # Sistema dinámico de temas y acentos de color
│
├── Domain (Modelos de Dominio)
│   ├── Track.cs                   # Entidad canción con metadatos técnicos y carátula
│   ├── AlbumModel.cs              # Entidad álbum con agrupación de pistas y años
│   ├── ArtistModel.cs             # Entidad artista con discografía, biografía y avatar
│   └── Playlist.cs                # Entidad lista de reproducción y pistas contenidas
│
├── Providers & Services (Servicios de Aplicación)
│   ├── Audio/
│   │   ├── AudioPlayerService.cs  # Motor de audio BASS / NAudio, EQ, WASAPI
│   │   ├── DeviceSelectorViewModel.cs # Gestión de dispositivos de salida de audio
│   │   └── LastFmScrobbler.cs     # Scrobbling automático de pistas
│   ├── Online/
│   │   ├── LastFmService.cs       # Extracción robusta de biografías e imágenes (Scraping + API)
│   │   └── OnlineSearchService.cs # Servicios de metadatos remotos
│   ├── Metadata/
│   │   └── MetadataService.cs     # Extracción TagLib-Sharp, thumbnailing y caching
│   └── Library/
│       └── LibraryCacheService.cs # Cache en memoria indexada para carga instantánea
│
└── Data Access Layer (Persistencia y SQLite)
    └── Data/
        ├── LibraryDao.cs          # DAO optimizado SQLite con transacciones WAL
        └── PlaylistDao.cs         # Persistencia de listas de reproducción y relaciones
```

---

## 2. Principios de Clean Code y Buenas Prácticas

1. **Separación de Responsabilidades (SRP):**
   - Las vistas XAML no contienen lógica de negocio; se limitan a data binding y animaciones de interfaz.
   - Los ViewModels implementan `ObservableObject` y delegan tareas intensivas de I/O a servicios y DAOs en hilos secundarios (`Task.Run`).

2. **Inversión de Control y Providers:**
   - Servicios fundamentales (`AudioPlayerService`, `LastFmService`, `LibraryCacheService`) operan como Providers independientes con instancias singleton o inyección directa, facilitando pruebas y reemplazos sin acoplamiento.

3. **Prevención de Fugas de Memoria (Memory Leak Prevention):**
   - **Gestión de Recursos Multimedia:** Descarga y decodificación de imágenes valida cabeceras (`BitmapDecoder`), usa archivos temporales `.tmp` atómicos y libera memoria en segundo plano mediante `GC.Collect` / `BitmapCacheOption.OnLoad`.
   - **Desacoplamiento de Eventos:** Los eventos de servicios globales se desuscriben al descargar vistas (`Loaded`/`Unloaded`) para evitar referencias vivas retenidas.

4. **Experiencia de Usuario Fluida (60 FPS) y Carga Asíncrona:**
   - **Carga No Bloqueante en Segundo Plano:** Las propiedades de imagen (`CoverImageSource` y `AvatarImageSource`) entregan respuesta inmediata al hilo de UI, delegando la decodificación de miniaturas a hilos en segundo plano (`Task.Run`) para prevenir congelamientos ("trabones") de la interfaz.
   - **Caché Caliente en Navegación Interna:** Las miniaturas decodificadas se conservan en memoria mientras el usuario navega entre pestañas de su biblioteca, evitando lecturas redundantes y liberándose únicamente al salir de la vista completa de música o ante eventos de purga del sistema.
   - **Transiciones Cinemáticas Estandarizadas:** Duración unificada a **1.0 segundo** (`Duration="0:0:1.0"`).
   - **Barras de Desplazamiento Universales Estilo Fluent con Altura Mínima Garantizada:** Tirador en cápsula redondeada (`CornerRadius="3"`, expansión en hover) con flechas discretas arriba (`▲`) y abajo (`▼`). Se implementa `ScrollBarMinThumbViewportConverter` para garantizar que el cursor mantenga un mínimo de **56px** (`MinHeight="56"` vertical y `MinWidth="56"` horizontal) sin importar si la biblioteca contiene cientos o decenas de miles de canciones.

5. **Reproducción en Curso Inmersiva y Cola Desacoplada de Barra de Título:**
   - La barra de título de la aplicación (`Grid.Row="0"`, `Panel.ZIndex="200"`) aloja el botón "Volver a la biblioteca" de forma desacoplada de la vista inmersiva `NowPlayingView` (`Panel.ZIndex="100"`). Esto evita interferencias visuales y de interacción cuando se expande la cola de reproducción activa (`QueueExpandedContainer`).
   - La cola activa cuenta con una capa de fondo (`QueueBackdropOverlay`) animada mediante transiciones de opacidad al **90%** (`Opacity="0.9"`), asegurando contraste y legibilidad óptima para los controles y listados de pistas sin perder la ambientación visual del artista en reproducción.

6. **Gestión de Artistas Colaboradores y Scraping de Alta Resolución:**
   - **Desglose de Colaboraciones:** [`ArtistSplitterService`](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/Services/Library/ArtistSplitterService.cs) identifica participantes individuales y excepciones protegidas (ej. *AC/DC*, *Earth, Wind & Fire*).
   - **Sincronización Bidireccional de Galería:** Tanto en el panel de personalización de `NowPlayingView` como en el encabezado del modal de fotos, la selección de artista se encuentra sincronizada de forma bidireccional, permitiendo inspeccionar, omitir y fijar como principal fotos de cada colaborador individual.
   - **Scraping Directo de Máxima Resolución (`/ar0/`):** [`LastFmService`](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/Services/Online/LastFmService.cs) aplica scraping optimizado mediante codificación de URLs con `System.Net.WebUtility.UrlEncode` (`quote_plus`), cabecera de escritorio Chrome y conversión automática de miniaturas al formato nativo sin recortes (`/i/u/ar0/`) en la red de entrega Fastly CDN.

7. **Enriquecimiento Masivo de Artistas en Segundo Plano (`ArtistEnrichmentService`):**
   - **Abstracción de Imágenes (`IArtistImageProvider`):** La descarga de fotos está desacoplada de la cola masiva mediante la interfaz `IArtistImageProvider`, implementada inicialmente por `DeezerImageProvider` con validación estricta de nombres normalizados (sin tildes, signos ni mayúsculas).
   - **Extracción Estricta vía API Oficial:** La cola masiva no realiza scraping web; consume exclusivamente el endpoint oficial `artist.getinfo` de Last.fm con validación de coincidencia de nombres para descartar autocorrecciones incorrectas.
   - **Gestión de Concurrencia y Resiliencia:** Máximo 1 artista procesado a la vez (`SemaphoreSlim(1)`), límite de tasa con retardo de ~260ms ($\le 4$ req/s), reintentos con retroceso exponencial ante 429/5xx, y detección de fallos generales de red (pausa automática sin quemar caché negativa).
   - **Caché Negativa Persistente:** Se guarda un marcador `not_found.txt` con fecha UTC para artistas inexistentes, evitando reintentos redundantes durante 30 días a menos que el usuario fuerce una actualización manual.
   - **Sincronización Reactiva con la UI:** Tras completar cada artista, se notifica a la interfaz mediante `Dispatcher.InvokeAsync(..., DispatcherPriority.Background)`, actualizando al instante el avatar y la biografía en el modelo en memoria sin interferir con la navegación o la reproducción musical.


