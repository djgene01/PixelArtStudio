# PixelArtStudio

A WPF (.NET 10) editor for pixel art, sprites and sprite animations.

## Promo

<video src="https://github.com/djgene01/PixelArtStudio/raw/main/docs/promo.mp4" controls muted width="720">
  Your viewer can't play embedded video. <a href="docs/promo.mp4">Watch the promo (MP4)</a>.
</video>

Narrated by Pixel the Pixi. [Download the MP4](docs/promo.mp4).

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
| `get_state` | Size, layers, frames, durations, palette, active frame and layer, unsaved flag |
| `new_sprite`, `open_file`, `save`, `export` | New sprite, open `.pxs` or an image, save `.pxs`, export PNG/BMP/PNG sequence/sprite sheet/GIF/APNG/WebP |
| `draw` | A batch of pixels, lines, rectangles, ellipses, fills and clears as one undo step |
| `paint_rows`, `get_pixels` | Write or read pixels as text rows with a character-to-colour legend |
| `view_frame`, `view_animation` | PNG previews of one frame, or all frames side by side, for Claude to check its work |
| `add_frame`, `delete_frame`, `move_frame`, `set_frame_duration` | Timeline editing (`add_frame` can duplicate a frame) |
| `copy_cel`, `shift_cel` | Copy a cel to another frame or layer, or move its pixels |
| `add_layer`, `duplicate_layer`, `delete_layer`, `move_layer`, `merge_down`, `set_layer` | Layer stack, name, visibility and opacity |
| `resize_canvas`, `flip`, `rotate`, `set_palette` | Whole-sprite changes |
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
- `src/PixelArtStudio.Mcp`: the MCP server (official `ModelContextProtocol.AspNetCore` SDK, Streamable HTTP). Its tools work through the small `IStudioEditor` interface, so they are unit-tested without WPF.
- `src/PixelArtStudio`: the WPF app (MVVM with CommunityToolkit.Mvvm, Fluent dark theme). `MainViewModel` implements `IStudioEditor`, and tool calls run on the UI thread.
- `tests/PixelArtStudio.Tests`: xUnit tests for the Core and MCP projects.

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
