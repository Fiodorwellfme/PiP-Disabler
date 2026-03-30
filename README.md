<h1>Concept:</h1>    
  
Runtime meshcutting combined with FOV zoom (incompatible with Fontaine's FOV fix) to reduce the FPS cost of ADSing with optic scopes.

# How it works :  

When ADS, identify if the current sight is an optic or not.  
If it is not an optic, do nothing.  
If it is an optic, don't run if: thermal / NV scopes or unsupported scope from bypass list or whitelist mismatch  
Else run the mod  

Reticle extraction is done from the optic lens material.  
The reticle is screen-centered and the main camera gets aligned to weapon sway.  
The reticle is rendered on the main FPS camera through a command buffer to prevent smearing due to upscalers.  
To make it compatible with NVG shaders it renders at a different time if those are on:
- NVG active: `AfterForwardAlpha`
- NVG off: `AfterEverything`

The rear lens mesh is reused as stencil input to drive reticle and vignette rendering.  

Meshcutting still driven by per-scope settings (pls help).  
If the initial cut fails and produces no entries, we retry for a few frames (might get ditched down the line).  
Mesh data is cached in RAM for reuse.  

Magnification is derived from scope/template zoom data instead of using the FOVs returned by the OpticCamera component of the scope (variable across magnifications, BSG jank).  
WIP keeps the fixed 50° zoom baseline for magnified modes, but now also has a 1x override path so 1x modes behave closer to the player settings FOV instead of feeling weirdly over-zoomed.

Weapon scaling is ribcage-scale black magic, with auto-tuned baseline/strength according to the player settings FOV.  


<h1>Current features:</h1>    
  
Reliable magnification through template zoom / scope data fallback.  
1x FOV override instead of forcing everything through the same zoom path.  
Freelook FOV restore.  
Fika ping / healthbar / nameplate compatibility while ADS.  
Weapon scaling auto-tuned from player settings FOV.  
Lens-based stencil masking for reticle visibility.  
Scope shadow / vignette tied into the same stencil-aware render path.  
In-memory mesh caching per weapon / raid flow.  
Customizable LOD bias + culling settings while ADSing.  
Whitelist / auto-bypass system.  
Auto-exclusion of night vision and thermal scopes.  
Per-scope meshcutter settings.  
Per-scope reticle size.  

<h1>Roadmap:</h1>    
  
-~~Fix Fika ping system / healthbar / nameplates while ADS~~ Done  
-~~Make FOV on freelook exit equal to FOV before freelook enter~~ Done  
-~~Find a way to make hybrid / all-in-one optics bypass correctly~~ Done with scope-mode bypass config  
-~~Improve reticle masking using the actual visible lens instead of only housing heuristics~~ Done  
-~~Remove old cache system / rework it~~ Reworked into in-memory cache flow  
-~~Improve weapon scaling method~~ Auto-tuned by player settings FOV  
-~~Hide more useless options under advanced BepInEx settings~~ Mostly done  
-Solve FOV restore on pose changes  
-Tune more per-scope mesh surgery presets  
-Keep cleaning old AI-jank / dead code paths until the whole thing is readable without archaeology  


# How to use:  
Coming soon

<h1>FPS Comparison:</h1>     
  
![alt text](https://github.com/Fiodorwellfme/PiP-Disabler/blob/main/images/Screenshot%202026-03-07%20173054.png "Picture in Picture Scope")  
![alt text](https://github.com/Fiodorwellfme/PiP-Disabler/blob/main/images/Screenshot%202026-03-07%20173102.png "PiP-Disabler")  
![alt text](https://github.com/Fiodorwellfme/PiP-Disabler/blob/main/images/Screenshot%202026-03-07%20174242.png "Picture in Picture Scope")  
![alt text](https://github.com/Fiodorwellfme/PiP-Disabler/blob/main/images/Screenshot%202026-03-07%20174303.png "PiP-Disabler")  
