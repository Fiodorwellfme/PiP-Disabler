<h1>Concept:</h1>    
  
Runtime meshcutting combined with FOV zoom (incompatible with Fontaine's FOV fix) to reduce the FPS cost of ADSing with optic scopes.

# How it works (In progress):  

When ADS, identify if scope is optic or not.  
If Optic then check if thermal or NVG or variable scope or part of blacklisted scopes. If any of those is true then bypass mod.  
Extract reticle, flip the texture horizontally and display it center screen in a command buffer.  
  
Set a 4 diameter cutting plane along the scope axis using the lens closest to the camera as the origin (this has to be configured manually for each scope).  
The meshcutter cuts through all the meshes that are found between hands and the scope and the resulting meshes are then cached for reusal during the raid.  
  
Once the mesh has been cut, use it to create a mask that hides the reticle when it intersects with scope housing/Weapon mesh.  

Set main camera rotation axis aligned with weapon axis to keep aiming consistent and reduce sway.  

Magnification is searched for in Template.Zooms to be consistent across scopes, sometimes it may not be the same as the PiP scopes because of BSG Jank.  
Calculate FOV by dividing reference FOV (50°) by magnification and then set main cam FOV to that value.  

Scaling is done using the ribcage scaling method with some dark magic multipliers and offsets.

<h1>Current features:</h1>    
  
Reliable magnification level through scope json (may not be exactly same as PiP).  
Weapon scaling using absolute jank approximations.  
Customizable LODbias + Culling settings while ADSing.  
Whitelist system.  
Mesh caching in raid only.  
Auto-exclusion of nightvision and thermal scopes.  
Customizable ADS speed.  
Per scope meshcutter settings.  
Per scope reticle size.  

<h1>Roadmap:</h1>    
  
-~~Remove cache system or rework it, performance impact needs to be inspected~~ Auto delete cache at end of raid option  
-~~More targeted meshes (weapon,attachments...)~~ Done  
-~~Find way to work across more scopes~~ Done  
-~~More consistent zoom FOVs~~ Done, using the multiplier stored in the item json  
-~~Make default whitelist~~ Auto disable for variable scopes, NV and thermal scopes  
-~~Remove useless options/code~~ Done  
-~~Support variable scopes~~ lmao fuckoff  
-~~Improve weapon scaling offset/multiplier or method~~ Feels right  
-~~Improve reticle scaling~~ Per scope reticle size  
-~~Make meshcutting more precise.~~ Per scope meshcutting   
-~~Add whitelist system~~ Done   
-~~Add per scope config~~ **(maybe even per mag level ?)**    
-~~Hide reticle when swaying across scope housing/weapon~~ Done  
-~~Find way to make vignette stick to scope.~~ Kind of, all the guns scale approximately the same so one vignette/shadow size fits all  
-~~Release 0.1~~  
-Try to make lens transparent + render reticle on it instead of the center of screen + camera alignment method  
-Hide multiple options under advanced BepInEx settings  
-Find best settings for different scopes. (Help welcome)  

<h1>FPS Comparison:</h1>     
  
![alt text](https://github.com/Fiodorwellfme/PiP-Disabler/blob/main/images/Screenshot%202026-03-07%20173054.png "Picture in Picture Scope")  
![alt text](https://github.com/Fiodorwellfme/PiP-Disabler/blob/main/images/Screenshot%202026-03-07%20173102.png "PiP-Disabler")  
![alt text](https://github.com/Fiodorwellfme/PiP-Disabler/blob/main/images/Screenshot%202026-03-07%20174242.png "Picture in Picture Scope")  
![alt text](https://github.com/Fiodorwellfme/PiP-Disabler/blob/main/images/Screenshot%202026-03-07%20174303.png "PiP-Disabler")  
