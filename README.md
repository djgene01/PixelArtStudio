# PixelArtStudio

A WPF (.NET 10) editor for pixel art, sprites and sprite animations.

## Promo

<video src="https://github.com/djgene01/PixelArtStudio/raw/main/docs/promo.mp4" controls muted width="720">
  Your viewer can't play embedded video. <a href="docs/promo.mp4">Watch the promo (MP4)</a>.
</video>

Narrated by Pixel the Pixi. [Download the MP4](docs/promo.mp4).

## Features

- **Drawing tools:** pencil, eraser, fill (contiguous or global), eyedropper, line, rectangle, ellipse (outline or filled), and rectangle select.
- **Brush options:** brush sizes 1 to 64, horizontal and vertical mirror drawing, and Shift to constrain lines to 45° and shapes to squares or circles.
- **Colour:** left button paints the primary colour, right button the secondary.
  - RGBA sliders and a hex box.
  - Palette: PICO-8 + DawnBringer 32 by default, and you can load or save `.hex` and `.gpl` files.
  - Palette swap: replace a swatch with the primary colour, optionally recolouring every pixel of the old colour in one undo step. Reorder swatches with ◀ and ▶.
- **Colour cycling:** the classic palette-swap animation (water, fire, waterfalls) without extra frames. See [Colour cycling](#colour-cycling).
- **Layers:** visibility, opacity, rename, reorder, duplicate and merge down.
- **Animation:**
  - Frame timeline with thumbnails and per-frame durations, plus a frame rate setting for all frames.
  - Playback on the canvas, a looping preview, and onion skin.
- **Selection:** move a selection by dragging inside it; cut, copy and paste use the Windows clipboard (PNG with alpha).
- **Canvas:** canvases from 1 to 4096 px per side, up to 512M pixels over all frames and layers (about 2 GB); resize with an anchor, flip and rotate.
  - Zoom from 12.5% to 6400%; Fit uses the window size, and toolbar zoom keeps the view centred.
  - Pixel grid, plus an optional 8, 16 or 32 px tile grid.
  - Up to 100 undo steps within 768 MB; strokes only store the area they changed.
- **Native file format:** `.pxs`, a ZIP holding a JSON manifest and one PNG per cel.
- **Open and import:** PNG, BMP, GIF and WebP. Animated GIF, APNG and WebP open as frames with their timings. Sprite sheets can be imported and sliced into frames.
- **Export:**
  - Single images: PNG or BMP of the current frame.
  - PNG sequence.
  - Sprite sheet PNG, with an optional JSON file of frame rectangles and durations.
  - Animated GIF, APNG and lossless animated WebP.
  - Every format can be scaled 1x to 16x with nearest-neighbour sampling.
  - Multi-frame formats can bake colour cycling into the output.

## Colour cycling

Colour cycling rotates the colours of a range of palette slots over time, so pixels painted in those colours appear to move.

1. Put the colours of the effect next to each other in the palette, in order (for water, dark blue to white). Use Add, Replace and ◀ ▶ to arrange them.
2. Paint with exactly those palette colours (opaque).
3. Click the first swatch of the range, then Shift+click the last one. Click **Add from selection** under **Colour cycles**.
4. Press F6 or **Play cycles** to see it move. Set each cycle's step time (ms per step), direction (Reverse) and whether it is enabled.

Notes:
- The pixels are not changed; cycling is applied on screen and when you export with **Bake colour cycling**, which adds an image each time a cycle steps, over one full loop of the frames and cycles (at most 60 s and 600 images).
- Swatches in a cycle show a small white marker. If a range holds the same colour twice, pixels of that colour follow its first slot.
- Cycles are saved in `.pxs` files. Files with cycles, or with a side over 1024 px, are written as format version 2, which older versions of the app cannot open.

## Claude (MCP)

While it runs, the app hosts an [MCP](https://modelcontextprotocol.io) server at `http://127.0.0.1:47821/mcp`, so Claude can create sprites and animations in the open window. Claude's edits appear live, and each tool call is one step on the normal undo stack (Ctrl+Z works on them). The status bar shows `MCP: 127.0.0.1:47821`, or `MCP: off` if the server could not start (for example, when a second instance finds the port in use).

The app must be running before Claude connects.

- **Claude Code:** this repository includes `.mcp.json`, so Claude Code started in this folder offers the server. Anywhere else, run:
  ```
  claude mcp add --transport http pixel-art-studio http://127.0.0.1:47821/mcp
  ```
- **Claude Desktop:** add this to `claude_desktop_config.json` (it needs Node.js for `mcp-remote`):
  ```json
  {
    "mcpServers": {
      "pixel-art-studio": { "command": "npx", "args": ["-y", "mcp-remote", "http://127.0.0.1:47821/mcp"] }
    }
  }
  ```

Then ask, for example, "Create a 16x16 4-frame bouncing ball and export it as a GIF to C:\Sprites\ball.gif".

| Tools | What they do |
| --- | --- |
| `get_state` | Size, layers, frames, durations, palette, colour cycles, active frame and layer, unsaved flag |
| `new_sprite`, `open_file`, `save`, `export` | New sprite, open `.pxs` or an image, save `.pxs`, export PNG/BMP/PNG sequence/sprite sheet/GIF/APNG/WebP (optionally baking colour cycling) |
| `draw` | A batch of pixels, lines, rectangles, ellipses, fills and clears as one undo step |
| `paint_rows`, `get_pixels` | Write or read pixels as text rows with a character-to-colour legend |
| `view_frame`, `view_animation` | PNG previews of one frame (or a region, or at a colour cycling time), or all frames side by side, for Claude to check its work |
| `add_frame`, `delete_frame`, `move_frame`, `set_frame_duration` | Timeline editing (`add_frame` can duplicate a frame) |
| `copy_cel`, `shift_cel` | Copy a cel to another frame or layer, or move its pixels |
| `add_layer`, `duplicate_layer`, `delete_layer`, `move_layer`, `merge_down`, `set_layer` | Layer stack, name, visibility and opacity |
| `resize_canvas`, `flip`, `rotate`, `set_palette`, `replace_color` | Whole-sprite changes; `replace_color` is a palette swap across every frame and layer |
| `add_color_cycle`, `set_color_cycle`, `remove_color_cycle` | Colour cycling over a range of palette slots |
| `set_active`, `undo`, `redo` | Show a frame or layer to the user; step the history |

Notes:
- Set the `PIXELARTSTUDIO_MCP_PORT` environment variable to use another port, and update the URL in your client to match.
- There is no authentication. The server only listens on the loopback interface and refuses requests from browser pages on other origins, but any program on your computer can call it.
- Tool calls never show dialogs. `new_sprite` and `open_file` refuse to replace a sprite with unsaved changes unless Claude passes `discard_changes`.
- The app now needs the ASP.NET Core runtime (`Microsoft.AspNetCore.App`) as well as the Windows Desktop runtime.

## Shortcuts

| Keys | Action |
| --- | --- |
| B / E / G / I | Pencil / Eraser / Fill / Eyedropper |
| L / U / O / M | Line / Rectangle / Ellipse / Select |
| X | Swap primary and secondary colour |
| [ / ] | Brush size down / up |
| , / . | Previous / next frame |
| P or F5 | Play / pause |
| F6 | Colour cycling on / off |
| Arrow keys | Nudge the selection by one pixel |
| Ctrl+Z, Ctrl+Y (Ctrl+Shift+Z) | Undo, redo |
| Ctrl+X / C / V | Cut / copy / paste |
| Ctrl+A, Ctrl+D, Esc | Select all, deselect |
| Del | Clear the selection |
| Enter | Place a pasted or moved selection |
| Ctrl+N / O / S, Ctrl+Shift+S | New / open / save, save as |
| Ctrl+E | Export |
| Ctrl+wheel, Ctrl++ / Ctrl+-, Ctrl+0, Ctrl+1 | Zoom around the cursor, zoom in / out, fit, actual size |
| Ctrl+G | Pixel grid on / off |
| Middle-drag or Space+drag, Shift+wheel | Pan, scroll sideways |

## Build and run

```
dotnet build PixelArtStudio.slnx
dotnet test
dotnet run --project src/PixelArtStudio
```

You can also pass a file to open: `dotnet run --project src/PixelArtStudio -- sprite.pxs`.

## Project layout

- `src/PixelArtStudio.Core`: the model, drawing algorithms, undo history and file import/export. Plain `net10.0` with no WPF, so it is unit-tested.
- `src/PixelArtStudio.Mcp`: the MCP server (official `ModelContextProtocol.AspNetCore` SDK, Streamable HTTP). Its tools work through the small `IStudioEditor` interface, so they are unit-tested without WPF.
- `src/PixelArtStudio`: the WPF app (MVVM with CommunityToolkit.Mvvm, Fluent dark theme). `MainViewModel` implements `IStudioEditor`, and tool calls run on the UI thread.
- `tests/PixelArtStudio.Tests`: xUnit tests for the Core and MCP projects.

## Installer

Download `PixelStudio-<version>-x64.msi` from the [Releases](https://github.com/djgene01/PixelArtStudio/releases) page. It is self-contained, so no .NET runtime is needed, and it adds a Start menu shortcut and opens `.pxs` files.

To build it yourself (needs the [WiX Toolset](https://wixtoolset.org) CLI and a Six Labors licence file, see below):
```
dotnet publish src/PixelArtStudio -c Release -r win-x64 --self-contained -p:Version=1.0.0 -p:SixLaborsLicenseFile=<path to sixlabors.lic> -o artifacts/publish
wix build installer/Package.wxs -arch x64 -d Version=1.0.0 -d PublishDir=<full path to artifacts/publish> -o artifacts/PixelStudio-1.0.0-x64.msi
```

## Licence

Pixel Studio is released under the [MIT licence](LICENSE).

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
