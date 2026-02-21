# Scope Housing Mesh Surgery — v4.0.0

Non-PiP scope mod for SPT. Disables the PiP optic camera for massive FPS gains, then adds zoom + reticle via a GrabPass shader on the lens surface.

## Features

- **PiP Disable** — Kills the second camera render. Identical FPS between hip-fire and ADS.
- **Shader Zoom** (new) — GrabPass shader magnifies the world through the scope lens. Weapon stays normal size. Includes procedural crosshair reticle. Zero extra CPU cost.
- **FOV Zoom Fallback** — If shader AssetBundle is missing, falls back to camera FOV override. Weapon appears zoomed too (less ideal but works without shader).
- **Lens Transparency** — Hides the PiP render texture surface.
- **Mesh Surgery** (experimental) — Runtime mesh cutting at lens plane.
- **Persistent mesh cut cache** — Stores cut meshes on disk next to the DLL (`mesh_cut_cache`) to avoid re-cutting on future runs.

## Installation

1. Copy `ScopeHousingMeshSurgery.dll` to `BepInEx/plugins/ScopeHousingMeshSurgery/`
2. (Optional but recommended) Copy `assets/scopezoom.bundle` to `BepInEx/plugins/ScopeHousingMeshSurgery/assets/`
3. Delete any old config: `BepInEx/config/com.example.scopehousingmeshsurgery.cfg`

## Building the Shader AssetBundle

The shader zoom feature requires a compiled AssetBundle. To build it:

1. Open Unity Editor (version matching EFT, ~2021.x)
2. Create or open a project
3. Copy `Shaders/ScopeZoom.shader` to `Assets/Shaders/`
4. Copy `Shaders/Editor/BuildAssetBundle.cs` to `Assets/Editor/`
5. Menu: **Assets → Build Scope Zoom Bundle (Auto)**
6. Find `scopezoom.bundle` in the `AssetBundles/` folder next to your project
7. Copy it to `BepInEx/plugins/ScopeHousingMeshSurgery/assets/scopezoom.bundle`

If you skip this step, the mod falls back to FOV zoom (camera FOV change).

## Config Options

### Zoom
| Setting | Default | Description |
|---------|---------|-------------|
| EnableZoom | true | Master zoom toggle |
| EnableShaderZoom | true | Use GrabPass shader (needs bundle) |
| DefaultZoom | 4.0 | Magnification for fixed scopes |
| AutoFovFromScope | true | Auto-detect zoom from scope data |

### General
| Setting | Default | Description |
|---------|---------|-------------|
| DisablePiP | true | Kill optic camera |
| AutoDisableForHighMagnificationScopes | false | Auto-disable the mod when scoped optic max magnification is above 10x |
| MakeLensesTransparent | true | Hide lens surfaces |

## How Shader Zoom Works

```
1. Scene renders normally (world + scope housing)
2. Lens quad reaches its turn in the transparent render queue
3. GrabPass snapshots the screen BEFORE the lens draws
4. Fragment shader:
   - Computes scope center in screen space
   - Pulls each pixel's UV toward that center by zoom factor
   - Samples the GrabPass texture at the zoomed UV
   - Applies circular vignette mask
   - Overlays procedural crosshair reticle
5. Lens renders with zoomed world content
```

No extra cameras. No extra CPU culling passes. One framebuffer copy (GrabPass) + one textured quad.
