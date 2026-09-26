# Xwt.Avalonia — backend Avalonia para Xwt (reemplazo de Xwt.Gtk)

`Xwt.Avalonia` mapea el MISMO modelo de objetos del frontend Xwt a controles
Avalonia, igual que `Xwt.Gtk` lo mapea a Gtk# (legacy que se retira tras la
rama 9.x) y `Xwt.WPF` a WPF. Vive DENTRO del submódulo `external/xwt`, junto
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
| `Xwt.Avalonia/` | net10.0 | Engine + backends de la primera oleada |
| `Xwt.Avalonia.Smoke/` | net10.0 | Humo headless determinista (9 aserciones, exit 0/1) |

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
| `CanvasBackend` | `Canvas` | Children con bounds (el dibujo custom espera a los handlers de la oleada 1) |

`AvaloniaEngine` además resuelve el ciclo de aplicación (guest/standalone),
threading (`InvokeAsync`, timers, `DispatchPendingEvents` sobre
`Avalonia.Threading`) y los handles nativos (`GetNativeWidget`,
`GetBackendForWindow`, `GetNativeWindow`, parent window vía
`TopLevel.GetTopLevel`).

## Oleadas siguientes (en el orden que pide el port de MonoDevelop)

1. Handlers de dibujo: `ImageBackendHandler`, `ContextBackendHandler`,
   `TextLayoutBackendHandler`, `FontBackendHandler`,
   `GradientBackendHandler`, `DrawingPathBackendHandler` → Avalonia.Media +
   SkiaSharp (el shell Avalonia ya usa esa pila).
2. `IScrollViewBackend`, `ICheckBoxBackend`, `IRadioButtonBackend`,
   `IToggleButtonBackend`, `IFrameBackend`, `ISeparatorBackend`,
   `IImageViewBackend`.
3. `ITreeViewBackend`/`ITreeStoreBackend`, `IListViewBackend`/
   `IListStoreBackend` (los Pads), `IComboBoxBackend`.
4. Familia de menús (`IMenuBackend`/`IMenuItemBackend`…),
   `IDialogBackend`/`IAlertDialogBackend`, file choosers, `ClipboardBackend`,
   `INotebookBackend`, `IPanedBackend`.
5. Hosting guest `ICustomWidgetBackend` (controles Avalonia crudos dentro de
   Xwt) y servicios de plataforma.

## QA

```bash
cd Xwt.Avalonia.Smoke && dotnet run
# [smoke] ok 9/9  (exit 0)
```

Cubre: inicialización por nombre de backend, Label/Button/Entry/Box →
controles nativos correctos, composición de hijos del Box, ventana con
contenido, evento Clicked de vuelta al frontend y round-trip de texto del
entry. Nota: en equipos con fuentes de usuario WOFF/WOFF2 hay que arrancar
con el workaround `FONTCONFIG_FILE` documentado en
`docs/interfaz-plan.md` §M16e/M16f (mismo bucle de SkFontMgr_fontconfig que
en el shell).

## Notas de API (Avalonia 12)

- `SystemDecorations` → `WindowDecorations` (renombrado en Avalonia 12).
- `TemplatedControl` vive en `Avalonia.Controls.Primitives`.
- Dentro de `Xwt.AvaloniaBackend` los nombres Avalonia se usan por ALIAS
  (`AWindow`, `AButton`, …): los namespaces Xwt sombrean `Control`,
  `Window`, `Canvas`, `Alignment`, `WrapMode`, `Cursor`, `Application`…
