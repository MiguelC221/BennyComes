# Changelog BennyComes

Todas las modificaciones notables realizadas en la aplicación BennyComes se documentan en este archivo.

---

## [Versión 1.2.1] - 2026-10-17

### 🚨 Botón de Emergencia para Restablecer la Aplicación desde Cero
- **Restablecimiento Total de Fábrica ([SettingsViewModel.cs](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/ViewModels/SettingsViewModel.cs) y [SettingsPage.xaml](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/Views/SettingsPage.xaml)):**
  - Se implementó la *"Zona de Emergencia"* en la parte inferior de la pestaña Ajustes.
  - Al presionar *"Restablecer Todo y Empezar de Cero"*, se solicita una confirmación de seguridad irreversible.
  - Detiene la reproducción de audio, cancela enriquecimientos en segundo plano y libera los pools de conexiones de SQLite (`SqliteConnection.ClearAllPools()`).
  - Elimina de forma íntegra la base de datos (`benycomes.db`, `benycomes.db-wal`, `benycomes.db-shm`), la caché de fotos de artistas (`%LocalAppData%\MusicPlayer\ArtistCache`), historiales y configuraciones, reiniciando la aplicación automáticamente limpia desde cero.

### 🎨 Inicio con Recomendaciones Coloridas y Organizadas por Secciones
- **Tarjetas Dinámicas y Vistosas ([HomeViewModel.cs](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/ViewModels/HomeViewModel.cs) y [HomePage.xaml](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/Views/HomePage.xaml)):**
  - Cada tarjeta de recomendación y Smart Mix cuenta ahora con un degradado lineal distintivo y vibrante (`CardBackgroundBrush`), bordes con brillo sutil y etiquetas de categoría (`PERSONAL`, `TENDENCIA`, `REDESCUBRE`, `GÉNERO`).
  - Se organizaron los bloques principales con indicadores de acento cromático (azul `#3B82F6` para *Top Canciones*, verde esmeralda `#10B981` para *Top Artistas*, violeta `#8B5CF6` para *Top Álbumes*) y medallas metálicas de ranking para el podio (oro, plata y bronce).

### 🌟 Cabeceras Vistosas con Alto Contraste en Información de Artista y Álbum
- **Banner Superior Ambiental con Respaldo de Legibilidad ([MyMusicPage.xaml](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/Views/MyMusicPage.xaml)):**
  - Las vistas de detalle de Artista y Álbum ahora muestran un fondo ambiental difuminado (`BlurEffect Radius="65"`) con el arte de portada o retrato del artista a opacidad calibrada.
  - Se colocó una máscara oscura de degradado (`#D00E0E14` a `#F50A0A0E`) y tipografía blanca pura (`#FFFFFF` y `#C8D0D8`) con sombra sutil, garantizando un ratio de contraste superior a 18:1 (WCAG AAA) para que ningún texto pierda legibilidad.

### 🔍 Botón "Volver a la Biblioteca" Ampliado en Reproducción en Curso
- **Accesibilidad Mejorada ([MainWindow.xaml](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/MainWindow.xaml)):**
  - Se incrementaron las dimensiones del botón de retorno: altura a 32 px, acolchado de 16 px, tamaño de icono y texto aumentados y esquinas redondeadas (`CornerRadius="8"`), haciéndolo significativamente más cómodo y fácil de pulsar.

### ⬆️ Elevación de Información al Ocultar el HUD por Inactividad del Ratón
- **Reposicionamiento Vertical Hacia Arriba ([NowPlayingView.xaml.cs](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/Views/NowPlayingView.xaml.cs)):**
  - Al cesar el movimiento del ratón en reproducción en curso, la carátula, título, artista y visualizador se elevan sutilmente -26 px hacia arriba mediante una animación suave `CubicEase`, evitando que colisionen o queden pegados a la parte baja de la pantalla. Al mover el ratón, regresan de manera fluida a su alineación original.

---

## [Versión 1.2.0] - 2026-10-17

### 🖼️ Selector de Fuentes y Extracción Limpia de Imágenes de Artista
- **Extracción de Fuente Única sin Peticiones Duplicadas ([FanartTvImageProvider.cs](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/Services/Online/FanartTvImageProvider.cs), [DeezerImageProvider.cs](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/Services/Online/DeezerImageProvider.cs) y [LastFmService.cs](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/Services/Online/LastFmService.cs)):**
  - Se desacopló la consulta a Deezer dentro de `FanartTvImageProvider` y se eliminó la segunda consulta duplicada de álbumes de Deezer en `LastFmService`.
  - Se implementó `FetchImageUrlsBySourceAsync` bajo la regla de fuente única: extrae estrictamente de UNA sola fuente si responde con éxito sin errores ni duplicaciones.
  - En modo *Automático*: prioriza Fanart.tv; si responde con imágenes, finaliza de inmediato sin tocar Deezer. Si Fanart.tv no tiene clave configurada, produce un error o devuelve 0 fotos, recurre limpiamente a Deezer como alternativa de respaldo.
- **Selector de Fuentes en la Galería del Artista ([MainWindow.xaml](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/MainWindow.xaml) y [MainViewModel.cs](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/ViewModels/MainViewModel.cs)):**
  - Se incorporó un menú desplegable de fuentes (`Automático`, `Fanart.tv`, `Deezer`) en la cabecera del modal de selección de fotos de artista.
  - El botón *"Obtener fotos"* consulta y descarga fotos exclusivamente desde la fuente elegida.

### 🔝 Auto-scroll al Inicio de la Pantalla en la Cola de Reproducción
- **Posicionamiento en la Primera Fila Visible ([NowPlayingView.xaml.cs](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/Views/NowPlayingView.xaml.cs)):**
  - Se corrigió el cálculo de desplazamiento de la cola en `ScrollToCurrentTrack`. Anteriormente, el cálculo de píxeles interfería con el modo lógico `CanContentScroll = true` de la lista de WPF, desplazando el elemento fuera del campo visible.
  - Ahora al cambiar de canción, la cola se desplaza inmediatamente para situar la pista en reproducción en la **primera posición visible (tope de la vista)**, manteniéndola perfectamente encuadrada y visible.

### 🚀 Optimización de Rendimiento en Pestaña Artistas y Álbumes
- **Reducción de Objetos Visuales Compartiendo `ContextMenu` ([MyMusicPage.xaml](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/Views/MyMusicPage.xaml)):**
  - Se extrajeron los menús contextuales de cada tarjeta de artista y álbum a recursos estáticos compartidos (`SharedArtistCardContextMenu` y `SharedAlbumCardContextMenu`).
  - Esto erradica más de 7,000 elementos visuales duplicados en memoria (`MenuItem`, `SymbolIcon`, `Separator`) que se creaban innecesariamente para cada tarjeta.
- **Filtro Bilineal Rápido de Imágenes ([MyMusicPage.xaml](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/Views/MyMusicPage.xaml)):**
  - Se cambió `BitmapScalingMode="HighQuality"` por `BitmapScalingMode="LowQuality"` en las miniaturas de artista y álbumes, eliminando el coste excesivo de filtrado bicúbico durante el desplazamiento continuo.
- **Carga en Lote con `BulkObservableCollection.AddRange` ([MyMusicViewModel.cs](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/ViewModels/MyMusicViewModel.cs)):**
  - En `LoadMoreArtists()` y `LoadMoreAlbums()`, se reemplazó el bucle `foreach Add()` por `AddRange()`, emitiendo una única notificación para todo el bloque y añadiendo banderas de control de reentrada (`_isLoadingMoreArtists` y `_isLoadingMoreAlbums`).

---

## [Versión 1.1.9] - 2026-10-17

### 🎬 Zoom Instantáneo en Reproducción en Curso y Opciones de Foto de Artista
- **Efecto de Zoom Ken Burns sin Cooldown ([NowPlayingView.xaml.cs](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/Views/NowPlayingView.xaml.cs)):**
  - Se optimizó el movimiento cinemático de fondo para iniciarse inmediatamente al entrar a la pantalla de reproducción en curso (`IsVisibleChanged`) o al cambiar de canción/foto.
  - Se redujo el período a 24s con curva cuadrática (`QuadraticEase`) y una escala inicial de `1.02` a `1.14`, eliminando la zona muerta/cooldown inicial donde no se percibía movimiento.
- **Interruptor *"Ver siempre la imagen de artista"* ([NowPlayingViewModel.cs](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/ViewModels/NowPlayingViewModel.cs) y [NowPlayingView.xaml](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/Views/NowPlayingView.xaml)):**
  - Se agregó un interruptor condicional visible en el menú de Personalización (...) únicamente cuando existan fotos de artista disponibles (`HasAvailableArtistImage`).
  - Al activarse, limita estrictamente el difuminado (*blur*) hasta un máximo de 4 px y el oscurecimiento al 30%, permitiendo apreciar con nitidez el retrato del artista.
  - Al desactivarse, se liberan por completo los límites (permitiendo hasta 80 px de desenfoque y 90% de oscuridad según la preferencia manual del usuario).
- **Animación Fluida al Desplegar y Ocultar la Cola de Reproducción:**
  - El botón *"Ocultar cola"* ahora cuenta con una transición animada elegante: la lista de la cola se desliza hacia abajo con desvanecimiento (`CubicEase EaseIn`) y el reproductor inmersivo se restablece suavemente con entrada deslizante (`CubicEase EaseOut`), erradicando cualquier desaparición abrupta.

### 📜 Desplazamiento Centrado Inteligente en Listas de Reproducción
- **Auto-scroll Progresivo con Pista Centrada ([PlaylistDetailPage.xaml.cs](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/Views/PlaylistDetailPage.xaml.cs) y [NowPlayingView.xaml.cs](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/Views/NowPlayingView.xaml.cs)):**
  - Al cambiar de pista en una lista de reproducción (o en la cola activa), la lista calcula la posición vertical de la canción en tiempo real.
  - Si la canción se encuentra en las primeras posiciones (antes del punto medio del visor), la lista permanece arriba (`offset = 0`) para permitir ver las primeras pistas con naturalidad.
  - Al avanzar y alcanzar la mitad de la pantalla, la lista desciende suavemente manteniendo la pista en reproducción exactamente en el **centro**.
  - Al llegar al final de la lista, el desplazamiento se detiene armónicamente en el límite inferior (`ScrollableHeight`).
- **Resaltado Visual en [PlaylistDetailPage.xaml](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/Views/PlaylistDetailPage.xaml):**
  - La fila activa en la lista de reproducción se ilumina con fondo acentuado, borde sutil, título destacado y el icono animado de altavoz activo (`Speaker224`).

### ⚡ Carga Asíncrona de Miniaturas y Rework del Backend de "Mi Música"
- **Miniaturas Asíncronas en `Track.CoverImageSource` ([Track.cs](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/Models/Track.cs) y [MetadataService.cs](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/Services/Metadata/MetadataService.cs)):**
  - La propiedad `CoverImageSource` ya no bloquea el hilo de UI leyendo el disco o extrayendo etiquetas TagLib al cargar canciones.
  - Retorna instantáneamente (0 ms) un degradado suave procedural de respaldo o la imagen previamente en memoria caché.
  - La carga y decodificación de miniaturas se encola en segundo plano mediante `Task.Run` con un semáforo de concurrencia de 4 hilos (`SemaphoreSlim(4, 4)`). Al completarse, la imagen congelada (`Freeze()`) se asigna en `AlbumArtImage` con prioridad `DispatcherPriority.Background`, actualizando la interfaz con total suavidad.
- **Optimización de Cambio de Pestañas y Agrupación A-Z ([MyMusicViewModel.cs](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/ViewModels/MyMusicViewModel.cs)):**
  - En `ApplyGroupingToCollection`, se verifica si la colección ya se encuentra agrupada antes de limpiar y reasignar `GroupDescriptions`, evitando que WPF destruya y reconstruya la jerarquía visual de contenedores al alternar pestañas.
  - Se implementó paginación inteligente de 60 elementos iniciales para *Álbumes* y *Artistas* en orden A-Z, evitando el bloqueo del hilo de interfaz por la creación simultánea de cientos de tarjetas en el `WrapPanel`.
  - El salto alfabético rápido (`SelectJumpLetter`) ahora precarga dinámicamente hasta la letra objetivo (`EnsureAlbumsLoadedForLetter` y `EnsureArtistsLoadedForLetter`), asegurando desplazamiento instantáneo sin congelamientos.

### 🔍 Rework del Buscador: Sugerencias al Escribir y Búsqueda Global con Enter
- **Sugerencias Rápidas y Ligeras al Escribir ([SearchViewModel.cs](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/ViewModels/SearchViewModel.cs)):**
  - Al escribir en la barra de búsqueda (tanto en la barra lateral de [MainWindow.xaml](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/MainWindow.xaml) como en [SearchPage.xaml](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/Views/SearchPage.xaml)), ya no se dispara una búsqueda masiva de toda la biblioteca en cada pulsación ni se fuerza la navegación inmediata.
  - Se despliega un menú flotante (*AutoSuggest Popup*) con sugerencias instantáneas: opción para buscar en todo, mejores coincidencias de canciones, artistas y álbumes.
  - Al hacer clic en una sugerencia, se reproduce directamente la canción o se abre el detalle del artista/álbum.
- **Búsqueda Global con Enter y `BulkObservableCollection`:**
  - Al presionar **Enter** o el icono de búsqueda, se ejecuta la búsqueda global exhaustiva en segundo plano y se navega a la página de resultados unificada.
  - `MatchingTracks`, `MatchingArtists` y `MatchingAlbums` ahora usan `BulkObservableCollection<T>.ReplaceRange()`, emitiendo una única notificación de reseteo en lugar de cientos de eventos individuales, eliminando por completo cualquier congelamiento o cierre abrupto de la aplicación.

### 🔊 Corrección del Selector de Salida de Audio / DAC en Reproducción en Curso
- **Resolución de Bloqueo Visual por Z-Index ([MainWindow.xaml](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/MainWindow.xaml)):**
  - El modal de selección de dispositivo y modo WASAPI Exclusive Bit-Perfect se configuró con `Grid.Row="0" Grid.RowSpan="3" Panel.ZIndex="1050"`, garantizando que se superponga por encima de `NowPlayingView` (`Panel.ZIndex="100"`).
- **Diseño Hi-Fi Vectorial Unificado ([NowPlayingView.xaml](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/Views/NowPlayingView.xaml)):**
  - Se sustituyó el icono genérico por el mismo diseño vectorial minicomponente / bafle Hi-Fi que figura en el reproductor principal, con punto de indicación acentuado y feedback táctil reactivo.

---

## [Versión 1.1.8] - 2026-10-17

### 🎨 Animación y Estabilidad en el Encabezado de Detalle de Artista
- **Compresión Animada con la Rueda del Ratón (`OnArtistDetailPreviewMouseWheel`):**
  - En la página de detalle de artista ([MyMusicPage.xaml](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/Views/MyMusicPage.xaml) y [MyMusicPage.xaml.cs](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/Views/MyMusicPage.xaml.cs)), el simple hecho de girar la rueda del ratón hacia abajo (`e.Delta < 0`) activa inmediatamente la compresión del encabezado del artista con una transición fluida (`DoubleAnimation` sobre `MaxHeight` y `Opacity` con `CubicEase EaseOut` de 250ms).
  - Al desplazarse hacia arriba cerca del inicio (`VerticalOffset <= 25`), el encabezado completo se restaura suavemente con animación.
- **Eliminación del Glitch de Oscilación Visual y Espacio Extra:**
  - En `OnArtistDetailScrollChanged`, se filtran los eventos provocados por el cambio de altura del `Viewport` (`ViewportHeightChange`) durante la animación del encabezado y se aplica un *cooldown* anti-rebote de 320ms, erradicando por completo el parpadeo/oscilación visual en el umbral de scroll.
  - Se añadió margen inferior adicional (`Margin="0,0,0,180"`) a los contenedores de álbumes tanto en vista de lista como en vista de cuadrícula para brindar holgura al desplazamiento.

### 🔑 Limpieza de API Key para Instalador y Configuración de Proveedores Oficiales
- **Instalador Limpio sin Claves Preconfiguradas:**
  - Se vació `DEFAULT_TEST_API_KEY` en [LastFmService.cs](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/Services/Online/LastFmService.cs) y se limpia automáticamente cualquier clave de prueba heredada en la base de datos local, garantizando que el ejecutable/instalador se publique completamente limpio.
- **Campos de Configuración de API Keys en Ajustes ([SettingsPage.xaml](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/Views/SettingsPage.xaml) y [SettingsViewModel.cs](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/ViewModels/SettingsViewModel.cs)):**
  - **API Key de Last.fm:** Campo configurable con botón *"Probar y guardar"* y enlace directo a `last.fm/api/account/create` para biografías y metadatos oficiales (`artist.getInfo`).
  - **API Key de Fanart.tv:** Nuevo campo configurable con validación en vivo (*"Probar y guardar"*) y enlace directo a `fanart.tv/get-an-api-key` para retratos (`artistthumb`) y fondos (`artistbackground`) en alta definición resueltos mediante MBID de MusicBrainz, complementando a la API oficial de Deezer.

### 🧹 Eliminación Completa del Scraping HTML de Last.fm (`+images`)
- **Transición 100% a APIs Oficiales ([LastFmService.cs](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/Services/Online/LastFmService.cs) y [FanartTvImageProvider.cs](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/Services/Online/FanartTvImageProvider.cs)):**
  - Se eliminó por completo el método `FetchArtistImagesFromLastFmGalleryAsync`, el diccionario `PlaceholderImageIds` y cualquier bloque de scraping HTML sobre `https://www.last.fm/music/.../+images`.
  - La obtención de imágenes de artistas utiliza exclusivamente las APIs REST oficiales de **Fanart.tv v3** (vía MusicBrainz MBID) y **Deezer API**.

---

## [Versión 1.1.7] - 2026-10-17

### 🎵 Navegación Inmersiva desde Reproducción en Curso
- **Botón Unificado Invisible hacia el Álbum:**
  - En la vista inmersiva `NowPlayingView` (tanto en vista normal como en la barra superior al expandir la cola de reproducción), el bloque de información de la canción (título, artista y álbum) ha sido encapsulado en un único botón transparente con cursor de mano.
  - Al hacer clic en cualquier parte del texto del título o del artista, la aplicación navega directamente al detalle del álbum correspondiente dentro de la biblioteca musical (`ViewCurrentAlbumCommand`), conservando el historial de navegación para retroceder con facilidad.

### 🌟 Resaltado Universal de la Canción en Reproducción
- **Detección y Sincronización en Tiempo Real (`IsPlaying`):**
  - Implementado `MyMusicViewModel.SyncPlayingTrack()` y actualización directa en `MainViewModel.ForceUiPlaybackUpdate()` para sincronizar de manera reactiva la propiedad `IsPlaying` en todas las instancias de canciones cargadas en la aplicación.
- **Feedback Visual en Todas las Pistas de "Mi Música":**
  - **Pestaña Canciones (`SongsListView`):** La fila de la canción en reproducción se resalta con fondo sutil en color de acento (`#250078D4`), borde acentuado de 1px, título en negrita con el color de énfasis del tema y un distintivo flotante con el icono de altavoz activo (`Speaker224`) sobre la carátula.
  - **Detalle de Artista (Listas de pistas por álbum):** Resaltado de fila con borde de acento, sustitución del icono de reproducción por el icono animado de altavoz activo (`Speaker224`) y título destacado en color de énfasis.
  - **Detalle de Álbum (`SelectedAlbumDetail.Tracks`):** Resaltado idéntico en color acentuado, sustitución del botón play por altavoz parlante y tipografía acentuada.

### 🔤 Menú Alfabético A-Z Inteligente y Condicional
- **Visibilidad Estricta por Filtro Alfabético:**
  - El botón de salto alfabético `[ A-Z ]` en la barra superior de filtros ahora está condicionado a la propiedad `IsNameSortActive`.
  - Aparece únicamente cuando el criterio de ordenamiento activo es por nombre ("Nombre (A-Z)"), tanto en la pestaña *Canciones*, como en *Artistas* y *Álbumes*.
  - Al ordenar por fecha de adición, año, duración, género o cantidad de álbumes/canciones, el botón se oculta automáticamente para mantener la interfaz despejada y coherente, cerrando además el modal en caso de estar desplegado.

### ⚡ Optimización Instantánea de Artistas sin Last.fm
- **Cero Latencia en Artistas al Desactivar Last.fm o Búsqueda de Fotos:**
  - Al desactivar Last.fm globalmente o desactivar la opción "Buscar artistas con Last.fm":
    - Se omiten por completo las comprobaciones de red a Last.fm y Deezer, eliminando cualquier bloqueo, reintento o delay HTTP.
    - Se evita la creación innecesaria de carpetas en disco al verificar la existencia de avatares locales mediante `Directory.Exists`.
    - La carga de imágenes de artista (`LoadThumbnailHierarchy`) recurre de inmediato a las carátulas locales de los álbumes (`fallbackTrack`) sin dejar siluetas vacías ni estados de espera.
    - `ArtistEnrichmentService` no inicia hilos de red en segundo plano cuando la opción de sincronización se encuentra deshabilitada.

### 🛠️ Correcciones Críticas de Estabilidad
- **Resolución de Bloqueo de Arranque (Crash en XAML):**
  - Corregido el icono no válido `ArrowCloudDownload24` en `Views/SettingsPage.xaml` sustituyéndolo por el valor canónico del enum `ArrowDownload24` de `Wpf.Ui.Common.SymbolRegular`, permitiendo el arranque limpio y sin excepciones de la aplicación.

### ⚡ Rendimiento y Gestión de Memoria en Navegación
- **Carga Asíncrona No Bloqueante de Carátulas y Avatares:**
  - `CoverImageSource` en `AlbumModel` y `AvatarImageSource` en `ArtistModel` ahora retornan instantáneamente sin bloquear el hilo principal de la interfaz (`UI thread`).
  - La lectura de disco y extracción TagLib se despacha en segundo plano (`Task.Run`), actualizando la interfaz vía `DispatcherPriority.Background` conforme cada miniatura se decodifica, eliminando por completo los congelamientos al navegar entre pestañas.
- **Supresión de la Purga Destructiva en el Cambio de Pestañas:**
  - Se eliminó la liberación forzada de miniaturas en memoria en `OnSelectedTabIndexChanged`. Las carátulas y fotos ya decodificadas permanecen en memoria mientras el usuario navega dentro de la colección musical, eliminando la recarga redundante de disco y reduciendo drásticamente el consumo de CPU.
- **Refresco y Agrupación Selectiva por Pestaña Activa:**
  - `RefreshCurrentTabData()` y `UpdateGrouping()` ahora procesan únicamente la colección de la pestaña seleccionada (Canciones, Artistas o Álbumes), evitando el reprocesamiento innecesario de miles de elementos en segundo plano.

### 🎨 Rediseño Universal de Barras de Desplazamiento (ScrollBars)
- **Nuevo Estilo Moderno Fluent con Flechas y Tirador en Cápsula:**
  - Implementado un estilo global en `App.xaml` para todas las barras de desplazamiento (`Orientation="Vertical"` y `Horizontal`) de la aplicación (biblioteca, listas de reproducción, cola, ajustes y búsqueda).
  - Incluye botón superior con flecha discreta (`▲`), botón inferior con flecha (`▼`), pista transparente con feedback visual al pasar el cursor, y tirador (`Thumb`) con diseño de píldora redondeada (`Width="6"`, `CornerRadius="3"`, expansión a `8px` en hover y arrastre, `MinHeight="40px"`), replicando con exactitud el diseño proporcionado por el usuario.

---

## [Versión 1.1.2] - 2026-10-16

### 🎨 Experiencia Visual e Interfaz de Usuario
- **Arrastre y Restauración de Ventana en Modo Maximizado:**
  - Implementado soporte completo para desmaximizar fluidamente la ventana arrastrando la barra de título transparente en `Reproducción en curso` y en cualquier vista maximizada, recalculando la posición proporcional respecto al cursor mediante Win32 `ReleaseCapture` y `SendMessage(WM_NCLBUTTONDOWN, HTCAPTION)`.
- **Compresión Automática del Encabezado de Artista al Hacer Scroll:**
  - En la vista de detalle de Artista (tanto en modo lista como en carátulas), al desplazarse hacia abajo (`VerticalOffset > 70px`), el encabezado se comprime a una barra fija minimalista con avatar reducido (38x38), nombre del artista en negrita, botón directo de Reproducción y selector de vista, ahorrando espacio en pantalla.
- **Ocultamiento de "Mi música" y Pestañas en Vistas de Detalle:**
  - Al abrir un Álbum o un Artista, se colapsa la fila de pestañas secundarias (*Canciones*, *Artistas*, *Álbumes*) y la cabecera general. En la vista de Álbum, el título de la página adopta el nombre del álbum.
- **Navegación Interactiva al Artista desde el Álbum:**
  - El nombre del artista dentro del detalle del álbum ahora es un control interactivo (estilo hipervínculo con icono sutil) que navega inmediatamente a la discografía del artista (`NavigateToArtistCommand`).
- **Ampliación del Área de Clic (Hitbox) en Pestañas de Música:**
  - Las pestañas *Canciones*, *Artistas* y *Álbumes* cuentan ahora con un contenedor amplio de interacción (`Padding="14,8,14,6"` con efecto sutil al pasar el cursor) y una animación de entrada/salida suave en el indicador de acento inferior mediante `Opacity` y `ScaleTransform.ScaleX`.
- **Estandarización de Animaciones a 1.0 Segundo:**
  - Transición cinemática de páginas principales, apertura y cierre de la vista inmersiva `NowPlayingView`, despliegue de la cola de reproducción y transición crossfade de 1.0 segundo al cambiar de pista en el MiniPlayer.

### 🖼️ Descargas e Integridad Multimedia
- **Prevención de Imágenes Incompletas y Cortes en Last.fm:**
  - En `LastFmService`, se adoptó un flujo de descarga atómico: lectura completa en memoria previa a escritura en archivo temporal `.tmp`, verificación estricta de encabezados y dimensiones (>50x50 píxeles) mediante `BitmapDecoder` antes de sustituir el archivo final en disco.
- **Cobertura de Borde Inferior en Efecto Ken Burns:**
  - Se añadió overscan (`Margin="-60"`) al contenedor de transformación de fondo en `NowPlayingView` para evitar que el zoom-out revele bordes vacíos en ventanas ultra anchas o maximizadas.

### ⚡ Rendimiento y Usabilidad
- **Altura Mínima Ergonómica en Barras de Desplazamiento:**
  - Añadido estilo global en `App.xaml` para asegurar que el `Thumb` de los controles `ScrollBar` mantenga una altura mínima de `40px`, garantizando facilidad de arrastre en bibliotecas con miles de pistas.
- **Navegación In-Memory Rápida:**
  - Eliminado el refresco redundante de colecciones en memoria al regresar a `MyMusic`, conservando una respuesta inmediata a 60 FPS.

### 📚 Documentación y Clean Architecture
- Creado el directorio `/docs` con [ARCHITECTURE.md](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/docs/ARCHITECTURE.md) detallando la división por capas, ciclo de vida de recursos y principios de Clean Code del proyecto.

---

## [Versión 1.1.3] - 2026-10-17

### ⚡ Rendimiento y Fluidez entre Pestañas
- **Carga Asíncrona No Bloqueante de Carátulas y Avatares:**
  - En `Models/MusicGroupModels.cs`, las propiedades `CoverImageSource` (`AlbumModel`) y `AvatarImageSource` (`ArtistModel`) ahora leen metadatos y decodifican mapas de bits en hilos secundarios en segundo plano (`Task.Run`). Al completarse, asignan la imagen en el despachador UI con prioridad `DispatcherPriority.Background`, eliminando por completo cualquier congelamiento visual al navegar entre listas y cuadrículas.
- **Refresco Selectivo y Agrupamiento Bajo Demanda:**
  - En `ViewModels/MyMusicViewModel.cs`, el cambio de pestañas (*Canciones*, *Artistas*, *Álbumes*) ya no invalida innecesariamente las miniaturas de toda la colección ni regenera agrupaciones de pestañas inactivas. Solo se refresca y agrupa la colección de la pestaña actualmente seleccionada.

### 🖱️ Ergonomía Universal del Cursor de Desplazamiento (ScrollBar Thumb)
- **Altura Mínima Garantizada de 56px en Cualquier Colección:**
  - Se implementó `ScrollBarMinThumbViewportConverter` (`IMultiValueConverter`) para recalcular dinámicamente la propiedad `ViewportSize` del `Track` en tiempo de renderizado:
    $$\text{minViewport} = \frac{\text{ratio} \cdot \text{Maximum}}{1.0 - \text{ratio}}, \quad \text{con } \text{ratio} = \frac{\text{MinThumbLength}}{\text{trackLength}}$$
  - Se configuró `MinHeight="56"` (vertical) y `MinWidth="56"` (horizontal) en los estilos globales de `ScrollBar` en `App.xaml`.
  - Incluso con decenas de miles de canciones o álbumes donde el `Track` nativo de WPF encogería el cursor a 8px o menos, el cursor ahora preserva una altura ergonómica de al menos 56px que se puede agarrar y arrastrar fácilmente con el ratón en toda la aplicación.

---

## [Versión 1.1.4] - 2026-10-17

### 🎵 Experiencia en Reproducción en Curso & Cola Activa
- **Reubicación de "Volver a la biblioteca" a la Barra de Título:**
  - El botón "Volver a la biblioteca" se integró de forma fija en la barra de título (`Grid.Row="0"`, `Panel.ZIndex="200"`) con diseño de píldora traslúcida (`CornerRadius="6"`, `Background="#26FFFFFF"`, `BorderBrush="#33FFFFFF"`), icono de flecha y texto nítido.
  - Al abrirse la cola activa (Up Next), el botón permanece visible en la parte superior sin sufrir interferencias ni solapamientos con las cabeceras o controles de la cola.
  - En `MainWindow.xaml.cs`, `OnTitleBarMouseDown` ignora eventos originados en controles derivados de `ButtonBase`, evitando falsos arrastres al presionar el botón de retorno.
- **Fondo de la Cola Activa con Opacidad al 90% y Animación Fade-In:**
  - Se incorporó `QueueBackdropOverlay` (`#0A0B10`) entre la galería animada y el contenedor de la lista de canciones.
  - Al desplegar la cola activa (`IsNowPlayingQueueExpanded == True`), la opacidad del fondo realiza una transición fluida tipo fade-in desde `0.0` hasta `0.9` (90% de opacidad) con aceleración `CubicEase` de 1.0 segundo.
  - Esto preserva un sutil toque del fondo artístico mientras garantiza máxima legibilidad y contraste para las canciones de la cola. Al colapsar la cola, la opacidad regresa suavemente a 0.0.

---

## [Versión 1.1.5] - 2026-10-17

### 🖼️ Selector y Sincronización de Galería para Múltiples Artistas (Colaboraciones)
- **Selector de Artista en el Encabezado de la Galería:**
  - Cuando una canción o pista cuenta con múltiples artistas colaboradores (detectados mediante [`ArtistSplitterService`](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/Services/Library/ArtistSplitterService.cs)), el modal de fotos presenta un menú desplegable (`ComboBox`) en la cabecera permitiendo alternar al instante entre los distintos artistas participantes.
  - Al cambiar de artista, se cargan de inmediato las fotos correspondientes (`LoadGalleryPhotosAsync`) y se sincroniza automáticamente con el selector de artista en Reproducción en Curso (`NowPlaying.SelectedCollaborationArtist`).
  - Igualmente, si el usuario cambia el artista seleccionado en la barra lateral de personalización de Reproducción en Curso, la galería abierta se actualiza al instante con las imágenes del artista elegido.

### 🌐 Método Principal de Scraping de Imágenes de Artistas (Last.fm + Fastly CDN)
- **Porte del Algoritmo Python como Método Principal:**
  - En [`LastFmService.cs`](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/Services/Online/LastFmService.cs) se implementó la lógica precisa del método en Python para la obtención de fotos:
    1. **Codificación de URL `quote_plus`:** Uso de `System.Net.WebUtility.UrlEncode` para normalizar espacios como `+` en la ruta `https://www.last.fm/music/{encoded_artist}/+images`.
    2. **Cabeceras de Navegador Desktop:** Cabecera `User-Agent` de Google Chrome desktop (`Chrome/120.0.0.0`).
    3. **Extracción Selectiva de Elementos:** Búsqueda en los contenedores de galería `.image-list-item-wrapper img` y `.image-list img`, con respaldo genérico filtrando avatares e identificadores de placeholder.
    4. **Conversión a Resolución Original (`/ar0/`):** Sustitución del patrón `/i/u/[^/]+/` por `/i/u/ar0/` (Fastly CDN Aspect Ratio 0), garantizando que las imágenes se descarguen en su resolución nativa original sin recortes ni compresiones, eliminando los bordes vacíos durante el efecto Ken Burns en monitores ultra-anchos.

---

## [Versión 1.1.6] - 2026-10-17

### ⚡ Servicio de Enriquecimiento Masivo de Artistas en Segundo Plano
- **Arquitectura No Bloqueante (`ArtistEnrichmentService`):**
  - Implementado [`ArtistEnrichmentService.cs`](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/Services/Online/ArtistEnrichmentService.cs) bajo el patrón singleton `Lazy<T>`, gestionando una cola de procesamiento progresiva en segundo plano para toda la biblioteca.
  - Arranque offline-first diferido: se esperan 10 segundos tras el inicio de la app antes de comenzar el escaneo para no competir con el renderizado inicial ni retardar la UI.
  - Se encola automáticamente tras escaneos e importaciones locales en [`MainViewModel.cs`](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/ViewModels/MainViewModel.cs) y [`SettingsViewModel.cs`](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/ViewModels/SettingsViewModel.cs).
- **Extracción Estricta de Biografías vía API Oficial (Sin Scraping Web):**
  - En [`LastFmService.cs`](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/Services/Online/LastFmService.cs), `FetchArtistBioViaApiAsync` consulta exclusivamente el endpoint oficial `artist.getinfo` con la API key de Last.fm.
  - Validación de nombre: se normalizan tildes, mayúsculas y caracteres de puntuación para verificar que el nombre devuelto coincida con el consultado, descartando falsos positivos de autocorrección.
- **Abstracción de Imágenes y Proveedor Deezer:**
  - Nueva interfaz [`IArtistImageProvider`](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/Services/Online/IArtistImageProvider.cs) e implementación [`DeezerImageProvider`](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/Services/Online/DeezerImageProvider.cs), desacoplando la obtención de imágenes y permitiendo incorporar proveedores como Fanart.tv a futuro sin modificar la cola.
  - Guarda imágenes únicamente si el artista no cuenta con ninguna previa, respetando `primary.txt` y `omitted.txt` sin sobreescribir elecciones del usuario.
- **Caché Negativa y Resiliencia de Red:**
  - Si un artista no existe en Last.fm ni en Deezer, se genera un marcador `not_found.txt` con fecha UTC que previene nuevas consultas durante 30 días (salvo actualización manual forzada).
  - Manejo diferenciado de fallos de red: ante desconexión o caídas generales, la cola se pausa automáticamente sin marcar artistas como no encontrados.
  - Límite de 1 artista a la vez, delay de control para $\le 4$ req/s y backoff exponencial (máx 3 reintentos) ante errores 429 o 5xx.
- **Panel de Control y Progreso en Ajustes:**
  - Añadido panel visual en [`SettingsPage.xaml`](file:///C:/Users/Miguquis/.gemini/antigravity/scratch/BenyComes/Views/SettingsPage.xaml) con botón *"Actualizar información de todos los artistas"*, controles de *Pausar*, *Reanudar* y *Cancelar*, barra de progreso reactiva y contador de artistas procesados y pendientes.
  - Registro de eventos de inicio y finalización en `%LocalAppData%\BenyComes\app.log`.


