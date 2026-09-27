# Xwt.Avalonia — backend Avalonia para Xwt (reemplazo de Xwt.Gtk)

`Xwt.Avalonia` mapea el MISMO modelo de objetos del frontend Xwt a controles
Avalonia, igual que `Xwt.Gtk` lo mapea a Gtk# (legacy que se retirará proximamente) y `Xwt.WPF` a WPF. Vive DENTRO del submódulo `external/xwt`, junto
a los otros backends — no es una reconstrucción paralela de la aplicación.

## Uso

```csharp
// Guest mode (caso MonoDevelop: el shell Avalonia ya tiene la Application):
Xwt.Application.Initialize ("Xwt.AvaloniaBackend.AvaloniaEngine, Xwt.Avalonia");

// Standalone (el engine levanta el dispatcher propio):
Xwt.Application.Initialize (new AvaloniaEngine ());
```

## Proyectos

| Proyecto | Target | Contenido |
|---|---|---|
| `Xwt.Avalonia/` | net10.0 | Engine + backends de las oleadas 0 y 1 |
| `Xwt.Avalonia.Smoke/` | net10.0 | Humo headless determinista (14 aserciones, exit 0/1) |

El núcleo `Xwt/Xwt.csproj` es multi-target `net40;net10.0`: en net10 los 3
tipos de `System.Xaml` que usa el frontend (`ContentPropertyAttribute`,
`ValueSerializer`, `IValueSerializerContext`) los provee el shim
`Xwt/Xwt/Compatibility/SystemXamlShim.cs` (más un `XamlServices` del
designer que lanza `PlatformNotSupportedException` y un guard NET para el
`BinaryFormatter` de `TransferDataSource`, obsoleto en .NET moderno). El
build net40 queda intacto.

## Primera oleada (portada)

| Backend | Nativo | Notas |
|---|---|---|
| `WindowBackend` | `Avalonia.Controls.Window` | `SetChild` como contenido; también adapta ventanas existentes del shell |
| `LabelBackend` | `TextBlock` | Text/TextColor/alignment/ellipsize/wrap |
| `ButtonBackend` | `Button` | Rutea `Clicked` al `IButtonEventSink` vía `ApplicationContext.InvokeUserCode` |
| `BoxBackend` | `Canvas` (contenedor fijo) | El frontend `Box` calcula packing/orientación/spacing y entrega los rects en `SetAllocation` — mismo contrato que el `CustomContainer` del backend GTK |
| `TextEntryBackend` | `TextBox` | Text/Changed/Activated (Enter)/selección/caret |
| `CanvasBackend` | `Grid` { host de render + overlay `Canvas` } | El host (`Control` con `Render` override) rasteriza cada frame a un `WriteableBitmap` (Bgra8888, patrón `SkTextEditor` del shell) y el sink `ICanvasEventSink.OnDraw` recibe un `SkDrawContext` (pila estilo Cairo sobre SKCanvas+SKPath); el overlay `Canvas` aloja los children con bounds |

`AvaloniaEngine` además resuelve el ciclo de aplicación (guest/standalone),
threading (`InvokeAsync`, timers, `DispatchPendingEvents` sobre
`Avalonia.Threading`) y los handles nativos (`GetNativeWidget`,
`GetBackendForWindow`, `GetNativeWindow`, parent window vía
`TopLevel.GetTopLevel`).

## Oleada 1 — dibujo (`DrawingBackends.cs`, sobre Avalonia.Media + SkiaSharp)

| Handler | Handle | Notas |
|---|---|---|
| `ContextBackend` | `SkDrawContext` | Pila estilo Cairo (SKCanvas + SKPath compartido + stack de `DrawState`): Save/Restore/Clip±Preserve, Fill/Stroke±Preserve, SetColor/LineWidth/LineDash, **SetPattern** (shader de gradiente o bitmap repeat — resuelve el Pattern del FRONTEND con `GetSafeBackend`, igual que Xwt.WPF), transformaciones, arcos/curvas/rects (también relativos), IsPointInStroke/InFill, CreatePath/CopyPath/AppendPath |
| `FontBackend` | `FontData` | Cache de `SKTypeface` por familia/estilo; **defaults runtime**: `SKTypeface.Default.FamilyName` puede ser vacío con el font manager fontconfig de SkiaSharp 3.119 (familia sin resolver ⇒ métricas cero), así que `FontCache.DefaultFamily()` elige una sans real instalada (DejaVu Sans → Liberation Sans → …) o la primera familia no vacía; mono/serif idem con candidatos conocidos; `RegisterFontFromFile` vía `SKFontManager.CreateTypeface` |
| `TextLayoutBackend` | `AvaloniaTextLayout` | Blob POR RUN (colores por `ColorTextAttribute`), `SKTextBlobBuilder.AddRun` + `SKCanvas.DrawTextBlob`, ellipsize WordElipsis, alineación, GetSize/IndexFromCoordinates/CoordinateFromIndex/Baseline/Meanline |
| `GradientBackend` / `ImagePatternBackend` | `GradientData` / `SKBitmap` | Lineal/radial con stops; el patrón de imagen es el bitmap del image backend |
| `ImageBuilderBackend` | `AvaloniaImageBuilder` | SKSurface offscreen + SKBitmap; `CreateContext` entrega un `SkDrawContext` sobre ese canvas |
| `AvaloniaImageBackend` | `AvaloniaImageData` | Load/Save (SKCodec/encode), Copy/Crop/Area, Set/GetBitmapPixel, MultiResolution; **`ConvertToBitmap` de custom-drawn**: rasteriza REPLAYANDO el callback con `idesc.Size` (los VectorImage no tienen tamaño intrínseco en el backend) — y con **`Alpha = 1`** (el default de `ImageDescription.Alpha` es 0 y anula todo el dibujo); `GetStockIcon` queda para la oleada 4 |

## Oleada 3 — listas (los Pads, en `ListBackends.cs`)

`ListViewBackend` (también `IListBoxBackend`), `TreeViewBackend` y
`ComboBoxBackend`. Los rows renderizan con controles Avalonia planos
(`ScrollViewer → StackPanel` de `Grid` por fila) en vez de templates de
datos: el contrato Xwt exige APIs claveadas por `TreePosition`
(SelectRow/ExpandRow/GetCellBounds…) sobre un origen de datos que el backend
no posee, y `ITreeDataTemplate.BindChildren` de Avalonia 12 pierde la
identidad por nodo. El contenido de celda mapea igual que `CellUtil` de
Xwt.WPF (TextCellView→TextBlock, ImageCellView→Image rasterizada por la
oleada 1, CheckBoxCellView→CheckBox); `CanvasCellView` queda para la oleada
de celdas custom-drawn.

**Stores**: el núcleo Xwt exige backends registrados (`ListStore` no tiene
fallback y el fallback del `TreeStore` es internal) — `ListStoreBackend` y
`TreeStoreBackend` implementan `IListStoreBackend`/`ITreeStoreBackend` con
filas/nodos cuya identidad ES el `TreePosition` (los handles se pueden
guardar, igual que los GtkTreeIter de GTK); para `IListDataSource` propios
(como los view models de los Pads) hay un fallback por índice
(`IndexRowHandle`). El `TreeStore` se reconstruye de los eventos del origen
(NodeChanged/…) y el flatten respeta la expansión con eventos
Expanding/Expanded/Collapsing/Collapsed.

## Oleadas siguientes (en el orden que pide el port de MonoDevelop)

1. `IScrollViewBackend`, `ICheckBoxBackend`, `IRadioButtonBackend`,
   `IToggleButtonBackend`, `IFrameBackend`, `ISeparatorBackend`,
   `IImageViewBackend`.
2. Familia de menús (`IMenuBackend`/`IMenuItemBackend`…),
   `IDialogBackend`/`IAlertDialogBackend`, file choosers, `ClipboardBackend`,
   `INotebookBackend`, `IPanedBackend`.
3. Hosting guest `ICustomWidgetBackend` (controles Avalonia crudos dentro de
   Xwt) y servicios de plataforma; celdas custom-drawn (`CanvasCellView`).

## QA

```bash
cd Xwt.Avalonia.Smoke && dotnet run
# [smoke] ok 19/19  (exit 0)
```

Cubre: inicialización por nombre de backend, Label/Button/Entry/Box →
controles nativos correctos, composición de hijos del Box, ventana con
contenido, evento Clicked de vuelta al frontend, round-trip de texto del
entry, las 5 rutas de dibujo de la oleada 1 (raster de un `ImageBuilder` +
lectura de píxel, tamaño/index de `TextLayout`, fill con gradiente y texto
dibujado dentro de una imagen) y las 5 de la oleada 3 (ListView sobre
ListStore con selección por índice, TreeView sobre TreeStore con selección
por posición de nodo, ComboBox con índice seleccionado). Nota: en equipos
con fuentes de usuario WOFF/WOFF2 hay que arrancar con el workaround
`FONTCONFIG_FILE` documentado en `docs/interfaz-plan.md`
§M16e/M16f/M16g (mismo bucle de SkFontMgr_fontconfig que en el shell).

## Notas de API (Avalonia 12)

- `SystemDecorations` → `WindowDecorations` (renombrado en Avalonia 12).
- `TemplatedControl` vive en `Avalonia.Controls.Primitives`.
- Dentro de `Xwt.AvaloniaBackend` los nombres Avalonia se usan por ALIAS
  (`AWindow`, `AButton`, …): los namespaces Xwt sombrean `Control`,
  `Window`, `Canvas`, `Alignment`, `WrapMode`, `Cursor`, `Application`…
