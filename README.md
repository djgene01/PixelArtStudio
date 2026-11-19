# PixelArtStudio

A WPF (.NET 10) editor for pixel art, sprites and sprite animations.

## Features

- **Drawing tools:** pencil, eraser, fill (contiguous or global), eyedropper, line, rectangle, ellipse (outline or filled), and rectangle select.
- **Brush options:** brush sizes 1 to 8, horizontal and vertical mirror drawing, and Shift to constrain lines to 45° and shapes to squares or circles.
- **Colour:** left button paints the primary colour, right button the secondary.
  - RGBA sliders and a hex box.
  - Palette: PICO-8 + DawnBringer 32 by default, and you can load or save `.hex` and `.gpl` files.
- **Layers:** visibility, opacity, rename, reorder, duplicate and merge down.
- **Animation:**
  - Frame timeline with thumbnails and per-frame durations, plus a frame rate setting for all frames.
  - Playback on the canvas, a looping preview, and onion skin.
- **Selection:** move a selection by dragging inside it; cut, copy and paste use the Windows clipboard (PNG with alpha).
- **Canvas:** canvases from 1 to 1024 px per side; resize with an anchor, flip and rotate; 100 undo steps.
- **Native file format:** `.pxs`, a ZIP holding a JSON manifest and one PNG per cel.
- **Open and import:** PNG, BMP, GIF and WebP. Animated GIF, APNG and WebP open as frames with their timings. Sprite sheets can be imported and sliced into frames.
- **Export:**
  - Single images: PNG or BMP of the current frame.
  - PNG sequence.
  - Sprite sheet PNG, with an optional JSON file of frame rectangles and durations.
  - Animated GIF, APNG and lossless animated WebP.
  - Every format can be scaled 1x to 16x with nearest-neighbour sampling.

## Shortcuts

| Keys | Action |
| --- | --- |
| B / E / G / I | Pencil / Eraser / Fill / Eyedropper |
| L / U / O / M | Line / Rectangle / Ellipse / Select |
| X | Swap primary and secondary colour |
| [ / ] | Brush size down / up |
| , / . | Previous / next frame |
| Ctrl+Z, Ctrl+Y (Ctrl+Shift+Z) | Undo, redo |
| Ctrl+X / C / V | Cut / copy / paste |
| Ctrl+A, Ctrl+D, Esc | Select all, deselect |
| Del | Clear the selection |
| Enter | Place a pasted or moved selection |
| Ctrl+N / O / S, Ctrl+Shift+S | New / open / save, save as |
| Ctrl+E | Export |
| Ctrl+wheel, Ctrl++ / Ctrl+-, Ctrl+0 | Zoom around the cursor, zoom in / out, fit |
| Middle-drag or Space+drag | Pan |

## Build and run

```
dotnet build PixelArtStudio.slnx
dotnet test
dotnet run --project src/PixelArtStudio
```

You can also pass a file to open: `dotnet run --project src/PixelArtStudio -- sprite.pxs`.

## Project layout

- `src/PixelArtStudio.Core`: the model, drawing algorithms, undo history and file import/export. Plain `net10.0` with no WPF, so it is unit-tested.
- `src/PixelArtStudio`: the WPF app (MVVM with CommunityToolkit.Mvvm, Fluent dark theme).
- `tests/PixelArtStudio.Tests`: xUnit tests for the Core project.

## ImageSharp licence

Image encoding and decoding use [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp) 3.2.0, which is under the Six Labors Split License.

Since 3.2, the package checks for a licence key at build time:
- **Debug builds** work without a key and show a "No Six Labors license found" warning.
- **Release builds** fail until a key is configured.

To configure one, get a key from <https://sixlabors.com/pricing/> and then do one of the following:
- Set the `SixLaborsLicenseKey` MSBuild property.
- Set `SixLaborsLicenseFile`.
- Put a `sixlabors.lic` file in the project.

Older 3.1.x versions don't have this check, but they have known security advisories, so they are not used.
